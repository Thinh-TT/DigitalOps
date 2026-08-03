# Session log - RAG scraper scanned PDF OCR

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Liên quan: `log-20260803-rag-scraper-304-resume-delete-fix.md` (giữ nguyên lịch sử đã đóng)
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Sửa 16 lỗi `extractor returned no text blocks` trên PDF scan của job `ID3` và
xử lý có kiểm soát một PDF vượt giới hạn tải 25 MiB.

## Bằng chứng nguyên nhân

- Cả 16 PDF lỗi không có text layer; các trang đầu trả `extract_text() = 0` và
  chứa ảnh scan toàn trang.
- Python package `pytesseract` có sẵn nhưng executable không nằm trong `PATH`.
  Tesseract thực tế nằm tại `C:\Program Files\Tesseract-OCR\tesseract.exe`.
- Extractor truyền raw image bytes trực tiếp vào `PIL.Image.open`, khiến bytes bị
  hiểu nhầm là tên file. Cách đúng là dùng `BytesIO`.
- Tesseract hệ thống chỉ có model `eng`, không có `vie`; cấu hình yêu cầu
  `vie+eng` nên OCR tiếng Việt không thể khởi tạo đúng.
- File `sachtbt_nguyen_phu_trong_LYUP.pdf` có kích thước 27.640.035 bytes
  (26,36 MiB), chỉ nhỉnh hơn giới hạn cũ, nhưng có 628 trang scan. Tăng giới hạn
  mà không giới hạn OCR sẽ tạo tải CPU kéo dài.

## Quyết định và thay đổi

- Tự tìm Tesseract trong `PATH` và các thư mục cài đặt Windows thông dụng; hỗ
  trợ `ocr.tessdata_dir` và fallback ngôn ngữ có cảnh báo.
- Sửa image decode bằng `BytesIO`; dùng một lần `image_to_data` để lấy cả text
  và confidence, thay vì chạy Tesseract hai lần trên mỗi ảnh.
- Thêm ngân sách OCR: tối đa 50 trang/tài liệu, 3 triệu pixel/ảnh và timeout 30
  giây/trang. Tài liệu vượt page cap được giữ kết quả một phần với quality
  `truncated` và metadata số trang processed/omitted/failed.
- Tăng hard response limit từ 25 MiB lên 32 MiB. URL policy, HTTPS/SSRF,
  concurrency, timeout và giới hạn response vẫn được giữ.
- Inspector có category/khuyến nghị riêng cho `response_too_large`,
  `ocr_unavailable` và `pdf_no_text`.
- Cài runtime model `vie.traineddata` từ official `tessdata_fast` commit
  `87416418657359cb625c412a48b6e1d6d41c29bd`; model nằm trong `storage/ocr`
  và bị loại khỏi Git.

## Kiểm tra

- OCR live PDF scan 3 trang: 3 blocks, 5.194 ký tự, confidence 0,941, không
  truncated, 0 trang lỗi; hoàn tất trong 13,61 giây.
- Live fetch file 27.640.035 bytes dưới trần 32 MiB thành công; xác nhận 628
  trang, không có text layer và có một ảnh scan ở trang đầu.
- SHA-256 `vie.traineddata`:
  `79df64caf7bcfb2a27df5042ecb6121e196eada34da774956995747636d5bfa1`.
- `python -m pytest -q`: `57 passed in 17.16s`.

## Phạm vi còn lại

- OCR scan tốn CPU hơn text extraction; job có nhiều PDF có thể chạy lâu hơn
  đáng kể. Page/pixel/timeout cap là chủ ý để giữ tool local ổn định.
- Job `ID3` là package lịch sử. Cần xóa/tạo lại hoặc dùng Job ID mới để chạy các
  PDF lỗi qua OCR mới.
