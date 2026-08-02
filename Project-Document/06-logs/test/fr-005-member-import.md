# Mẫu thử FR-005 — Import hội viên từ Excel

## Mục tiêu

Xác nhận tải template và import XLSX theo cơ chế all-or-nothing, báo lỗi chính xác
theo dòng/cột và không nhận file ngoài giới hạn.

## Chuẩn bị file thử

Tải template từ `GET /members/import-template`; không tự tạo workbook khác cấu trúc.
Tạo các bản sao:

| File | Nội dung |
| --- | --- |
| `TEST-members-valid.xlsx` | 3 dòng hợp lệ, phone/email duy nhất |
| `TEST-members-one-invalid.xlsx` | 2 dòng hợp lệ và 1 dòng thiếu Họ và tên/email sai |
| `TEST-members-duplicate.xlsx` | Duplicate trong file hoặc với database |
| `TEST-members-empty.xlsx` | Chỉ có header, không có data row |
| `TEST-members-too-large.xlsx` | Vượt giới hạn cấu hình hoặc số dòng tối đa |
| `TEST-members-fake.xlsx` | File text/zip đổi đuôi `.xlsx` |

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR005-01 | Tải template | Administrator và Clerk tải template | `200`; đúng MIME XLSX/tên file; có sheet Hội viên, Hướng dẫn, Danh mục và đúng header |
| FR005-02 | Import hợp lệ | Upload `TEST-members-valid.xlsx` bằng multipart field `file` | `200`; `importedCount=3`, `totalRows=3`, errors rỗng; tìm thấy cả 3 hội viên |
| FR005-03 | Rollback khi có dòng lỗi | Upload file có 2 dòng đúng, 1 dòng sai | `422`; importedCount bằng 0; errors có rowNumber/field/message; không dòng nào được lưu |
| FR005-04 | Duplicate | Upload file duplicate | `422`; lỗi chỉ rõ dòng/field duplicate; database không thay đổi |
| FR005-05 | Workbook rỗng/sai cấu trúc | Upload file rỗng, thiếu sheet hoặc đổi header | `422` hoặc validation response đúng contract; không mutation |
| FR005-06 | Sai định dạng | Upload CSV/PDF hoặc fake XLSX | `415 ProblemDetails`; không lưu dữ liệu |
| FR005-07 | Quá kích thước/số dòng | Upload file vượt giới hạn | `413` cho kích thước hoặc `422` cho giới hạn workbook; không đọc/ghi một phần |
| FR005-08 | Sai quyền | Drafter/anonymous tải hoặc import | Quyền đúng theo contract: Administrator/Clerk được phép; sai role `403`, anonymous `401` |
| FR005-09 | UI retry | Sau một import lỗi, sửa file và upload lại | Bảng lỗi cũ được thay bằng kết quả mới; success xóa file đã chọn và refresh danh sách |

## Kiểm tra transaction

Trước và sau mỗi ca lỗi, tìm theo prefix của file. Tổng số hội viên phải không đổi.
Không dùng việc UI không hiển thị làm bằng chứng duy nhất; kiểm tra lại qua API.

## Dọn dữ liệu

Deactivate các hội viên được import thành công theo đúng prefix lượt thử.
