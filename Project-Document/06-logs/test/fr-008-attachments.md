# Mẫu thử FR-008 — Attachment incoming/outgoing

## Mục tiêu

Xác nhận upload/download/delete attachment qua API, kiểm tra thật nội dung file,
giới hạn kích thước, extraction status và quyền theo tài liệu cha.

## File mẫu

| File | Mục đích | Kỳ vọng ban đầu |
| --- | --- | --- |
| `TEST-text.pdf` | PDF có text layer, nhỏ | `Pending` |
| `TEST-document.docx` | DOCX hợp lệ | `Pending` |
| `TEST-sheet.xlsx` | XLSX hợp lệ | `Pending` |
| `TEST-image.jpg` | JPEG hợp lệ | `Unsupported` |
| `TEST-image.png` | PNG hợp lệ | `Unsupported` |
| `TEST-fake.pdf` | Text đổi đuôi PDF | Bị từ chối |
| `TEST-script.exe` | Extension không hỗ trợ | Bị từ chối |
| `TEST-over-limit.pdf` | Vượt `AttachmentStorage:MaxFileSizeBytes` | Bị từ chối |

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR008-01 | Upload incoming hợp lệ | Clerk upload PDF/DOCX/XLSX bằng multipart field `file` | `201`; metadata đúng; status `Pending`; storage path/extractedText không lộ ra response |
| FR008-02 | Upload ảnh | Clerk upload JPG/JPEG/PNG hợp lệ | `201`; status `Unsupported`; UI ghi Không hỗ trợ, không có nút OCR |
| FR008-03 | Upload outgoing hợp lệ | `DRAFTER-A` upload vào outgoing mình sở hữu ở Editing/AiDraft/ReviewFailed | `201`; attachment xuất hiện trong SCR-012 |
| FR008-04 | Magic bytes/MIME sai | Upload fake PDF hoặc Office zip hỏng | `415` hoặc validation error; không tạo metadata/file rác |
| FR008-05 | Extension/kích thước sai | Upload EXE và file vượt giới hạn | `415` cho loại file; `413` cho quá kích thước; UI bỏ file lỗi khỏi local list |
| FR008-06 | Download | BusinessAccess tải attachment hợp lệ | `200`; filename/MIME đúng; checksum/nội dung bằng file đã upload |
| FR008-07 | Delete đúng quyền | Clerk xóa incoming chưa Completed; owner xóa outgoing editable | `204`; GET download sau đó `404`; file vật lý và metadata được xóa nhất quán |
| FR008-08 | Parent locked | Thử upload/delete incoming Completed hoặc outgoing locked | `409`/`403` theo rule; attachment hiện có không đổi |
| FR008-09 | Non-owner/sai role | `DRAFTER-B` upload/delete attachment outgoing của A; Staff khác xóa incoming | `403`; không mutation |
| FR008-10 | Parent/attachment không tồn tại | Upload vào GUID cha lạ; download/delete GUID attachment lạ | `404 ProblemDetails`; không tạo file orphan |
| FR008-11 | Tên file nguy hiểm | Upload tên có path traversal hoặc quá 255 ký tự | Tên được sanitize hoặc request bị từ chối; không ghi ngoài storage root |

## Evidence và dọn dữ liệu

- Lưu attachment ID, status, filename và checksum; không lưu path nội bộ.
- Xóa từng attachment synthetic qua API trước khi cleanup document cha.
