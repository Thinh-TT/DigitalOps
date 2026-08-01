# API Specification

## 1. Mục Tiêu Tài Liệu

### 1.1. Mục đích

- Chuẩn hóa contract giữa ASP.NET Core Web API, React web client và tester.
- Đồng bộ với [01-functional-requirements.md](../03-functional/01-functional-requirements.md), [01-database-designer.md](01-database-designer.md) và [03-ai-rag-design.md](03-ai-rag-design.md) khi triển khai các tính năng AI.
- Bao phủ toàn bộ use case `FR-001` đến `FR-016` trong phạm vi MVP.
- Làm cơ sở cho controller, DTO, validation, OpenAPI/Swagger và integration test.

### 1.2. Phạm vi

API mô tả các nhóm Authentication, Staff, Members, document type/template, incoming document, attachment, reminder, outgoing document, AI/review, approval/archive và full-text search. Kiến trúc RAG/LLM local-first đã được Project Owner phê duyệt cho MVP/demo theo baseline v3; đây vẫn là implementation detail của các tác vụ AI và không tạo public endpoint mới trong contract này. Production hardening được review riêng.

Trong Development, backend có thể chọn Ollama hoặc External chat provider qua
server-side `.env`; lựa chọn này không xuất hiện trong DTO/route và không thay đổi
behavior `503` khi provider lỗi. Embedding/retrieval vẫn local.

Không có endpoint cho refresh token, server-side logout, OCR, email/SMS reminder, Citizen Portal hoặc search service ngoài PostgreSQL trong MVP.

## 2. Quy Ước Chung

### 2.1. Base route và HTTP

| Hạng mục | Quy ước |
| --- | --- |
| Base route | `/api/v1` |
| API style | Controller-based REST API với `[ApiController]` |
| Request/response body | `application/json; charset=utf-8` |
| Upload | `multipart/form-data` |
| Download attachment | API stream file bytes sau khi kiểm tra quyền |
| JSON property | `camelCase` |
| Identifier | `Guid` dạng chuỗi chuẩn, ví dụ `4f3d...` |
| Ngày | `YYYY-MM-DD` |
| Thời điểm | ISO-8601 UTC, ví dụ `2026-07-30T09:30:00Z` |
| Enum/status | Chuỗi mã tiếng Anh ổn định |

### 2.2. Quy ước response

- Resource đơn lẻ trả trực tiếp response DTO.
- Tạo resource trả `201 Created` kèm header `Location` và resource mới.
- Action thay đổi trạng thái trả `200 OK` kèm resource mới nhất.
- Xóa attachment trả `204 No Content`.
- Import Excel trả `200 OK` cùng báo cáo import.
- Lỗi dùng `ProblemDetails`; lỗi validation model dùng `ValidationProblemDetails`.
- EF Core entity không được trả trực tiếp qua API.

### 2.3. Pagination, filter và sắp xếp

Mọi list endpoint hỗ trợ `page` và `pageSize`:

| Query | Kiểu | Mặc định / giới hạn |
| --- | --- | --- |
| `page` | integer | `1`, tối thiểu `1` |
| `pageSize` | integer | `20`, từ `1` đến `100` |

List trả theo DTO chung `PagedResponse<T>`:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

Sort mặc định:

- Members: `fullName` tăng dần.
- Incoming documents: `deadline` tăng dần, sau đó `createdAt` giảm dần.
- Outgoing documents: `updatedAt` giảm dần.
- Reminders: `createdAt` giảm dần.
- Full-text search: `score` giảm dần.

## 3. Xác Thực Và Phân Quyền

### 3.1. JWT access token

- MVP chỉ dùng access token, không có refresh token hoặc server-side revoke list.
- Đăng xuất là thao tác frontend xóa access token và dữ liệu phiên cục bộ; không có `POST /auth/logout`.
- Header gọi API: `Authorization: Bearer <accessToken>`.
- JWT chứa tối thiểu `sub`, `staffId`, role claims và claim `mustChangePassword`.
- `LoginResponse.expiresAt` là nguồn duy nhất để client biết thời điểm token hết hạn.

### 3.2. Policy

| Policy / role | Endpoint áp dụng |
| --- | --- |
| Anonymous | `POST /auth/login` |
| Authenticated | `GET /auth/me`, download attachment, xem document/reminder/search theo quyền |
| PasswordChangeRequired | `POST /auth/change-password`; được phép khi `mustChangePassword = true` |
| BusinessAccess | Mọi endpoint nghiệp vụ; yêu cầu Staff active và `mustChangePassword = false` |
| Administrator | Quản lý Staff/role, document type và template |
| Clerk | Incoming document, assignment, archive; member CRUD/import |
| Drafter | Tạo/sửa văn bản đi của chính mình, AI draft, review |
| Leader | Phê duyệt hoặc trả lại văn bản |

