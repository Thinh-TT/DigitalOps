# Mẫu thử FR-010 — Nhắc hạn và đánh dấu đã đọc

## Mục tiêu

Xác nhận Reminder Worker tạo đúng `BeforeDeadline`, `DueDate`, `Overdue`, chạy
idempotent, chuyển status quá hạn và chỉ cho người nhận/Administrator truy cập.

## Tiền điều kiện

- Cấu hình timezone `Asia/Ho_Chi_Minh` và `BeforeDeadlineDays` theo môi trường.
- Tạo các incoming synthetic đã assignment cho `STAFF-A`, chưa Completed:

| Document | Deadline so với business date | Kỳ vọng |
| --- | --- | --- |
| `TEST-REM-BEFORE` | `today + BeforeDeadlineDays` | BeforeDeadline |
| `TEST-REM-DUE` | `today` | DueDate |
| `TEST-REM-OVERDUE` | trước `today` | Overdue + status Overdue |
| `TEST-REM-FUTURE` | ngoài các mốc trên | Không tạo reminder |
| `TEST-REM-COMPLETE` | bất kỳ, status Completed | Không tạo reminder mới |

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR010-01 | Tạo reminder theo mốc | Chạy/chờ một worker cycle | Tạo đúng 3 reminder cho BEFORE/DUE/OVERDUE; FUTURE/COMPLETE không tạo |
| FR010-02 | Chuyển Overdue | Đọc lại `TEST-REM-OVERDUE` | Status thành `Overdue`; document khác không bị đổi sai |
| FR010-03 | Idempotency | Chạy lại worker cùng business date | Không thêm reminder trùng theo document/recipient/kind/date |
| FR010-04 | Danh sách người nhận | `STAFF-A` mở SCR-010 và filter read/kind/paging nếu có | Chỉ reminder của mình; metadata/paging và badge unread đúng |
| FR010-05 | Đánh dấu đã đọc | `STAFF-A` POST `/reminders/{id}/read` hai lần | Lần đầu status/readAt cập nhật; lần hai idempotent, không tạo bản ghi mới |
| FR010-06 | Staff khác | Tài khoản khác đọc danh sách/mark-read reminder của `STAFF-A` | Không xem được reminder của người khác; mark-read trả `403` |
| FR010-07 | Administrator filter recipient | ADMIN-A lọc theo recipient và mark read theo quyền hiện hành | Được phép theo contract; không làm sai owner/recipient |
| FR010-08 | Anonymous/password boundary | Gọi API không token hoặc token mustChangePassword | `401`/`403`; không đổi delivery status |
| FR010-09 | Not found | Mark-read GUID không tồn tại | `404 ProblemDetails` |
| FR010-10 | Worker restart | Restart worker rồi chờ cycle kế tiếp cùng ngày | Không duplicate; log tổng created/existing hợp lý |

## Dọn dữ liệu

Cleanup reminder và incoming synthetic theo đúng ID trong database development nếu
có quy trình được phê duyệt; không có UI/API chạy hoặc xóa worker history thủ công.
