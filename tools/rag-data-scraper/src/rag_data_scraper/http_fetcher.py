from __future__ import annotations

import asyncio
from dataclasses import dataclass, replace
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
import ipaddress
import random
import socket
import time
from typing import Iterable, Mapping, Optional
from urllib.parse import quote, unquote, urljoin, urlsplit, urlunsplit

import httpx


class UnsafeUrlError(ValueError):
    pass


class RedirectPolicyError(UnsafeUrlError):
    def __init__(self, source_url: str, target_url: str, reason: str) -> None:
        super().__init__(
            f"Blocked redirect {source_url} -> {target_url}: {reason}"
        )
        self.source_url = source_url
        self.target_url = target_url


class ResponseTooLargeError(ValueError):
    pass


class FetchRequestError(RuntimeError):
    def __init__(
        self,
        message: str,
        *,
        attempt_count: int,
        elapsed_ms: int,
    ) -> None:
        super().__init__(message)
        self.attempt_count = attempt_count
        self.elapsed_ms = elapsed_ms


class HttpStatusError(RuntimeError):
    def __init__(
        self,
        status_code: int,
        url: str,
        *,
        attempt_count: int = 1,
        elapsed_ms: int = 0,
    ) -> None:
        super().__init__(f"HTTP {status_code} for {url}")
        self.status_code = status_code
        self.url = url
        self.attempt_count = attempt_count
        self.elapsed_ms = elapsed_ms


@dataclass(frozen=True)
class FetchResponse:
    requested_url: str
    final_url: str
    status_code: int
    headers: Mapping[str, str]
    content: bytes
    attempt_count: int = 1
    elapsed_ms: int = 0
    redirect_chain: tuple[str, ...] = ()


class UrlPolicy:
    def __init__(
        self,
        allowed_hosts: Optional[Iterable[str]] = None,
        *,
        allow_http: bool = False,
        allow_related_asset_hosts: bool = False,
    ) -> None:
        exact_hosts = {
            host.strip().lower().rstrip(".")
            for host in (allowed_hosts or [])
            if host and host.strip()
        }
        self.allowed_hosts = set(exact_hosts)
        if allow_related_asset_hosts:
            self.allowed_hosts.update(self._related_asset_hosts(exact_hosts))
        self.allow_http = allow_http

    @staticmethod
    def _related_asset_hosts(hosts: set[str]) -> set[str]:
        """Infer only conventional public asset aliases for a site's root."""
        asset_prefixes = {
            "cms",
            "static",
            "cdn",
            "media",
            "files",
            "download",
            "uploads",
        }
        conventional_entrypoints = {"m", "mobile", "www"}
        common_second_level = {"ac", "co", "com", "edu", "gov", "net", "org"}
        related: set[str] = set()
        for host in hosts:
            labels = host.split(".")
            if len(labels) < 2:
                continue
            root_size = 2
            if (
                len(labels) >= 3
                and len(labels[-1]) == 2
                and labels[-2] in common_second_level
            ):
                root_size = 3
            root = ".".join(labels[-root_size:])
            prefix = labels[:-root_size]
            if prefix and (len(prefix) != 1 or prefix[0] not in conventional_entrypoints):
                continue
            related.add(root)
            related.update(f"{name}.{root}" for name in conventional_entrypoints)
            related.update(f"{name}.{root}" for name in asset_prefixes)
        return related

    def normalize_and_validate(self, raw_url: str) -> str:
        if not raw_url or len(raw_url) > 2048:
            raise UnsafeUrlError("URL is empty or exceeds 2048 characters")

        parsed = urlsplit(unquote(raw_url.strip()))
        allowed_schemes = {"https"}
        if self.allow_http:
            allowed_schemes.add("http")
        if parsed.scheme.lower() not in allowed_schemes:
            raise UnsafeUrlError("Only approved HTTP(S) URLs are allowed")
        if parsed.username or parsed.password:
            raise UnsafeUrlError("Credentials in URLs are not allowed")
        if not parsed.hostname:
            raise UnsafeUrlError("URL must include a hostname")

        hostname = parsed.hostname.lower().rstrip(".")
        if self.allowed_hosts and hostname not in self.allowed_hosts:
            raise UnsafeUrlError(f"Host '{hostname}' is outside the crawl scope")
        self._reject_non_public_addresses(hostname, parsed.port)

        host = parsed.netloc.lower()
        safe_path = quote(parsed.path or "/", safe="/:~%+")
        safe_query = quote(parsed.query, safe="=&?:~%+")
        return urlunsplit((parsed.scheme.lower(), host, safe_path, safe_query, ""))

    @staticmethod
    def _reject_non_public_addresses(hostname: str, port: Optional[int]) -> None:
        if hostname == "localhost" or hostname.endswith(".localhost"):
            raise UnsafeUrlError("Loopback hosts are not allowed")

        try:
            literal = ipaddress.ip_address(hostname)
            addresses = [literal]
        except ValueError:
            try:
                resolved = socket.getaddrinfo(
                    hostname,
                    port or 443,
                    type=socket.SOCK_STREAM,
                )
            except socket.gaierror as exc:
                raise UnsafeUrlError(f"Unable to resolve host '{hostname}'") from exc
            addresses = []
            for item in resolved:
                address = ipaddress.ip_address(item[4][0].split("%", 1)[0])
                if address not in addresses:
                    addresses.append(address)

        if not addresses or any(not address.is_global for address in addresses):
            raise UnsafeUrlError(
                "Private, loopback, link-local or reserved addresses are not allowed"
            )