Khi `mustChangePassword = true`, chỉ `GET /auth/me` và `POST /auth/change-password` được phép. Mọi endpoint nghiệp vụ trả `403 Forbidden` với `ProblemDetails` type `password-change-required`.

### 3.3. Quy tắc quyền dữ liệu

- Mọi Staff active xem được incoming/outgoing document, attachment metadata, review history và full-text result.
- Chỉ Administrator/Văn thư xem toàn bộ hồ sơ Members. `GET /members/lookup` là ngoại lệ hẹp cho role Drafter: chỉ trả Member active với `id`, `fullName`, `position` để liên kết khi tạo văn bản đi.
- `GET /staff?activeOnly=true` cho Administrator và Clerk để chọn người điều phối; CRUD Staff vẫn chỉ Administrator.
- Reminder mặc định chỉ trả dữ liệu của Staff đang đăng nhập. Administrator có thể truyền `recipientStaffId` để hỗ trợ vận hành.

## 4. DTO Dùng Chung

### 4.1. Error response

`ProblemDetails` tuân RFC 7807/RFC 9457 theo ASP.NET Core:

```json
{
  "type": "https://digitalops/errors/invalid-state",
  "title": "State transition is not allowed.",
  "status": 409,
  "detail": "Outgoing document must pass review before approval.",
  "instance": "/api/v1/outgoing-documents/4f3d.../approval",
  "traceId": "00-..."
}
```

Validation model trả `ValidationProblemDetails` với `errors` theo camelCase field name.

### 4.2. Kiểu enum dùng chung

| Enum | Giá trị |
| --- | --- |
| `MemberStatus` | `Active`, `Inactive` |
| `IncomingDocumentStatus` | `New`, `InProgress`, `Completed`, `Overdue` |
| `OutgoingDocumentStatus` | `AiDraft`, `Editing`, `PendingReview`, `ReviewFailed`, `PendingApproval`, `Approved`, `Archived` |
| `ReviewSource` | `Rule`, `AI`, `Hybrid` |
| `ReviewResult` | `Failed`, `Passed` |
| `ReminderKind` | `BeforeDeadline`, `DueDate`, `Overdue` |
| `ReminderDeliveryStatus` | `Unread`, `Read` |
| `ExtractionStatus` | `Pending`, `Processing`, `Succeeded`, `Failed`, `Unsupported` |
| `ApprovalDecision` | `Approve`, `Return` |
| `DocumentKind` | `Incoming`, `Outgoing` |
| `MatchSource` | `Summary`, `Title`, `Content`, `AiDraftContent`, `Attachment` |

### 4.3. DTO tham chiếu nhỏ

Các response document tái sử dụng các DTO tham chiếu sau để không lặp dữ liệu:

| DTO | Thuộc tính |
| --- | --- |
| `StaffReference` | `id`, `fullName`, `position`, `department` |
| `MemberReference` | `id`, `fullName`, `position` |
| `DocumentTypeReference` | `id`, `code`, `name` |
| `DocumentTemplateReference` | `id`, `name`, `documentType` |
| `AttachmentResponse` | `id`, `fileName`, `uploadedBy`, `uploadedAt`, `extractionStatus`, `extractedAt` |

`AttachmentResponse` không trả `fileUrl`, `extractedText`, `extractionError` hoặc signed URL.

## 5. DTO Identity Và Staff

### 5.1. Authentication DTO

| DTO | Thuộc tính |
| --- | --- |
| `LoginRequest` | `userNameOrEmail` (required), `password` (required) |
| `LoginResponse` | `accessToken`, `expiresAt`, `mustChangePassword`, `staff`, `roles` |
| `CurrentUserResponse` | `staff`, `roles`, `mustChangePassword` |
| `ChangePasswordRequest` | `currentPassword` (required), `newPassword` (required) |

`POST /auth/change-password` trả lại `LoginResponse` có access token mới. Token cũ mang claim `mustChangePassword = true` không được dùng cho nghiệp vụ.

### 5.2. Staff DTO

| DTO | Thuộc tính |
| --- | --- |
| `StaffCreateRequest` | `userName`, `email`, `temporaryPassword`, `fullName`, `position?`, `department?`, `phone?`, `roles` |
| `StaffUpdateRequest` | `fullName?`, `position?`, `department?`, `email?`, `phone?`, `isActive?` |
| `RoleAssignmentRequest` | `roles` — mảng không rỗng các role hợp lệ |
| `ResetPasswordRequest` | `temporaryPassword` (required) |
| `StaffResponse` | `id`, `identityUserId`, `userName`, `fullName`, `position`, `department`, `email`, `phone`, `isActive`, `roles`, `createdAt`, `updatedAt` |

Giá trị role API: `Administrator`, `Clerk`, `Drafter`, `Leader`. UI ánh xạ sang nhãn tiếng Việt.

