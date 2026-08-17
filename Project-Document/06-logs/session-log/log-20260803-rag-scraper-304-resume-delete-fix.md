# Session log - RAG scraper 304, resume và xóa job

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Liên quan: `log-20260803-rag-scraper-pagination-and-redirect-fixes.md` (giữ nguyên lịch sử đã đóng)
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Sửa nhóm lỗi `UnsafeUrlError/url_policy` xuất hiện khi cào attachment từ
`m.mattran.org.vn`, ngăn resume chạy lại lỗi terminal cũ và bảo đảm nút xóa job
loại bỏ toàn bộ trạng thái riêng của job thay vì chỉ xóa staging.

## Bằng chứng nguyên nhân

- URL attachment trả `301` từ host `m` sang host `static`; conditional request ở
  đích trả `304 Not Modified` không có `Location` theo đúng chuẩn.
- `httpx.Response.is_redirect` cũng trả `True` cho status `304`. Fetcher kiểm tra
  redirect trước 304 nên phát sinh sai thông báo `Redirect response did not
  include Location` và Inspector gắn nhầm thành lỗi URL policy.
- `prepare_frontier` cũ đưa cả `running` và `failed` về `pending`, làm các lỗi
  `.doc`/404 terminal từ lần chạy trước bị cào lại khi dùng lại Job ID.
- API xóa job cũ chỉ xóa `storage/staging/<job_id>` và bộ nhớ tiến trình; raw,
  checkpoint, `CrawlJobs`, `CrawlFrontier` và fetch history vẫn tồn tại.

## Quyết định và thay đổi

- Xử lý `304` trước nhánh redirect và trả kết quả rỗng cho adapter tái sử dụng
  raw cache đã xác minh SHA-256. Chỉ theo redirect khi response thực sự có
  `Location`; mọi redirect target vẫn phải qua HTTPS upgrade, host allowlist và
  kiểm tra DNS/IP SSRF hiện có.
- Resume chỉ đưa frontier `running` (bị gián đoạn) về `pending`; `failed` là
  terminal. Pending URL được kiểm tra lại bằng policy hiện tại trước khi claim,
  vì vậy `.doc` tồn đọng chuyển thành `skipped`.
- Inspector tách `redirect_missing_location` và `redirect_limit` khỏi nhóm
  `url_policy`, kèm khuyến nghị đúng nguyên nhân.
- Xóa job giờ xóa staging, raw/checkpoint, fetch history, frontier và CrawlJobs;
  cache `CrawledResources` dùng chung giữa các job được giữ lại. Dashboard mô tả
  đúng phạm vi xóa.

## Kiểm tra

- Regression tests mới bao phủ chuỗi `301 -> HTTPS static host -> 304`, resume
  failed/running, stale `.doc`, phân nhóm Inspector, state deletion và API delete.
- `python -m pytest -q`: `53 passed in 18.65s`.
- Live replay bằng fetcher thật với ETag đã lưu nhận `304`, final host
  `static.mattran.org.vn`, một redirect hop, không còn `UnsafeUrlError`.
- Hai web instance local ở cổng `8000` và `8011` đã được khởi động lại sau khi
  xác nhận không có job đang chạy; cả hai trả HTTP 200.

## Phạm vi còn lại

- Preview lịch sử không tự thay đổi. Xóa job cũ trên dashboard rồi tạo lại, hoặc
  dùng Job ID mới, để tạo package/Inspector bằng pipeline đã sửa.
- `.doc` nhị phân cũ vẫn không được extract; crawler chủ động bỏ qua và tiếp tục
  hỗ trợ PDF/DOCX.
