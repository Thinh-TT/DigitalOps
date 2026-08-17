from dataclasses import dataclass
import hashlib
import re
from typing import Callable, List, Optional, Tuple
from uuid import UUID

from ..extractors.base import BlockType, ExtractedDocument
from ..models.chunk import Chunk, ChunkACL, ChunkSet


TokenCounter = Callable[[str], int]


@dataclass(frozen=True)
class _Unit:
    start: int
    end: int
    heading_path: Optional[str]
    page_number: Optional[int]


class StructureChunker:
    """Create bounded chunks as exact contiguous slices of normalized text.

    The target is the preferred packing size, the soft maximum permits one
    semantic unit (for example a sentence) to remain intact, and the hard
    maximum is an invariant. Oversized semantic units fall back to word, then
    character boundaries.
    """

    def __init__(
        self,
        target_tokens: int = 448,
        soft_max_tokens: Optional[int] = None,
        overlap_tokens: Optional[int] = None,
        max_tokens: int = 512,
        tokenizer_name: str = "heuristic:vietnamese-word-1.3x",
        token_counter: Optional[TokenCounter] = None,
    ) -> None:
        if overlap_tokens is None:
            overlap_tokens = min(64, max(0, target_tokens // 4))
        if soft_max_tokens is None:
            soft_max_tokens = min(max_tokens, target_tokens + 32)
        if not (
            0
            <= overlap_tokens
            < target_tokens
            <= soft_max_tokens
            <= max_tokens
        ):
            raise ValueError(
                "chunk limits must satisfy "
                "0 <= overlap < target <= soft max <= max"
            )
        self.target_tokens = target_tokens
        self.soft_max_tokens = soft_max_tokens
        self.overlap_tokens = overlap_tokens
        self.max_tokens = max_tokens
        self.tokenizer_name = tokenizer_name
        self._token_counter = token_counter or self._estimate_tokens

    @staticmethod
    def _estimate_tokens(text: str) -> int:
        if not text:
            return 0
        return max(1, int(len(text.split()) * 1.3))

    def _count_tokens(self, text: str) -> int:
        return int(self._token_counter(text))

    @staticmethod
    def _trim_span(text: str, start: int, end: int) -> tuple[int, int]:
        while start < end and text[start].isspace():
            start += 1
        while end > start and text[end - 1].isspace():
            end -= 1
        return start, end

    def _sentence_spans(
        self,
        text: str,
        start: int,
        end: int,
    ) -> List[tuple[int, int]]:
        """Return sentence/list-line spans without changing source offsets."""
        value = text[start:end]
        boundaries = re.finditer(r"(?:(?<=[.!?…;])[ \t]+|\n+)", value)
        spans: List[tuple[int, int]] = []
        cursor = start
        for boundary in boundaries:
            sentence_end = start + boundary.start()
            sentence_start, sentence_end = self._trim_span(
                text, cursor, sentence_end
            )
            if sentence_start < sentence_end:
                spans.append((sentence_start, sentence_end))
            cursor = start + boundary.end()
        sentence_start, sentence_end = self._trim_span(text, cursor, end)
        if sentence_start < sentence_end:
            spans.append((sentence_start, sentence_end))
        return spans

    def _split_characters(
        self,
        text: str,
        start: int,
        end: int,
        heading_path: Optional[str],
        page_number: Optional[int],
        token_limit: int,
    ) -> List[_Unit]:
        units: List[_Unit] = []
        cursor = start
        while cursor < end:
            low = cursor + 1
            high = end
            best: Optional[int] = None
            while low <= high:
                candidate_end = (low + high) // 2
                if self._count_tokens(text[cursor:candidate_end]) <= token_limit:
                    best = candidate_end
                    low = candidate_end + 1
                else:
                    high = candidate_end - 1
            if best is None:
                raise ValueError(
                    "token counter cannot fit one character within hard limit"
                )
            units.append(_Unit(cursor, best, heading_path, page_number))
            cursor = best
        return units

    def _split_words(
        self,
        text: str,
        start: int,
        end: int,
        heading_path: Optional[str],
        page_number: Optional[int],
    ) -> List[_Unit]:
        """Split one oversized sentence near target; chars are last resort."""
        word_spans = list(re.finditer(r"\S+", text[start:end]))
        if not word_spans:
            return []

        units: List[_Unit] = []
        segment_start_index = 0
        while segment_start_index < len(word_spans):
            low = segment_start_index + 1
            high = len(word_spans)
            best: Optional[int] = None
            while low <= high:
                mid = (low + high) // 2
                candidate_start = start + word_spans[segment_start_index].start()
                candidate_end = start + word_spans[mid - 1].end()
                if (
                    self._count_tokens(text[candidate_start:candidate_end])
                    <= self.target_tokens
                ):
                    best = mid
                    low = mid + 1
                else:
                    high = mid - 1

            segment_start = start + word_spans[segment_start_index].start()
            if best is None:
                word_end = start + word_spans[segment_start_index].end()
                if (
                    self._count_tokens(text[segment_start:word_end])
                    <= self.soft_max_tokens
                ):
                    units.append(
                        _Unit(
                            segment_start,
                            word_end,
                            heading_path,
                            page_number,
                        )
                    )
                else:
                    units.extend(
                        self._split_characters(
                            text,
                            segment_start,
                            word_end,
                            heading_path,
                            page_number,
                            self.target_tokens,
                        )
                    )
                segment_start_index += 1
                continue

            segment_end = start + word_spans[best - 1].end()
            units.append(_Unit(segment_start, segment_end, heading_path, page_number))
            segment_start_index = best
        return units

    def _split_span(
        self,
        text: str,
        start: int,
        end: int,
        heading_path: Optional[str],
        page_number: Optional[int],
    ) -> List[_Unit]:
        if self._count_tokens(text[start:end]) <= self.soft_max_tokens:
            return [_Unit(start, end, heading_path, page_number)]

        units: List[_Unit] = []
        for sentence_start, sentence_end in self._sentence_spans(
            text, start, end
        ):
            if (
                self._count_tokens(text[sentence_start:sentence_end])
                <= self.soft_max_tokens
            ):
                units.append(
                    _Unit(
                        sentence_start,
                        sentence_end,
                        heading_path,
                        page_number,
                    )
                )
            else:
                units.extend(
                    self._split_words(
                        text,
                        sentence_start,
                        sentence_end,
                        heading_path,
                        page_number,
                    )
                )
        if not units:
            raise ValueError("unable to split oversized non-empty text block")
        return units

    def _build_units(
        self,
        document: ExtractedDocument,
        normalized_text: str,
    ) -> List[_Unit]:
        units: List[_Unit] = []
        heading_path: List[str] = []
        cursor = 0

        for index, block in enumerate(document.blocks):
            block_text = block.text
            if not block_text:
                continue
            if index > 0:
                separator = normalized_text[cursor:cursor + 2]
                if separator != "\n\n":
                    raise ValueError("normalized document block separator is inconsistent")
                cursor += 2
            start = cursor
            end = start + len(block_text)
            if normalized_text[start:end] != block_text:
                raise ValueError("normalized document blocks do not match normalized text")

            if block.block_type == BlockType.HEADING:
                level = max(1, block.heading_level or 1)
                heading_path = heading_path[: level - 1]
                heading_path.append(block_text)
            heading = " > ".join(heading_path) if heading_path else None
            units.extend(
                self._split_span(
                    normalized_text,
                    start,
                    end,
                    heading,
                    block.page_number,
                )
            )
            cursor = end

        if cursor != len(normalized_text):
            raise ValueError("normalized text contains bytes outside normalized blocks")
        return units

    def _make_chunk(
        self,
        chunk_set: ChunkSet,
        index: int,
        units: List[_Unit],
        normalized_text: str,
        acl: ChunkACL,
    ) -> Chunk:
        start = units[0].start
        end = units[-1].end
        text = normalized_text[start:end]
        token_count = self._count_tokens(text)
        if token_count > self.max_tokens:
            raise ValueError(
                f"chunk {index} has {token_count} tokens, above max {self.max_tokens}"
            )
        pages = sorted(
            {unit.page_number for unit in units if unit.page_number is not None}
        )
        return Chunk(
            chunk_set_id=chunk_set.chunk_set_id,
            chunk_index=index,
            text=text,
            token_count=token_count,
            character_start=start,
            character_end=end,
            content_sha256=hashlib.sha256(text.encode("utf-8")).hexdigest(),
            heading_path=units[-1].heading_path,
            page_numbers=pages,
            chunk_acl=acl,
        )

    def chunk(
        self,
        extracted_doc: ExtractedDocument,
        observation_id: UUID,
        job_id: str,
        acl: Optional[ChunkACL] = None,
        normalized_text: Optional[str] = None,
    ) -> Tuple[ChunkSet, List[Chunk]]:
        acl = acl or ChunkACL(
            allowed_roles=["public"],
            security_classification="internal",
        )
        normalized_text = normalized_text or "\n\n".join(
            block.text for block in extracted_doc.blocks
        )
        chunk_set = ChunkSet(
            observation_id=observation_id,
            job_id=job_id,
            chunking_strategy="contiguous_structure_sentence_aware_sliding",
            chunker_version="3.0.0",
            tokenizer_name=self.tokenizer_name,
            target_tokens=self.target_tokens,
            soft_max_tokens=self.soft_max_tokens,
            max_tokens=self.max_tokens,
            overlap_tokens=self.overlap_tokens,
            total_chunks=0,
        )

        chunks: List[Chunk] = []
        buffer: List[_Unit] = []
        for unit in self._build_units(extracted_doc, normalized_text):
            candidate = normalized_text[buffer[0].start:unit.end] if buffer else normalized_text[unit.start:unit.end]
            if buffer and self._count_tokens(candidate) > self.target_tokens:
                chunks.append(self._make_chunk(chunk_set, len(chunks), buffer, normalized_text, acl))
                overlap: List[_Unit] = []
                for prior in reversed(buffer):
                    proposed = [prior, *overlap]
                    overlap_text = normalized_text[proposed[0].start:proposed[-1].end]
                    if self._count_tokens(overlap_text) > self.overlap_tokens:
                        break
                    overlap = proposed
                buffer = overlap
                if buffer:
                    with_overlap = normalized_text[buffer[0].start:unit.end]
                    if self._count_tokens(with_overlap) > self.soft_max_tokens:
                        buffer = []
            buffer.append(unit)

        if buffer:
            chunks.append(self._make_chunk(chunk_set, len(chunks), buffer, normalized_text, acl))
        chunk_set.total_chunks = len(chunks)
        return chunk_set, chunks