`StaffUpdateRequest` là PATCH presence-aware: field không xuất hiện giữ nguyên
giá trị; `null` xóa `position`, `department`, `phone`; `fullName` và `email`
không nhận `null` hoặc chuỗi rỗng.

## 6. API Authentication Và Staff

### 6.1. Authentication

| Method | Path | Auth/role | Request | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `POST` | `/auth/login` | Anonymous | `LoginRequest` | `200 LoginResponse` | `401` thông tin không hợp lệ hoặc Staff inactive |
| `GET` | `/auth/me` | Authenticated | — | `200 CurrentUserResponse` | `401` token không hợp lệ/hết hạn |
| `POST` | `/auth/change-password` | Authenticated, PasswordChangeRequired hoặc BusinessAccess | `ChangePasswordRequest` | `200 LoginResponse` | `400` password không đạt policy, `401` token hết hạn |

### 6.2. Staff và role

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/staff` | Administrator; Clerk nếu `activeOnly=true` | `activeOnly?`, `page?`, `pageSize?` | `200 PagedResponse<StaffResponse>` | `403` |
| `POST` | `/staff` | Administrator | `StaffCreateRequest` | `201 StaffResponse` | `409` username/email trùng |
| `GET` | `/staff/{id}` | Administrator | — | `200 StaffResponse` | `404` |
| `PATCH` | `/staff/{id}` | Administrator | `StaffUpdateRequest` | `200 StaffResponse` | `404`, `409` email trùng hoặc Administrator active cuối |
| `PUT` | `/staff/{id}/roles` | Administrator | `RoleAssignmentRequest` | `200 StaffResponse` | `400` role không hợp lệ; `409` Administrator active cuối |
| `POST` | `/staff/{id}/reset-password` | Administrator | `ResetPasswordRequest` | `204 No Content` | `404`, `400` password không đạt policy |

Danh sách mặc định 20 dòng, tối đa 100, sắp xếp theo `fullName` rồi `id`.
Reset password đặt `mustChangePassword = true` trong database; vì
`BusinessAccess` kiểm tra cả claim và trạng thái database, JWT cũ bị chặn ngay
với type `password-change-required`. Role trong JWT là snapshot:
`GET /auth/me` trả role từ token hiện tại, còn role mới chỉ có hiệu lực ở JWT
được cấp tiếp theo. Không có DELETE endpoint cho Staff.

### 6.3. Khởi tạo Identity

- Mỗi lần API khởi động, initializer đảm bảo idempotent bốn role
  `Administrator`, `Clerk`, `Drafter`, `Leader`; initializer không chạy EF
  migration.
- `IdentityBootstrap__Enabled=false` là mặc định. Khi bật và chưa có
  Administrator active, cấu hình `UserName`, `Email`, `TemporaryPassword`,
  `FullName`, `Position`, `Department`, `Phone` tạo Identity user và Staff trong
  một transaction, với `EmailConfirmed=true`, lockout enabled và
  `MustChangePassword=true`.
- Bootstrap không reset hoặc ghi đè tài khoản đã tồn tại. Cấu hình không thể tạo
  quan hệ Administrator hợp lệ làm startup thất bại, nhưng log không chứa mật
  khẩu.

## 7. DTO/API Members Và Import Excel

### 7.1. Member DTO

| DTO | Thuộc tính |
| --- | --- |
| `MemberUpsertRequest` | `fullName`, `dateOfBirth?`, `gender?`, `address?`, `phone?`, `email?`, `position?`, `joinDate?`, `status?`, `notes?`; FullName bắt buộc khi POST và bắt buộc sau khi áp dụng PATCH. POST luôn tạo `Active`; PATCH chỉ nhận `status = Active` để kích hoạt lại. Ngừng hoạt động phải dùng action `deactivate`. |
| `MemberResponse` | `id`, toàn bộ trường Member, `createdAt`, `updatedAt` |
| `MemberLookupResponse` | `id`, `fullName`, `position` |
| `MemberImportResult` | `importedCount`, `totalRows`, `errors` |
| `MemberImportRowError` | `rowNumber`, `field`, `message` |
| `MemberImportProblemDetails` | RFC ProblemDetails và các extension `importedCount = 0`, `totalRows`, `errors: MemberImportRowError[]` |

### 7.2. Endpoint Members

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/members` | Administrator, Clerk | `q?`, `status?`, paging | `200 PagedResponse<MemberResponse>` | `403` |
| `GET` | `/members/lookup` | Drafter, Clerk, Administrator | `q?`, paging; chỉ Active | `200 PagedResponse<MemberLookupResponse>` | `403` |
| `POST` | `/members` | Administrator, Clerk | `MemberUpsertRequest` | `201 MemberResponse` | `400` |
| `GET` | `/members/{id}` | Administrator, Clerk | — | `200 MemberResponse` | `404`, `403` |
| `PATCH` | `/members/{id}` | Administrator, Clerk | `MemberUpsertRequest` dạng partial | `200 MemberResponse` | `400`, `404` |
| `POST` | `/members/{id}/deactivate` | Administrator, Clerk | — | `200 MemberResponse` | `404`, `409` nếu đã Inactive |
| `GET` | `/members/import-template` | Administrator, Clerk | — | `200` stream `DigitalOps-Member-Import-Template.xlsx` | `403` |
| `POST` | `/members/import` | Administrator, Clerk | `multipart/form-data`: `file` | `200 MemberImportResult` | `400`, `413`, `415`, `422 MemberImportProblemDetails` |

