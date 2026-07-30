# Session Log - 2026-07-31 Project Documentation Alignment

- **Ngày**: 2026-07-31
- **Người thực hiện**: Codex
- **Task liên quan**: Project Documentation Alignment (chưa có ID trên Task Board)
- **Loại**: Decision
- **Trạng thái**: Hoàn thành

## Bối cảnh

Xây dựng bộ tài liệu chuẩn hóa kiến trúc, quy trình. Làm nguồn chuẩn cho dev/AI Coding Agent triển khai dự án tránh việc bám sai stack, API hoặc phạm vi MVP.

## Quyết định

- Lấy 01-project/01-ideas-and-scope.md, 03-functional/01-functional-requirements.md, 02-architecture/01-database-designer.md, 02-architecture/02-api-spec.md và 04-ui/01-ui-sitemap-and-wireframe.md làm bộ nguồn sự thật cho DigitalOps.
- Chuẩn hóa AGENT.md theo ASP.NET Core Web API controller-based, EF Core/PostgreSQL, ASP.NET Core Identity + JWT, React/Vite/TypeScript/Ant Design, AI service abstraction, reminder worker và text extraction worker.
- Chuẩn hóa README thành catalog tài liệu DigitalOps, phạm vi MVP, tech stack, thứ tự đọc và liên kết theo module.
- Quy ước log tương lai dùng file mới tại 06-logs/session-log/log-yyyymmdd-task.md; file 00-template.md lưu lại entry khởi tạo/đồng bộ tài liệu này.

## Tác động

- Agent và kỹ sư có thứ tự đọc tài liệu, rule API/database/UI và tiêu chí test thống nhất trước khi bắt đầu code.
- Các quy tắc cũ về Flutter, SQL Server, SignalR, chat, rental, mobile và APK không còn áp dụng cho DigitalOps.
- Task Board hiện vẫn có nội dung cũ/chưa hoàn thiện; cần được đồng bộ trong một task tài liệu riêng trước khi dùng làm kế hoạch triển khai chi tiết.

## Kiểm tra đã thực hiện

- Đối chiếu stack, MVP và giới hạn sản phẩm với Ideas and Scope.
- Đối chiếu FR-001 đến FR-016, schema/database rules, API /api/v1 và UI sitemap.
- Kiểm tra tất cả đường dẫn tài liệu trong AGENT.md và README đều nằm dưới Project-Document.

## Theo dõi tiếp

- Hoàn thiện 05-tasks/01-task-board.md theo các phase và Definition of Done của DigitalOps.
- Bổ sung color guideline khi bắt đầu đợt đại tu UI sau khi MVP vận hành ổn định.