class SafeHttpFetcher:
    """Reusable HTTP session with bounded retries, rate limits, and URL safety."""

    RETRY_STATUSES = frozenset({408, 425, 429, 500, 502, 503, 504})

    def __init__(
        self,
        policy: UrlPolicy,
        *,
        timeout_seconds: float = 30.0,
        max_response_bytes: int = 25 * 1024 * 1024,
        max_redirects: int = 5,
        max_attempts: int = 3,
        backoff_base_seconds: float = 0.5,
        max_backoff_seconds: float = 8.0,
        per_host_delay_seconds: float = 0.2,
        per_host_max_concurrent: int = 2,
        user_agent: str = "DigitalOps-RAG-Crawler/1.0",
        client: Optional[httpx.AsyncClient] = None,
    ) -> None:
        if max_response_bytes <= 0:
            raise ValueError("max_response_bytes must be positive")
        if max_attempts < 1:
            raise ValueError("max_attempts must be positive")
        if backoff_base_seconds < 0 or max_backoff_seconds < 0:
            raise ValueError("retry delays cannot be negative")
        if per_host_delay_seconds < 0 or per_host_max_concurrent < 1:
            raise ValueError("per-host limits are invalid")
        self.policy = policy
        self.max_response_bytes = max_response_bytes
        self.max_redirects = max_redirects
        self.max_attempts = max_attempts
        self.backoff_base_seconds = backoff_base_seconds
        self.max_backoff_seconds = max_backoff_seconds
        self.per_host_delay_seconds = per_host_delay_seconds
        self.per_host_max_concurrent = per_host_max_concurrent
        self._host_locks: dict[str, asyncio.Lock] = {}
        self._host_semaphores: dict[str, asyncio.Semaphore] = {}
        self._host_last_started: dict[str, float] = {}
        self._owns_client = client is None
        self._client = client or httpx.AsyncClient(
            timeout=timeout_seconds,
            follow_redirects=False,
            headers={"User-Agent": user_agent},
            limits=httpx.Limits(
                max_connections=max(4, per_host_max_concurrent * 4),
                max_keepalive_connections=max(2, per_host_max_concurrent * 2),
            ),
        )

    async def _wait_for_host(self, url: str) -> asyncio.Semaphore:
        hostname = (urlsplit(url).hostname or "").lower()
        lock = self._host_locks.setdefault(hostname, asyncio.Lock())
        semaphore = self._host_semaphores.setdefault(
            hostname,
            asyncio.Semaphore(self.per_host_max_concurrent),
        )
        await semaphore.acquire()
        try:
            async with lock:
                now = time.monotonic()
                delay = self.per_host_delay_seconds - (
                    now - self._host_last_started.get(hostname, 0.0)
                )
                if delay > 0:
                    await asyncio.sleep(delay)
                self._host_last_started[hostname] = time.monotonic()
            return semaphore
        except Exception:
            semaphore.release()
            raise

    @staticmethod
    def _retry_after_seconds(headers: Mapping[str, str]) -> Optional[float]:
        raw_value = headers.get("Retry-After") or headers.get("retry-after")
        if not raw_value:
            return None
        try:
            return max(0.0, float(raw_value.strip()))
        except ValueError:
            try:
                retry_at = parsedate_to_datetime(raw_value)
                if retry_at.tzinfo is None:
                    retry_at = retry_at.replace(tzinfo=timezone.utc)
                return max(
                    0.0,
                    (retry_at - datetime.now(timezone.utc)).total_seconds(),
                )
            except (TypeError, ValueError, OverflowError):
                return None

    def _backoff_seconds(
        self,
        attempt_count: int,
        headers: Optional[Mapping[str, str]] = None,
    ) -> float:
        retry_after = self._retry_after_seconds(headers or {})
        if retry_after is not None:
            return min(retry_after, self.max_backoff_seconds)
        exponential = self.backoff_base_seconds * (2 ** (attempt_count - 1))
        jitter = random.uniform(0.0, max(0.001, exponential * 0.25))
        return min(self.max_backoff_seconds, exponential + jitter)

    async def _fetch_once(
        self,
        requested_url: str,
        current_url: str,
        headers: Optional[Mapping[str, str]],
    ) -> FetchResponse:
        redirect_chain: list[str] = []
        for redirect_count in range(self.max_redirects + 1):
            semaphore = await self._wait_for_host(current_url)
            try:
                async with self._client.stream(
                    "GET",
                    current_url,
                    headers=dict(headers or {}),
                ) as response:
                    response_headers = dict(response.headers)

                    # httpx intentionally includes 304 in ``is_redirect`` even
                    # though a conditional-cache response has no Location. A
                    # 304 is terminal for this request and must reach the
                    # adapter so it can reuse the verified cached artifact.
                    if response.status_code == 304:
                        return FetchResponse(
                            requested_url=requested_url,
                            final_url=str(response.url),
                            status_code=response.status_code,
                            headers=response_headers,
                            content=b"",
                            redirect_chain=tuple(redirect_chain),
                        )

                    if response.has_redirect_location:
                        if redirect_count >= self.max_redirects:
                            raise UnsafeUrlError("Maximum redirect count exceeded")
                        location = response.headers["Location"]
                        target_url = urljoin(current_url, location)
                        parsed_target = urlsplit(target_url)
                        if (
                            urlsplit(current_url).scheme.lower() == "https"
                            and parsed_target.scheme.lower() == "http"
                        ):
                            target_url = urlunsplit(
                                (
                                    "https",
                                    parsed_target.netloc,
                                    parsed_target.path,
                                    parsed_target.query,
                                    "",
                                )
                            )
                        try:
                            current_url = self.policy.normalize_and_validate(
                                target_url
                            )
                        except UnsafeUrlError as exc:
                            raise RedirectPolicyError(
                                str(response.url),
                                target_url,
                                str(exc),
                            ) from exc
                        redirect_chain.append(current_url)
                        continue

                    if response.status_code in self.RETRY_STATUSES:
                        return FetchResponse(
                            requested_url=requested_url,
                            final_url=str(response.url),
                            status_code=response.status_code,
                            headers=response_headers,
                            content=b"",
                            redirect_chain=tuple(redirect_chain),
                        )

                    content_length = response.headers.get("Content-Length")
                    if content_length:
                        try:
                            declared_length = int(content_length)
                        except ValueError:
                            declared_length = 0
                        if declared_length > self.max_response_bytes:
                            raise ResponseTooLargeError(
                                f"Response exceeds {self.max_response_bytes} bytes"
                            )

                    body = bytearray()
                    async for chunk in response.aiter_bytes():
                        body.extend(chunk)
                        if len(body) > self.max_response_bytes:
                            raise ResponseTooLargeError(
                                f"Response exceeds {self.max_response_bytes} bytes"
                            )
                    return FetchResponse(
                        requested_url=requested_url,
                        final_url=str(response.url),
                        status_code=response.status_code,
                        headers=response_headers,
                        content=bytes(body),
                        redirect_chain=tuple(redirect_chain),
                    )
            finally:
                semaphore.release()
        raise UnsafeUrlError("Redirect processing failed")

    async def fetch(
        self,
        raw_url: str,
        *,
        headers: Optional[Mapping[str, str]] = None,
    ) -> FetchResponse:
        requested_url = raw_url
        safe_url = self.policy.normalize_and_validate(raw_url)
        started = time.perf_counter()
        last_error: Optional[httpx.TransportError] = None

        for attempt_count in range(1, self.max_attempts + 1):
            try:
                response = await self._fetch_once(
                    requested_url,
                    safe_url,
                    headers,
                )
            except httpx.TransportError as exc:
                last_error = exc
                if attempt_count >= self.max_attempts:
                    break
                await asyncio.sleep(self._backoff_seconds(attempt_count))
                continue

            elapsed_ms = int((time.perf_counter() - started) * 1000)
            response = replace(
                response,
                attempt_count=attempt_count,
                elapsed_ms=elapsed_ms,
            )
            if (
                response.status_code not in self.RETRY_STATUSES
                or attempt_count >= self.max_attempts
            ):
                return response
            await asyncio.sleep(
                self._backoff_seconds(attempt_count, response.headers)
            )

        elapsed_ms = int((time.perf_counter() - started) * 1000)
        detail = type(last_error).__name__ if last_error else "transport failure"
        raise FetchRequestError(
            f"HTTP transport failed after {self.max_attempts} attempts: {detail}",
            attempt_count=self.max_attempts,
            elapsed_ms=elapsed_ms,
        ) from last_error

    async def get(
        self,
        raw_url: str,
        *,
        headers: Optional[Mapping[str, str]] = None,
    ) -> FetchResponse:
        return await self.fetch(raw_url, headers=headers)

    async def __aenter__(self) -> "SafeHttpFetcher":
        return self

    async def __aexit__(self, exc_type, exc, traceback) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()