Import là all-or-nothing. `422` trả toàn bộ `MemberImportRowError`; database không ghi bất kỳ dòng nào nếu có lỗi. `rowNumber = 0` dành cho lỗi workbook, `1` dành cho header và từ `2` trở đi là số dòng Excel thực tế. `field` dùng mã ổn định của Member hoặc `file`/`duplicateKey`.

Template có sheet dữ liệu `Hội viên`, header hàng 1 theo đúng thứ tự: `Họ và tên`, `Ngày sinh`, `Giới tính`, `Địa chỉ`, `Số điện thoại`, `Email`, `Chức vụ`, `Ngày gia nhập`, `Trạng thái`, `Ghi chú`. Sheet `Hướng dẫn` mô tả quy tắc nhập và sheet danh mục ẩn cung cấp validation cho Gender (`Male`, `Female`, `Other`) và Status (`Active`, `Inactive`). Ngày dùng `yyyy-mm-dd`, điện thoại là Text; Status trống mặc định `Active`.

Import mặc định giới hạn file 10 MiB, tối đa 10.000 dòng dữ liệu và giới hạn 100 MiB dung lượng giải nén; các ngưỡng được cấu hình bằng section `MemberImport`. Header phải đúng tên/thứ tự, dòng hoàn toàn trống được bỏ qua. File khác `.xlsx`, sai signature hoặc workbook hỏng trả `415`; sai sheet/header hoặc dữ liệu nghiệp vụ trả `422`.

Khóa trùng `FullName + DateOfBirth + Phone` được so sau chuẩn hóa trên cả hội viên Active/Inactive và trong chính file. FullName không phân biệt hoa thường; DateOfBirth và Phone so chính xác; `null` ở hai phía được coi là bằng nhau.

PATCH phân biệt field không gửi với `null`: field không gửi giữ nguyên, còn `null`
xóa giá trị của field nullable. `fullName` và `status` không nhận `null`. POST gửi
`status = Inactive`, hoặc PATCH gửi `status = Inactive`, đều trả validation `400`
để không đi vòng qua action ngừng hoạt động và quy tắc conflict của action này.

## 8. DTO/API DocumentTypes Và DocumentTemplates

### 8.1. DTO danh mục và mẫu

| DTO | Thuộc tính |
| --- | --- |
| `DocumentTypeRequest` | `code`, `name`, `description?`, `isActive?`; code/name bắt buộc khi POST và bắt buộc sau khi áp dụng PATCH |
| `DocumentTypeResponse` | `id`, `code`, `name`, `description`, `isActive`, `createdAt`, `updatedAt` |
| `DocumentTemplateRequest` | `documentTypeId`, `name`, `templateContent`, `formatRules`, `isActive?`; bốn trường đầu bắt buộc khi POST và bắt buộc sau khi áp dụng PATCH |
| `DocumentTemplateResponse` | `id`, `documentType`, `name`, `templateContent`, `formatRules`, `isActive`, `createdAt`, `updatedAt` |

`formatRules` là JSON object theo contract database. `version` phải là số nguyên
dương; `rules` là mảng (được phép rỗng); mỗi phần tử là object có `code` là
chuỗi không rỗng và `required` là boolean. `code` không được trùng sau khi trim,
so sánh phân biệt hoa/thường. Root và từng rule được phép có thuộc tính mở rộng.
Object sai cấu trúc trả `422 ValidationProblemDetails` tại
`errors.formatRules`; HTTP request JSON sai cú pháp vẫn trả model validation
`400` theo `[ApiController]`.

