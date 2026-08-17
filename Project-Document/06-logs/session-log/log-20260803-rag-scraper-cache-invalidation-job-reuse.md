# Session log - RAG scraper cache invalidation và Job ID reuse

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Liên quan: `log-20260803-rag-scraper-scanned-pdf-ocr.md` (giữ nguyên lịch sử đã đóng)
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Loại cảnh báo `Conditional cache miss`/`FileNotFoundError` sau khi xóa raw job và
bảo đảm xóa rồi tái sử dụng cùng Job ID không dính cache/frontier cũ.

## Bằng chứng nguyên nhân

- API xóa đúng `storage/raw/<job_id>` nhưng giữ hàng `CrawledResources` dùng
  chung. Hàng này còn `ETag`, `content_hash` và `raw_artifact_uri` trỏ vào file
  thuộc raw job đã xóa.
- Job sau gửi conditional request, nhận `304`, rồi `_cached_result` mới phát hiện
  file không còn. Crawler có fallback tải lại, nhưng phát sinh thêm request và
  traceback gây hiểu nhầm.

## Quyết định và thay đổi

- Conditional headers chỉ được tạo khi artifact nằm bên trong raw root, tồn tại
  và đúng SHA-256. File mất, bị sửa hoặc trỏ ra ngoài raw root làm cache pointer,
  validators và content hash bị xóa trong cùng transaction.
- Xóa job nhận exact resolved `raw_job_dir` và vô hiệu hóa mọi
  `CrawledResources.raw_artifact_uri` nằm dưới thư mục đó; hàng URL dùng chung
  vẫn được giữ để không phá lịch sử/identity ngoài phạm vi cần thiết.
- `CrawlJobs`, `CrawlFrontier`, `ResourceFetchHistory`, staging, raw và trạng
  thái memory tiếp tục được xóa như trước.
- Cache miss hiếm do race sau validation được log thành một warning ngắn có loại
  exception, không in traceback đường dẫn nội bộ; crawler vẫn refetch an toàn.

## Kiểm tra

- Regression test xác nhận cache hợp lệ vẫn gửi `If-None-Match`.
- File bị sửa, bị mất hoặc nằm ngoài raw root không gửi conditional headers và
  cache row được chuyển về `pending` với artifact fields rỗng.
- Xóa job giữ hàng URL dùng chung nhưng xóa validator/hash/raw pointer; tạo lại
  cùng Job ID bắt đầu với conditional headers rỗng.
- API delete xác nhận staging/raw/frontier/job state bị xóa và cache pointer được
  invalidated.
- `python -m pytest -q`: `58 passed in 21.81s`.

## Phạm vi còn lại

- Có một race nhỏ nếu raw file bị xóa đúng sau bước SHA-256 nhưng trước khi xử
  lý `304`; fallback refetch vẫn xử lý đúng và chỉ ghi warning ngắn.
- Hash raw trước conditional GET thêm I/O local, đổi lại tránh tái sử dụng cache
  hỏng và không gửi validator khi không có artifact thực để phục hồi.
