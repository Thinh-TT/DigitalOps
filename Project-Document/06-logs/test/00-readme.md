# Bộ mẫu thử DigitalOps

## 1. Phạm vi

Bộ tài liệu này dùng để kiểm thử thủ công/nghiệm thu các tính năng đã hoàn thành
từ FR-001 đến FR-012. Các tính năng FR-013 đến FR-016 chưa hoàn thành nên không
được đưa vào kết quả pass/fail hiện tại.

| File | Phạm vi | Màn hình chính |
| --- | --- | --- |
| `platform-smoke.md` | API/OpenAPI, database, App Shell, AI runtime | Toàn hệ thống |
| `fr-001-authentication.md` | Đăng nhập, đổi mật khẩu tạm, đăng xuất | SCR-001, SCR-002 |
| `fr-002-staff-and-roles.md` | Staff, role, reset mật khẩu | SCR-003 |
| `fr-003-member-query.md` | Danh sách, tìm kiếm, chi tiết hội viên | SCR-004 |
| `fr-004-member-management.md` | Tạo, sửa, ngừng hoạt động hội viên | SCR-004 |
| `fr-005-member-import.md` | Import hội viên từ Excel | SCR-005 |
| `fr-006-document-catalog.md` | Loại văn bản, template, FormatRules | SCR-006, SCR-007 |
| `fr-007-incoming-documents.md` | Tiếp nhận và quản lý văn bản đến | SCR-008, SCR-009 |
| `fr-008-attachments.md` | Attachment incoming/outgoing | SCR-009, SCR-012 |
| `fr-009-ai-assignment.md` | AI gợi ý và xác nhận điều phối | SCR-009 |
| `fr-010-reminders.md` | Reminder worker và đánh dấu đã đọc | SCR-010 |
| `fr-011-outgoing-documents.md` | Tạo văn bản đi theo mẫu | SCR-011, SCR-012 |
| `fr-012-ai-draft-editor.md` | AI sinh nháp và chỉnh sửa | SCR-012 |

## 2. Môi trường và tài khoản thử

- API và Web chạy theo `SETUP.md`; PostgreSQL đã áp migration hiện hành.
- Với ca AI: Ollama embedding và Qdrant hoạt động; provider Development đã cấu hình.
- Không dùng dữ liệu cá nhân thật. Mọi dữ liệu tạo mới dùng prefix
  `TEST-<YYYYMMDD>-<mã-ca>` để có thể tìm và dọn chính xác.
- Chuẩn bị các tài khoản synthetic sau:

| Bí danh | Role/trạng thái | Mục đích |
| --- | --- | --- |
| `ADMIN-A` | Administrator, active | Quản trị Staff và danh mục |
| `CLERK-A` | Clerk, active | Hội viên, văn bản đến, điều phối |
| `DRAFTER-A` | Drafter, active | Owner văn bản đi |
| `DRAFTER-B` | Drafter, active | Kiểm tra non-owner |
| `STAFF-A` | một role BusinessAccess, active | Người nhận điều phối/reminder |
| `TEMP-A` | active, `mustChangePassword=true` | Luồng đổi mật khẩu bắt buộc |
| `INACTIVE-A` | inactive | Kiểm tra chặn đăng nhập/lookup |

Không ghi mật khẩu hoặc access token thật vào file kết quả/evidence.

## 3. Quy ước thực thi

- API base URL: `https://localhost:7162/api/v1` hoặc URL của môi trường đang thử.
- Request nghiệp vụ gửi `Authorization: Bearer <token>`.
- Trạng thái ca thử: `P` = Pass, `F` = Fail, `B` = Blocked, `N/A` = không áp dụng.
- Với response lỗi, kiểm tra `application/problem+json`, `status`, `title`, `detail`,
  `instance` và errors theo field nếu là `ValidationProblemDetails`.
- Với thao tác mutation, luôn kiểm tra cả response và đọc lại resource/database.
- Với AI failure/concurrency, không reload trước khi xác nhận dữ liệu local được giữ.
- Chạy `platform-smoke.md` trước; sau đó thực hiện FR theo dependency từ FR-001
  đến FR-012 để tái sử dụng dữ liệu synthetic đã tạo.

## 4. Phiếu ghi kết quả

| Thuộc tính | Giá trị |
| --- | --- |
| Ngày chạy |  |
| Người chạy |  |
| Environment/commit |  |
| Trình duyệt/viewport |  |
| API provider/model |  |
| Embedding/Qdrant |  |

| Test ID | Kết quả | Evidence/link | Defect ID | Ghi chú |
| --- | --- | --- | --- | --- |
|  |  |  |  |  |

## 5. Dọn dữ liệu

- Chỉ xóa/vô hiệu hóa dữ liệu có prefix của lượt thử hiện tại.
- Không xóa Staff, template, document hoặc vector không do lượt thử tạo.
- Với dữ liệu không có public delete endpoint, đánh dấu inactive hoặc dùng quy trình
  cleanup development đã được phê duyệt; ghi lại ID đã tạo trong evidence.
- Sau ca AI, xác nhận point synthetic trong Qdrant đã stale/delete mà không ảnh hưởng
  nguồn Staff/Template thật.