### 8.2. Endpoint DocumentTypes

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/document-types` | BusinessAccess | `activeOnly?`, paging | `200 PagedResponse<DocumentTypeResponse>` | `403` |
| `POST` | `/document-types` | Administrator | `DocumentTypeRequest` | `201 DocumentTypeResponse` | `409` code trùng |
| `GET` | `/document-types/{id}` | BusinessAccess | — | `200 DocumentTypeResponse` | `404` |
| `PATCH` | `/document-types/{id}` | Administrator | `DocumentTypeRequest` dạng partial | `200 DocumentTypeResponse` | `404`, `409` |

### 8.3. Endpoint DocumentTemplates

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/document-templates` | BusinessAccess | `documentTypeId?`, `activeOnly?`, paging | `200 PagedResponse<DocumentTemplateResponse>` | `403` |
| `POST` | `/document-templates` | Administrator | `DocumentTemplateRequest` | `201 DocumentTemplateResponse` | `409` tên mẫu trùng trong loại |
| `GET` | `/document-templates/{id}` | BusinessAccess | — | `200 DocumentTemplateResponse` | `404` |
| `PATCH` | `/document-templates/{id}` | Administrator | `DocumentTemplateRequest` dạng partial | `200 DocumentTemplateResponse` | `404`, `422` FormatRules |

Không có DELETE endpoint cho document type/template. Vô hiệu hóa thực hiện qua `PATCH` với `isActive = false`.

`activeOnly = true` của document template chỉ trả template active có document
type active. Tạo template, đổi `documentTypeId` hoặc kích hoạt lại template yêu
cầu type đích đang active; sửa nội dung hoặc vô hiệu hóa template thuộc type
inactive vẫn được phép. Vô hiệu hóa type không cascade trạng thái template.

## 9. DTO/API IncomingDocuments, Điều Phối Và Nhắc Hạn

### 9.1. Incoming document DTO

| DTO | Thuộc tính |
| --- | --- |
| `IncomingDocumentCreateRequest` | `referenceNumber`, `senderOrg`, `summary`, `receivedDate`, `deadline`, `documentTypeId` |
| `IncomingDocumentUpdateRequest` | Các trường create ở dạng partial; không bao gồm assignment/status/completedAt |
| `IncomingDocumentResponse` | `id`, thông tin create, `documentType`, `suggestedStaff?`, `assignmentSuggestionReason?`, `assignmentConfidence?`, `assignmentSuggestedAt?`, `assignedToStaff?`, `assignmentConfirmedByStaff?`, `assignmentConfirmedAt?`, `status`, `completedAt?`, `attachments`, `createdAt`, `updatedAt` |
| `AssignmentSuggestionResponse` | `incomingDocumentId`, `suggestedStaff?`, `reason?`, `confidence?`, `suggestedAt?` |
| `AssignmentConfirmRequest` | `assignedToStaffId` |
| `ReminderResponse` | `id`, `incomingDocumentId`, `referenceNumber`, `summary`, `reminderKind`, `reminderDate`, `deliveryStatus`, `createdAt`, `readAt?` |

`documentType` dùng `DocumentTypeReference { id, code, name }`. Các staff
reference trong response dùng `{ id, fullName, position, department }`.
`attachments` luôn hiện diện và từ T2-03 chứa metadata thật theo
`AttachmentResponse`, sắp theo `uploadedAt DESC`, sau đó `id`. Response không
expose URL/object key, extracted text hoặc lỗi kỹ thuật.

### 9.2. Endpoint incoming document và assignment

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/incoming-documents` | BusinessAccess | `q?`, `documentTypeId?`, `status?`, `assignedToStaffId?`, `deadlineFrom?`, `deadlineTo?`, paging | `200 PagedResponse<IncomingDocumentResponse>` | `400` filter không hợp lệ |
| `POST` | `/incoming-documents` | Clerk | `IncomingDocumentCreateRequest` | `201 IncomingDocumentResponse` | `400` deadline trước ngày nhận |
| `GET` | `/incoming-documents/{id}` | BusinessAccess | — | `200 IncomingDocumentResponse` | `404` |
| `PATCH` | `/incoming-documents/{id}` | Clerk | `IncomingDocumentUpdateRequest` | `200 IncomingDocumentResponse` | `404`, `409` đã Completed |
| `POST` | `/incoming-documents/{id}/assignment-suggestion` | Clerk | — | `200 AssignmentSuggestionResponse` | `409` đã Completed, `503` AI lỗi |
| `POST` | `/incoming-documents/{id}/assignment` | Clerk | `AssignmentConfirmRequest` | `200 IncomingDocumentResponse` | `400` Staff inactive, `409` Completed |
| `POST` | `/incoming-documents/{id}/complete` | Clerk hoặc Assigned Staff | — | `200 IncomingDocumentResponse` | `403`, `409` đã Completed |

`POST /assignment` được dùng cho xác nhận lần đầu và giao lại. AI không thể tự tạo assignment; `503` từ suggestion không thay đổi gợi ý, assignment hoặc status hiện có.

Quy tắc T2-02:

- Chuỗi create/PATCH được trim; `referenceNumber` tối đa 100,
  `senderOrg` tối đa 255 và `summary` không rỗng. Ngày dùng `YYYY-MM-DD` và giá
  trị cuối cùng phải thỏa `receivedDate <= deadline`.
- PATCH phân biệt field bỏ qua với `null`, không nhận `null` và payload rỗng trả
  `400`. Không cho PATCH status, assignment hoặc `completedAt`; resource
  `Completed` trả `409`.
- Create và đổi loại yêu cầu document type active. Nếu loại hiện tại bị
  inactive, vẫn sửa trường khác nhưng chỉ đổi sang loại active.
- List trim `q` (tối đa 200), tìm không phân biệt hoa/thường trên số hiệu, nơi gửi
  và trích yếu; deadline range inclusive. Mặc định sắp `receivedDate DESC`,
  `createdAt DESC`, rồi `id`; page 1, pageSize 20, tối đa 100.
- Complete chỉ chuyển `InProgress`/`Overdue → Completed` khi đã có người xử lý
  chính, ghi `completedAt` UTC. Clerk hoặc đúng Assigned Staff được gọi; caller
  khác nhận `403`, trạng thái không hợp lệ/gọi lại nhận `409`.

### 9.3. Endpoint reminder

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/reminders` | BusinessAccess | `deliveryStatus?`, `recipientStaffId?` (Administrator only), paging | `200 PagedResponse<ReminderResponse>` | `403` recipientStaffId không hợp lệ |
| `POST` | `/reminders/{id}/read` | Người nhận hoặc Administrator | — | `200 ReminderResponse` | `404`, `403` |

