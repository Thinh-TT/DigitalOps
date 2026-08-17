# Session Log - RAG Scraper List Records and Legacy DOC

- **Ngày**: 2026-08-03
- **Người thực hiện**: Codex
- **Task liên quan**: Cải thiện crawler danh sách văn bản (chưa có ID trên Task Board)
- **Loại**: Implementation / Decision
- **Trạng thái**: Hoàn thành

## Bối cảnh

Job dùng seed `https://m.mattran.org.vn/van-ban-huong-dan.html`, limit 100 và
phân trang 40 chỉ tạo 58 observation dù website có hàng trăm dòng văn bản. Crawler
cũ coi mỗi trang danh sách là một tài liệu, bỏ `.doc` và loại anchor asset HTTP
trước khi có cơ hội nâng sang HTTPS.

## Quyết định và triển khai

- Thêm parser riêng cho danh sách Mặt trận: mỗi cặp dòng metadata/trích yếu tạo
  một primary record ổn định; trang HTML danh sách trở thành discovery-only.
- Tách document limit khỏi hard limit HTTP và pagination limit. Sau khi đủ output,
  crawler vẫn lần theo pagination để đếm tổng record; attachment có frontier kind
  riêng và không tiêu tốn document limit.
- Nâng anchor HTTP sang HTTPS chỉ khi host đã thuộc allowlist asset cùng domain;
  sau đó vẫn kiểm tra scheme, credentials, DNS công khai, redirect và size cap.
- Bổ sung alias `cms` cùng các alias asset hiện có; không cho phép host tùy ý.
- Hỗ trợ OLE DOC/RTF bằng LibreOffice headless trong temp directory, không dùng
  shell, có timeout, giới hạn output/expanded ZIP và kiểm tra DOCX trước extraction.
- Dashboard hiển thị riêng primary documents, limit, listing pages, attachments
  fetched và total observations. Metrics được giữ trong `job-metadata.json`.
- SQLite frontier thêm `resource_kind` với migration tương thích database hiện có.

## Kiểm tra đã thực hiện

- `python -m pytest -q`: 62 test pass, gồm parser record, document limit,
  extensionless attachment và legacy DOC conversion.
- Live `limit=20`, attachment tắt: tạo đúng 20 primary document từ record danh sách.
- Live kịch bản `limit=100`, pagination 40: đọc 37 trang có dữ liệu, phát hiện 547
  record và tạo đúng 100 primary document.
- `DxOs.Workers --validate-only` xác nhận package live hợp lệ: 100 observations,
  100 chunk sets và 100 chunks; không ghi PostgreSQL/Qdrant.
- Live legacy DOC thật: LibreOffice chuyển đổi thành công, tạo 1 observation/29
  chunks, không có crawler error.
- Playwright headless: form mới, mô tả limit, checkbox DOC, metrics row, 13 output
  format, CSP và console đều hợp lệ.

## Tác động và giới hạn

- Muốn xuất toàn bộ 547 primary record phải đặt limit ít nhất 547; limit 100 vẫn
  chỉ tạo 100 document nhưng dashboard báo tổng record đã phát hiện.
- LibreOffice làm tăng thời gian/CPU cho tài liệu `.doc`; file lỗi vẫn không làm
  mất record metadata/trích yếu đã lấy từ trang danh sách.
- Parser record hiện được khóa theo URL/cấu trúc của nguồn Mặt trận; nguồn danh
  sách khác tiếp tục dùng generic behavior cho đến khi có parser tương ứng.
