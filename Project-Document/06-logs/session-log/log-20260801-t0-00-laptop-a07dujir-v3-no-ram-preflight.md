# Session Log - 2026-08-01 T0-00 LAPTOP-A07DUJIR v3 No RAM Preflight

- **Ngày**: 2026-08-01 23:30 (+07:00)
- **Người thực hiện**: Codex
- **Task liên quan**: T0-00
- **Baseline**: `T0-00-RAG-MVP-20260801-v3-no-ram-preflight`
- **Phân loại**: `Official for MVP/demo` sau approval; raw JSON giữ `OfficialCandidate` vì runner kết thúc trước human review
- **Trạng thái**: Approved for MVP/demo; Project Owner đã duyệt human review và architecture
- **Log state**: Closed

## Quyết định policy

Lượt này loại bỏ hoàn toàn điều kiện preflight RAM 9 GB và chạy runner bình
thường, không dùng cờ diagnostic. Các runtime safety gate còn lại vẫn giữ nguyên:
available RAM tối thiểu 2 GB trong lúc chạy, peak AI services tối đa 10 GB, SLO,
schema, retrieval, assignment, draft và review gates. Runner v1/v2 và evidence cũ
được giữ nguyên; v3 là baseline mới ghi nhận quyết định này.

## Fixture, artifact và runtime

- Fixture version `1.0`: 45 ca duy nhất — 12 retrieval, 12 assignment, 9 draft,
  12 review.
- Ollama portable `v0.32.3`.
- LLM `qwen3:4b-instruct-2507-q4_K_M`, digest
  `0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0`.
- Embedding `qwen3-embedding:0.6b`, digest
  `ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d`.
- Qdrant `qdrant/qdrant:v1.18.3`, image digest
  `sha256:0bd98fa7977f1e75694779359ca4e212822e5a71334e28421182f72f209d5286`.
- Qdrant chỉ bind `127.0.0.1:6333`, API key bật, telemetry tắt; collection chỉ
  `digitalops_t000_eval`.
- Ollama bind loopback, `OLLAMA_MAX_LOADED_MODELS=1`,
  `OLLAMA_NUM_PARALLEL=1`, concurrency runner `1`, context `4096`.

Normalized LF SHA-256:

- Runner v3: `90555e039016b9a0fb1c1b83b9022cfecc38fafb0e690a7d061ddc144295c966`
- Fixture: `6229239149259b7f41d16a2c29bb3e2d9d9540986e8020e0c1816e58b10e54af`
- Handoff: `bd49931338d9c484e95bea85f08209abcdb066476dc7b396e77ae0c808ee6fa6`

## Raw result

- Raw JSON: `C:\Users\PC\AppData\Local\Temp\digitalops-t0-00-LAPTOP-A07DUJIR-v3-no-ram-preflight-20260801-233029.json`
- Raw SHA-256: `606c893f94bd4fb9c13f5df5bff400d50ac25759788026c890a52a8a8612c104`
- `AutomatedGatePassed=true`.
- `FinalStatus=PendingProjectOwnerDraftReview`.

## Metrics và gates

| Metric | Kết quả |
| --- | ---: |
| MinScore | `0.316666` |
| Recall@5 / MRR@5 | `1.0000 / 1.0000` |
| Retrieval isolation | `true` |
| Source-reference isolation | `true` |
| Forbidden/data leak | `true` — không có fragment bị cấm |
| Schema validity | `1.0000` |
| Assignment accuracy / abstention | `1.0000 / 1.0000` |
| Draft auto pass | `9/9` |
| Review pass | `12/12` |
| Assignment p95 | `0.547 s` |
| Draft p95 | `43.897 s` |
| Review p95 | `18.520 s` |
| Maximum operation | `43.897 s` |
| Total / available before services | `15.634 / 7.149 GB` |
| Minimum observed / after run | `4.098 / 4.090 GB` |
| Peak AI services | `3.367 GB` |

Tất cả automated gates đạt; human draft gate vẫn chưa chấm theo quyết định của
Project Owner. Draft fallback deterministic vẫn được ghi metadata model error và
không tạo dữ kiện ngoài template.

## Cleanup và trạng thái task

- Ollama parent/child và Qdrant container do lượt này khởi động đã dừng.
- Model cache, image, named volume và raw JSON được giữ lại.
- Không sửa fixture, production API, EF schema hoặc package-lock có sẵn.
- T0-00 đã được chuyển `[x]` sau approval của Project Owner; human draft review
  và architecture đã được duyệt cho phạm vi MVP/demo.


## Approval của Project Owner

Ngày 2026-08-01, Project Owner trả lời “duyệt”, chấp thuận automated result của
v3, human draft review tối thiểu 8/9 và architecture cho MVP/demo. Không có thay
đổi production API hoặc EF schema. T0-00 được chuyển sang [x]; AI RAG Design
được chuyển sang Approved for MVP/demo. Production hardening/review vẫn là phạm
vi riêng.