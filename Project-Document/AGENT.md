# DigitalOps Agent Instructions

Hướng dẫn cho AI agent và kỹ sư khi làm việc trong dự án DigitalOps – Hệ thống điều hành số văn bản và hồ sơ hội viên tích hợp AI cho UBMTTQ cấp phường.

## 1. Bối cảnh và công nghệ

| Layer | Công nghệ/quy ước |
| --- | --- |
| Backend | ASP.NET Core Web API, controller-based, tổ chức theo feature |
| Data | Entity Framework Core, PostgreSQL, một database và một schema |
| Identity | ASP.NET Core Identity, JWT access token, role policy |
| Frontend | React + Vite + TypeScript + Ant Design |
| AI | RAG local-first do DigitalOps điều phối; Ollama + Qwen3, Qdrant; chờ evaluation gate để Project Owner phê duyệt MVP/demo |
| Background | IHostedService cho reminder; worker trích xuất text cho file hỗ trợ |
| File storage | Local disk có tổ chức thư mục hoặc S3-compatible bucket |

MVP xử lý hồ sơ hội viên, văn bản đến, điều phối có AI gợi ý, nhắc hạn, văn bản đi, AI draft, review, phê duyệt, lưu trữ, attachment và tìm kiếm toàn văn. Không mở rộng sang citizen portal, welfare, OCR hoặc điều phối tự động không có xác nhận con người.

## 2. Nguồn sự thật và thứ tự ưu tiên

| Tài liệu | Vai trò |
| --- | --- |
| 00-customer-request/CDS-AI-MTTQ-phuong.md | Bối cảnh yêu cầu khách hàng ban đầu |
| 01-project/01-ideas-and-scope.md | Phạm vi MVP, công nghệ và giới hạn sản phẩm |
| 03-functional/01-functional-requirements.md | Use case FR-001 đến FR-016, role và business rule |
| 02-architecture/01-database-designer.md | Schema, quan hệ, trạng thái, constraint và migration rule |
| 02-architecture/02-api-spec.md | REST contract /api/v1, DTO, quyền, error response |
| 02-architecture/03-ai-rag-design.md | Quyết định RAG/LLM local-first, guardrail và evaluation gate do Project Owner duyệt |
| 04-ui/01-ui-sitemap-and-wireframe.md | Route web, màn hình, wireframe và UI traceability |
| 05-tasks/01-task-board.md | Theo dõi thứ tự triển khai, dependency và Definition of Done |
| 06-logs/dev-log.md, 06-logs/session-log/ | Quyết định, blocker và bài học của các session trước |

Khi tài liệu mâu thuẫn, giữ ý định sản phẩm trong Ideas and Scope và Functional Requirements; sau đó đồng bộ Database Designer, API Specification và UI Sitemap trước khi viết code. Chỉ triển khai code nghiệp vụ RAG/LLM sau khi AI RAG Design đạt đủ gate và được Project Owner đánh dấu Approved for MVP/demo. Không tự suy diễn endpoint, bảng, trạng thái hoặc role mới.

## 3. Cách bắt đầu một task

1. Xác định task/Definition of Done trong Task Board nếu task đã được lập.
2. Đọc Ideas and Scope, rồi Functional Requirements tương ứng.
3. Đọc API Specification cho API/frontend; đọc Database Designer cho entity, migration, query và state transition; đọc UI Sitemap cho React UI.
4. Kiểm tra code hiện có để tái sử dụng pattern trước khi thêm module hoặc abstraction.
5. Giới hạn thay đổi trong feature đang làm. Không refactor hoặc sửa tài liệu ngoài phạm vi nếu chưa có lý do rõ ràng.
6. Viết/chỉnh test phù hợp, chạy kiểm tra cần thiết và chỉ đánh dấu Done khi Definition of Done đạt.
7. Cập nhật tài liệu contract nếu thay đổi có tác động; ghi session log khi có quyết định kỹ thuật, blocker hoặc bài học đáng lưu.

## 4. Backend và dữ liệu

### 4.1. API

- Dùng ControllerBase, ApiController, route rõ ràng dưới /api/v1 và Authorize ở boundary.
- Không expose EF entity trực tiếp. Request/response dùng DTO trong API Specification, JSON camelCase và enum dạng string.
- Resource đơn trả DTO trực tiếp; list trả PagedResponse. Lỗi dùng ProblemDetails hoặc ValidationProblemDetails.
- Phân quyền theo role Administrator, Clerk, Drafter, Leader; kiểm tra thêm ownership và trạng thái resource trong service.
- Staff có mustChangePassword = true chỉ được gọi GET /auth/me và POST /auth/change-password. Không thêm refresh token hoặc server-side logout trong MVP.
- Không trả secret, password hash, fileUrl, extractedText, raw provider response, stack trace hoặc exception nội bộ.

### 4.2. Entity, migration và state