Reminder Worker không có public API. Job tạo reminder idempotent trực tiếp qua application service/database.

## 10. DTO/API Attachments Và Text Extraction

### 10.1. Upload/download contract

| Hạng mục | Contract |
| --- | --- |
| Upload body | `multipart/form-data` với form field bắt buộc `file` |
| Kiểu file | PDF, DOCX, XLSX, JPG, JPEG, PNG |
| Kích thước | `AttachmentStorage.MaxFileSizeBytes`, mặc định 10 MiB; vượt mức trả `413` |
| Thành công | `201 AttachmentResponse` |
| Download | `200` stream với `Content-Disposition: attachment`; không trả FileUrl |
| Text extraction | PDF/DOCX/XLSX trả `Pending` và xử lý nền; ảnh `Unsupported`; PDF scan có thể chuyển `Unsupported` |

Server chuẩn hóa tên hiển thị, sinh object key riêng và kiểm tra extension cùng
signature/cấu trúc file. MIME từ client không phải nguồn tin cậy duy nhất; nội
dung giả mạo trả `415`. File được lưu local ngoài web root qua
`IAttachmentStorage`; API không phục vụ trực tiếp đường dẫn storage.

### 10.2. Endpoint attachment

| Method | Path | Auth/role | Request | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `POST` | `/incoming-documents/{id}/attachments` | Clerk | multipart `file` | `201 AttachmentResponse` | `409` incoming Completed, `413`, `415` |
| `POST` | `/outgoing-documents/{id}/attachments` | Drafter sở hữu document | multipart `file` | `201 AttachmentResponse` | `403`, `409` document không editable |
| `GET` | `/attachments/{id}/download` | BusinessAccess | — | `200` file stream | `404`, `403` |
| `DELETE` | `/attachments/{id}` | Clerk với incoming; Drafter sở hữu outgoing | — | `204 No Content` | `403`, `409` parent không editable |

API không expose endpoint text extraction job. Client đọc lại document/attachment response để thấy `extractionStatus` thay đổi.

Boundary triển khai: T2-03 chỉ bật upload/delete cho incoming và download dùng
chung. Endpoint upload outgoing cùng ownership rule được bổ sung ở T3-01 sau
khi `outgoing_documents` tồn tại; không tạo stub hoặc FK không được kiểm soát.
Trong T2-03, `Pending` chính là trigger bền vững để T4-01 xử lý sau, không dùng
queue in-memory.

## 11. DTO/API OutgoingDocuments, AI Drafting Và Review

### 11.1. Outgoing document DTO

| DTO | Thuộc tính |
| --- | --- |
| `OutgoingDocumentCreateRequest` | `templateId`, `title`, `relatedIncomingDocumentId?`, `relatedMemberId?` |
| `OutgoingDocumentUpdateRequest` | `title?`, `content?` |
| `OutgoingDocumentResponse` | `id`, `template`, `relatedIncomingDocument?`, `relatedMember?`, `title`, `content`, `aiDraftContent?`, `draftedByStaff`, `status`, `reviewIssues`, `approvedByStaff?`, `approvedAt?`, `referenceNumber?`, `issuedDate?`, `archivedAt?`, `attachments`, `createdAt`, `updatedAt` |
| `AiDraftRequest` | `instruction?` — hướng dẫn bổ sung cho AI, không thay thế dữ liệu template/hồ sơ |
| `ReviewIssueResponse` | `ruleCode`, `severity`, `message`, `location?` |
| `ReviewResponse` | `id`, `outgoingDocumentId`, `attemptNo`, `reviewSource`, `reviewedByStaff?`, `contentSnapshot`, `reviewResult`, `reviewIssues`, `reviewedAt`, `documentStatus` |

