# Session log - RAG scraper hardening và chọn định dạng trước khi cào

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Tăng độ bền của crawler và cho phép người dùng chọn một trong 13 định dạng đầu ra trước khi bắt đầu cào. Định dạng được chọn phải được tạo tự động sau khi staging package hoàn tất, nhưng không khóa khả năng xuất lại sang định dạng khác.

## Quyết định và thay đổi

- Gom fetch HTTP vào client tái sử dụng theo job; thêm retry/backoff có `Retry-After`, giới hạn đồng thời/khoảng nghỉ theo host, redirect revalidation, giới hạn response và kiểm tra cấu trúc PDF/DOCX cơ bản.
- Thêm `CrawlPolicy` cho canonical URL, bỏ tracking query/fragment, lọc asset, ưu tiên tài liệu và bật/tắt PDF/DOC/DOCX.
- Lưu frontier theo job trong SQLite; checkpoint observation/chunks được ghi atomic và xác minh path/hash/relation/offset khi resume.
- Dùng `ETag`/`Last-Modified` cho conditional GET; cache `304` chỉ tái sử dụng khi cùng adapter và raw artifact còn đúng SHA-256.
- Dashboard/API/CLI nhận `export_format`, mặc định `chunks_jsonl`; file ưu tiên được giữ tại `storage/staging/<job_id>/exports/` cùng checksum `.sha256`.
- Auto-export thất bại không làm đổi một crawl package hợp lệ thành job crawl thất bại; trạng thái export được lưu riêng trong `job-metadata.json`.
- Sửa responsive form để select định dạng dài không gây tràn ngang ở viewport mobile.

## Kiểm tra

- `python -m compileall -q src`: đạt.
- `pytest -q`: `35 passed in 19.15s`.
- CLI help hiển thị `--export-format` và `--no-attachments`.
- Browser smoke local: dashboard render đủ 13 format; mặc định `chunks_jsonl`; đổi được sang `documents_pdf`; tắt được attachments; không có console error.
- Responsive smoke 390x844: sau bản vá, `pageWidth = viewportWidth = 375`, không còn horizontal overflow.

## Giới hạn còn lại

- Đây là hardening crawler và export workflow; chưa bổ sung browser-rendered crawling cho SPA, OCR engine mới, distributed queue hoặc production scheduler.
- Dashboard vẫn là local unauthenticated tool và chỉ được bind loopback theo CLI hiện tại.
