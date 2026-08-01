# Tài liệu dự án DigitalOps

DigitalOps là hệ thống điều hành số văn bản và hồ sơ hội viên tích hợp AI cho UBMTTQ cấp phường. Bộ tài liệu này là nguồn tham chiếu chung cho phân tích nghiệp vụ, thiết kế database/API, React UI, triển khai và kiểm thử MVP.

## Mục lục thư mục

~~~text
Project-Document/
├── 00-customer-request/
│   └── CDS-AI-MTTQ-phuong.md
├── 01-project/
│   └── 01-ideas-and-scope.md
├── 02-architecture/
│   ├── 01-database-designer.md
│   ├── 02-api-spec.md
│   └── 03-ai-rag-design.md
├── 03-functional/
│   └── 01-functional-requirements.md
├── 04-ui/
│   ├── 01-ui-sitemap-and-wireframe.md
│   └── 02-color-guidelines.md
├── 05-tasks/
│   └── 01-task-board.md
├── 06-logs/
│   ├── ai-evaluation/
│   │   ├── README.md
│   │   ├── t0-00-cases.json
│   │   └── t0-00-handoff.md
│   ├── dev-log.md
│   └── session-log/
│       └── 00-template.md
├── AGENT.md
└── README.md
~~~

## Danh mục tài liệu

| Nhóm | Tài liệu | Nội dung |
| --- | --- | --- |
| Yêu cầu khách hàng | 00-customer-request/CDS-AI-MTTQ-phuong.md | Bối cảnh và yêu cầu ban đầu từ khách hàng. |
| Tổng quan | 01-project/01-ideas-and-scope.md | Mục tiêu sản phẩm, phạm vi MVP, ngoài phạm vi và công nghệ. |
| Database | 02-architecture/01-database-designer.md | ERD, bảng, constraint, trạng thái, luồng dữ liệu và full-text search. |
| API | 02-architecture/02-api-spec.md | REST API /api/v1, DTO, JWT/policy, error response và endpoint mapping. |
| AI RAG/LLM | 02-architecture/03-ai-rag-design.md | Kiến trúc local-first, guardrail và evaluation gate do Project Owner phê duyệt. |
| Functional | 03-functional/01-functional-requirements.md | Vai trò, FR-001 đến FR-016 và business rule. |
| UI | 04-ui/01-ui-sitemap-and-wireframe.md | Sitemap React, route, role navigation, wireframe và UI traceability. |
| UI style | 04-ui/02-color-guidelines.md | Không gian dành cho guideline visual ở giai đoạn đại tu UI sau MVP. |
| Delivery | 05-tasks/01-task-board.md | Thứ tự thực hiện, dependency và Definition of Done. |
| Nhật ký | 06-logs/dev-log.md và 06-logs/session-log/ | Quy ước ghi nhận quyết định, issue và lesson theo session; log `Closed` là evidence bất biến. |
| Handoff AI evaluation | 06-logs/ai-evaluation/t0-00-handoff.md | Setup, kiểm tra digest, chạy 45 ca và quy tắc bàn giao T0-00 trên thiết bị khác. |
| Hướng dẫn làm việc | AGENT.md | Quy tắc cho agent/kỹ sư khi thay đổi dự án. |

## Phạm vi MVP

- Quản lý, tìm kiếm và import hồ sơ hội viên.
- Tiếp nhận văn bản đến, AI gợi ý cán bộ xử lý, người dùng xác nhận điều phối và nhắc hạn tự động.
- Tạo văn bản đi theo mẫu, liên kết hội viên/văn bản đến, AI hỗ trợ sinh bản nháp.
- Thẩm định thể thức, phê duyệt, cấp số, phát hành/lưu trữ và quản lý attachment.
- Tìm kiếm toàn văn trong dữ liệu văn bản và file PDF/DOCX/XLSX đã trích xuất text.

AI chỉ hỗ trợ gợi ý, soạn nháp và kiểm tra thể thức. AI không tự điều phối, phê duyệt hoặc ghi đè nội dung người dùng. OCR ảnh/PDF scan, citizen portal, welfare và các tính năng ngoài luồng văn bản không thuộc MVP.

## Công nghệ đã chốt

| Layer | Công nghệ |
| --- | --- |
| Backend | ASP.NET Core Web API, controller-based |
| ORM và database | Entity Framework Core, PostgreSQL |
| Authentication | ASP.NET Core Identity, JWT access token, role policy |
| Frontend | React, Vite, TypeScript, Ant Design |
| AI | RAG local-first do DigitalOps điều phối; Ollama + Qwen3, Qdrant; đang chờ evaluation gate để Approved for MVP/demo |
| Background | IHostedService reminder, text extraction worker |
| File storage | Local disk hoặc S3-compatible bucket |

## Thứ tự đọc đề xuất

1. 00-customer-request/CDS-AI-MTTQ-phuong.md khi cần hiểu bối cảnh khách hàng.
2. 01-project/01-ideas-and-scope.md để xác định phạm vi và giới hạn MVP.
3. 03-functional/01-functional-requirements.md để xác định use case/business rule.
4. 02-architecture/01-database-designer.md và 02-architecture/02-api-spec.md cho thiết kế/triển khai backend.
5. 02-architecture/03-ai-rag-design.md trước mọi task AI; quyết định đã khóa nhưng tài liệu còn Draft cho đến khi Project Owner xác nhận đủ evaluation gate.
6. 04-ui/01-ui-sitemap-and-wireframe.md khi làm React UI.
7. 05-tasks/01-task-board.md để theo dõi thứ tự thực hiện và Definition of Done.
8. 06-logs/ để biết quyết định kỹ thuật trước đó; xem AGENT.md trước khi bắt đầu thay đổi.

## Liên kết theo module

| Module | Tài liệu cần đọc |
| --- | --- |
| Identity, JWT và Staff | Functional Requirements, API Specification, Database Designer |
| Hội viên và import Excel | Functional Requirements, API Specification, Database Designer, UI Sitemap |
| Văn bản đến, assignment, reminder | Functional Requirements, Database Designer, API Specification, UI Sitemap |
| Văn bản đi, AI, review, approval, archive | Functional Requirements, Database Designer, API Specification, UI Sitemap |
| RAG/LLM | AI RAG Design, Functional Requirements, Database Designer và API Specification |
| Attachment, extraction, full-text search | Database Designer, API Specification, UI Sitemap |
| React UI | UI Sitemap/Wireframe, API Specification, Functional Requirements |
| Task/log | Task Board, Dev Log, Session Log và AGENT.md |

## Quy ước bảo trì tài liệu

- Dùng Markdown UTF-8, tên file tiếng Anh dạng kebab-case với tiền tố số thứ tự trong mỗi nhóm.
- Document changes phải giữ nhất quán giữa Functional Requirements, Database Designer, API Specification và UI Sitemap.
- API dùng /api/v1, DTO camelCase, lỗi ProblemDetails; database dùng PostgreSQL snake_case và UUID.
- Tạo session log mới theo mẫu 06-logs/session-log/log-yyyymmdd-task.md khi có quyết định hoặc blocker đáng lưu.
- Task chỉ được đánh dấu hoàn thành khi Definition of Done và kiểm tra tương ứng đã đạt.