Khi tạo văn bản đi, server render `TemplateContent` theo allow-list token case-sensitive: `{{member.fullName}}`, `dateOfBirth`, `gender`, `address`, `phone`, `email`, `position`, `joinDate`, `{{incoming.referenceNumber}}`, `senderOrg`, `summary`, `receivedDate`, `deadline`.
Ngày render theo `dd/MM/yyyy`; giới tính map `Nam`, `Nữ`, `Khác`. Token không biết, thiếu liên kết hoặc field null được giữ nguyên để người soạn hoàn thiện thủ công.

### 11.2. Endpoint outgoing document, AI và review

| Method | Path | Auth/role | Request/query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/outgoing-documents` | BusinessAccess | `q?`, `templateId?`, `relatedIncomingDocumentId?`, `relatedMemberId?`, `status?`, `draftedByStaffId?`, `dateFrom?`, `dateTo?`, paging | `200 PagedResponse<OutgoingDocumentResponse>` | `400` filter không hợp lệ |
| `POST` | `/outgoing-documents` | Drafter | `OutgoingDocumentCreateRequest` | `201 OutgoingDocumentResponse` | `400`, `409` template/member inactive |
| `GET` | `/outgoing-documents/{id}` | BusinessAccess | — | `200 OutgoingDocumentResponse` | `404` |
| `PATCH` | `/outgoing-documents/{id}` | Drafter sở hữu document | `OutgoingDocumentUpdateRequest` | `200 OutgoingDocumentResponse` | `403`, `409` status không editable |
| `POST` | `/outgoing-documents/{id}/ai-draft` | Drafter sở hữu document | `AiDraftRequest` | `200 OutgoingDocumentResponse` | `403`, `409`, `503` AI lỗi |
| `POST` | `/outgoing-documents/{id}/reviews` | Drafter sở hữu document | — | `200 ReviewResponse` | `409` status không hợp lệ, `503` AI/rule lỗi |
| `GET` | `/outgoing-documents/{id}/reviews` | BusinessAccess | paging | `200 PagedResponse<ReviewResponse>` | `404` |

AI draft và review chạy đồng bộ với timeout cấu hình. Nếu AI timeout/error, API trả `503` và không ghi đè `content`, `aiDraftContent`, `status`, `reviewIssues` hoặc `review_history`.

## 12. API Approval, Archive Và Full-Text Search

### 12.1. Approval/archive DTO

| DTO | Thuộc tính |
| --- | --- |
| `ApprovalDecisionRequest` | `decision`: `Approve` hoặc `Return` |
| `ArchiveRequest` | `referenceNumber`, `issuedDate` |

`Return` đưa document từ `PendingApproval` về `Editing`; MVP không lưu approval comment riêng vì database chưa có approval history/note.

### 12.2. Endpoint approval và archive

| Method | Path | Auth/role | Request | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `POST` | `/outgoing-documents/{id}/approval` | Leader | `ApprovalDecisionRequest` | `200 OutgoingDocumentResponse` | `403`, `409` review chưa Passed hoặc status sai |
| `POST` | `/outgoing-documents/{id}/archive` | Clerk | `ArchiveRequest` | `200 OutgoingDocumentResponse` | `403`, `409` chưa Approved/số trùng |

### 12.3. Full-text search DTO và endpoint

`DocumentSearchResult`:

| Thuộc tính | Ý nghĩa |
| --- | --- |
| `documentKind` | `Incoming` hoặc `Outgoing` |
| `documentId` | Id tài liệu mở từ kết quả |
| `referenceNumber` | Số/ký hiệu nếu có |
| `title` | Summary với incoming, Title với outgoing |
| `documentType` | `DocumentTypeReference` |
| `documentDate` | Incoming: receivedDate; Outgoing: issuedDate hoặc createdAt nếu chưa phát hành |
| `matchSource` | `Summary`, `Title`, `Content`, `AiDraftContent`, `Attachment` |
| `snippet` | Đoạn text bôi nổi bật từ PostgreSQL search |
| `score` | Điểm phù hợp dùng để sắp xếp giảm dần |

| Method | Path | Auth/role | Query | Thành công | Lỗi đáng chú ý |
| --- | --- | --- | --- | --- | --- |
| `GET` | `/documents/search` | BusinessAccess | `q` (>=2 ký tự), `documentKind?`, `documentTypeId?`, `incomingStatus?`, `outgoingStatus?`, `dateFrom?`, `dateTo?`, `matchSource?`, paging | `200 PagedResponse<DocumentSearchResult>` | `400` q/filter không hợp lệ |

Chỉ attachment có `extractionStatus = Succeeded` mới tạo match nguồn `Attachment`. Search không trả raw `extractedText`.

## 13. Mapping Endpoint Với Use Case

| Use case | Endpoint chính |
| --- | --- |
| FR-001 | `POST /auth/login`, `GET /auth/me`, `POST /auth/change-password` |
| FR-002 | `GET/POST /staff`, `PATCH /staff/{id}`, `PUT /staff/{id}/roles`, `POST /staff/{id}/reset-password` |
| FR-003 | `GET /members`, `GET /members/{id}` |
| FR-004 | `POST /members`, `PATCH /members/{id}`, `POST /members/{id}/deactivate` |
| FR-005 | `GET /members/import-template`, `POST /members/import` |
| FR-006 | `/document-types` và `/document-templates` CRUD/activate endpoints |
| FR-007 | `GET/POST/PATCH /incoming-documents`, `POST /incoming-documents/{id}/complete` |
| FR-008 | Parent attachment upload, `GET /attachments/{id}/download`, `DELETE /attachments/{id}` |
| FR-009 | `POST /incoming-documents/{id}/assignment-suggestion`, `POST /incoming-documents/{id}/assignment` |
| FR-010 | `GET /reminders`, `POST /reminders/{id}/read` |
| FR-011 | `POST /outgoing-documents`, `GET /members/lookup`, active template lookup |
| FR-012 | `PATCH /outgoing-documents/{id}`, `POST /outgoing-documents/{id}/ai-draft` |
| FR-013 | `POST/GET /outgoing-documents/{id}/reviews` |
| FR-014 | `POST /outgoing-documents/{id}/approval` |
| FR-015 | `POST /outgoing-documents/{id}/archive` |
| FR-016 | `GET /documents/search` |

## 14. Ma Trận HTTP Status Và Lỗi

| Status | Khi dùng | ProblemDetails type gợi ý |
| --- | --- | --- |
| `200 OK` | Query, action thành công, import | — |
| `201 Created` | Tạo Staff, Member, type/template, incoming/outgoing, attachment | — |
| `204 No Content` | Reset password, xóa attachment | — |
| `400 Bad Request` | Model validation, enum/date/query không hợp lệ | `validation-error` |
| `401 Unauthorized` | Thiếu, sai hoặc hết hạn JWT; login sai | `unauthorized` |
| `403 Forbidden` | Thiếu role, ownership, Staff inactive hoặc bắt buộc đổi password | `forbidden` / `password-change-required` |
| `404 Not Found` | Resource id không tồn tại | `not-found` |
| `409 Conflict` | Unique conflict hoặc state transition không hợp lệ | `conflict` / `invalid-state` |
| `413 Payload Too Large` | File vượt ngưỡng cấu hình | `file-too-large` |
| `415 Unsupported Media Type` | File/import không đúng loại | `unsupported-file-type` |
| `422 Unprocessable Entity` | Import row errors hoặc FormatRules semantic invalid | `business-validation-failed` |
| `503 Service Unavailable` | AI provider/rule dependency timeout hoặc unavailable | `ai-service-unavailable` |

Không trả stack trace, password, fileUrl, extractedText, raw AI provider response hoặc exception nội bộ trong response production.

## 15. Ghi Chú Triển Khai ASP.NET Core

1. Dùng controller kế thừa `ControllerBase`, gắn `[ApiController]`, route attribute rõ ràng đúng các path `/api/v1/...` trong tài liệu và `[Authorize]` ở boundary.
2. Đăng ký `AddProblemDetails`, JWT authentication, authorization policies, OpenAPI/Swagger và JSON camelCase trong `Program.cs`.
3. `UseAuthentication()` phải chạy trước `UseAuthorization()`.
4. Giữ DTO tách entity; service layer chịu trách nhiệm transaction, state transition và business validation.
5. Dùng `IHttpClientFactory`/typed client cho AI provider; xử lý timeout thành `503` mà không commit mutation.
6. Khi kiến trúc RAG/LLM được duyệt, đặt retrieval, prompt assembly và provider call sau service abstraction; không để controller hoặc client gọi trực tiếp vector store/LLM.
7. Upload dùng streaming/multipart, xác thực loại và dung lượng trước khi lưu file; download stream qua API sau policy check.
8. Reminder Worker và Text Extraction Worker tạo scope/`IDbContextFactory` riêng; không inject DbContext scoped trực tiếp vào singleton hosted service.
9. OpenAPI phải mô tả Bearer security scheme, response `ProblemDetails`, `multipart/form-data` và các enum string ở trên.
10. Thêm integration tests cho status code, role policy, forced password change, transaction review/approval/archive, upload và full-text search filter/pagination.