- PostgreSQL dùng snake_case; entity C# PascalCase; primary key uuid; thời điểm timestamptz UTC; ngày nghiệp vụ date; JSON lưu jsonb.
- Migration EF Core là nguồn tạo schema. Không chỉnh database thủ công trừ trường hợp được phê duyệt và có log.
- Không xóa cứng Members, Staff, DocumentTypes hoặc DocumentTemplates đã được tham chiếu; dùng Status/IsActive.
- Tôn trọng các CHECK constraint, foreign key, index và transaction boundary nêu trong Database Designer.
- Chỉ cho phép state transition hợp lệ: incoming New → InProgress → Overdue/Completed; outgoing AiDraft/Editing → PendingReview → ReviewFailed/PendingApproval → Approved → Archived.
- Mỗi review tạo ReviewHistory với AttemptNo và ContentSnapshot. AiDraftContent giữ bản AI đầu tiên, không bị ghi đè khi người dùng chỉnh sửa.

### 4.3. AI, reminder, attachment và search

- AI RAG Design đang Draft vì chưa đủ evaluation evidence; model/provider/vector store đã khóa và không được tự thay đổi. Không thêm schema embedding, public API RAG hoặc code nghiệp vụ trước khi Project Owner phê duyệt cho MVP/demo.
- Khi tiếp nhận T0-00 trên thiết bị khác, bắt buộc dùng `06-logs/ai-evaluation/t0-00-handoff.md` và baseline `T0-00-RAG-MVP-20260731-v1`. Không sửa log đã `Closed`, không ghép metric giữa các máy; mỗi lượt tạo session log mới.
- AI chỉ gợi ý điều phối, sinh draft hoặc review; không tự điều phối/phê duyệt. Timeout/lỗi AI trả 503 và không mutation dữ liệu hiện có.
- RAG index là dữ liệu dẫn xuất, không phải source of truth. PostgreSQL full-text search của FR-016 vẫn là search contract chính thức.
- Context truy hồi là dữ liệu không tin cậy: filter quyền trước retrieval, giảm thiểu dữ liệu gửi provider và không log raw prompt/completion nhạy cảm mặc định.
- Reminder worker chạy idempotent; không có public job endpoint.
- Upload chỉ chấp nhận PDF, DOCX, XLSX, JPG, JPEG, PNG và dung lượng theo cấu hình. Attachment chỉ thuộc đúng một parent.
- Trích xuất text chạy nền cho PDF có text, DOCX, XLSX. Không OCR ảnh/PDF scan trong MVP.
- Full-text search dùng PostgreSQL và chỉ trả attachment match khi extractionStatus là Succeeded.

## 5. Frontend React

- Dùng React + Vite + TypeScript + Ant Design; UI MVP desktop-first từ 1024px và ưu tiên kiểm thử chức năng.
- Giữ App Shell, route guard, role-based navigation và screen/action rules đúng UI Sitemap.
- Sau login, nạp GET /auth/me; 401 xóa phiên và về /login; 403 hiển thị Forbidden. Khi mustChangePassword bật, chỉ cho route đổi mật khẩu/đăng xuất.
- Không tạo dashboard, API, WYSIWYG, mobile layout hay visual system mới ngoài contract đã chốt.
- Form hiển thị validation 400/422 theo trường; 409 không tự ghi đè dữ liệu; 503 AI giữ nguyên nội dung người dùng đang có.
- Action chỉ hiện/enable khi role, ownership và status API cho phép. Sau mutation, dùng resource API trả về thay vì tự suy diễn trạng thái client.
- Download file qua API stream; không lộ storage path hoặc file URL.

## 6. Kiểm thử tối thiểu

| Loại | Yêu cầu |
| --- | --- |
| Unit | Service/business rule, state transition, validation, AI failure không mutation |
| Integration API | JWT/role/ownership, paging/filter, ProblemDetails, migration/constraint, upload/download, search |
| Frontend | DTO/API client, route guard, form validation, loading/empty/error/forbidden, action theo role/status |
| End-to-end/manual | Nhập hội viên; incoming → assignment → reminder → complete; outgoing → AI/review → approval → archive; import rollback; search toàn văn |

Luôn kiểm tra các mã chính: 200, 201, 204, 400, 401, 403, 404, 409, 413, 415, 422 và 503 khi áp dụng.

## 7. Tài liệu và session log

- Khi sửa schema, API, role, trạng thái hoặc flow, cập nhật các tài liệu phụ thuộc trong cùng task hoặc nêu rõ blocker.
- Dùng đường dẫn Project-Document/... trong tài liệu dự án.
- Tạo session log mới tại 06-logs/session-log/log-yyyymmdd-task.md; không sửa 00-template.md cho log vận hành thường ngày.
- Session log đã đánh dấu `Closed` là evidence bất biến. Muốn bổ sung hoặc sửa kết luận phải tạo log mới và liên kết ngược về log cũ.
- Log ngắn gọn, tập trung vào lý do, quyết định/tác động, kiểm tra đã chạy và việc cần theo dõi. Tham chiếu task ID nếu Task Board đã có ID.

## 8. Commit convention

~~~text
<type>: <short description>
~~~

Các type dùng: feat, fix, refactor, docs, style, test, chore.

Ví dụ:

~~~text
feat: add incoming document assignment api
fix: preserve draft when ai service times out
docs: align api specification with review flow
test: cover archive state transition
~~~
