# Session log - RAG scraper pagination, redirect và Inspector fixes

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Liên quan: `log-20260803-rag-scraper-hardening.md` (giữ nguyên lịch sử đã đóng)
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Sửa lỗi crawler phát sinh trên job thực tế, giảm dữ liệu boilerplate/trùng lặp,
nhóm vấn đề trong RAG Inspector và cho phép một seed URL tự lần theo chuỗi phân
trang trong giới hạn rõ ràng.

## Bằng chứng nguyên nhân

- Job `ID1`/`ID2` có 23 lỗi tệp đính kèm do URL HTTPS trả redirect sang
  `http://static.mattran.org.vn/...`; 1 URL rác `footer__logo` được lấy từ footer.
- Ba nhóm với tám chunk trùng là nội dung header/footer/menu dùng lại trên nhiều
  trang, không phải nội dung bài viết.
- Cấu hình depth cũ chỉ đi được những trang phân trang xuất hiện trực tiếp trên
  seed; chuỗi trang kế tiếp không tiếp tục khi đã chạm content depth.

## Quyết định và thay đổi

- Redirect hạ từ HTTPS sang HTTP không được tải trực tiếp. Crawler chỉ nâng đích
  đến HTTPS rồi áp lại allowlist host, DNS/IP SSRF policy, byte limit và kiểm tra
  magic PDF/DOCX.
- `generic_web` chỉ mở rộng sang các alias tài nguyên công khai thông dụng cùng
  registrable domain (`static`, `cdn`, `media`, `files`, `download`, `uploads`);
  adapter chuyên biệt vẫn dùng allowlist chính xác.
- Loại link trong `header`, `footer`, `nav`, `aside` và loại boilerplate trước
  extraction/chunking. File `.doc` cũ được bỏ qua; phạm vi attachment là PDF/DOCX.
- Thêm nhận diện query/path phân trang, `max_pagination_pages` (mặc định 25),
  giữ pagination ở cùng content depth và vẫn bị giới hạn bởi tổng `limit`.
- Canonical hóa `?page=1` thành URL danh sách gốc; bài/tài liệu được ưu tiên trước
  các trang danh sách kế tiếp.
- Dashboard/API/CLI nhận giới hạn phân trang. RAG Inspector nhóm crawler error và
  duplicate chunk cùng nguyên nhân thành một dòng, có occurrence count và URL mẫu.

## Kiểm tra

- `pytest -q`: `40 passed in 23.86s`.
- Live fetch PDF và DOCX từ `m.mattran.org.vn` đã nâng redirect sang
  `https://static.mattran.org.vn/...` và nhận đúng MIME/magic.
- Replay 23 raw HTML của `ID2`: từ 3 nhóm/8 instance chunk trùng xuống 0 nhóm.
- Live crawl với đúng một seed URL và `max_depth=0` đã đi tiếp chuỗi trang phân
  trang ở depth 0; test hồi quy xác nhận page 2 -> page 3 và link bài ở depth 1.
- Dashboard local trả HTTP 200 và render trường `paginationLimitInput`; test API
  xác nhận payload `max_pagination_pages` được truyền đến crawl worker.

## Phạm vi còn lại

- Các package `ID1`/`ID2` là dữ liệu lịch sử và không bị tự sửa. Cần tạo job ID
  mới để áp dụng fetch/extraction/frontier mới.
- Crawler theo link phân trang HTML thông thường; chưa render JavaScript cho SPA,
  chưa click nút infinite scroll/load-more và chưa giải CAPTCHA.
