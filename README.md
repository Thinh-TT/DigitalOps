# DigitalOps

DigitalOps là hệ thống điều hành số văn bản và hồ sơ hội viên dành cho đơn vị
cấp phường. Hệ thống tập trung quản lý luồng văn bản đến/văn bản đi, hồ sơ hội
viên, phân công xử lý, nhắc hạn và hỗ trợ nghiệp vụ bằng AI có kiểm soát.

AI trong DigitalOps chỉ đóng vai trò gợi ý, tạo bản nháp và kiểm tra thể thức.
Các quyết định điều phối, phê duyệt, phát hành và thay đổi dữ liệu chính thức
luôn do người dùng có thẩm quyền xác nhận.

## Chức năng chính

- Đăng nhập JWT, đổi mật khẩu tạm và kiểm soát truy cập theo Staff/role.
- Quản lý tài khoản Staff, nhiều role, trạng thái hoạt động và reset mật khẩu.
- Quản lý và import hồ sơ hội viên.
- Tiếp nhận, phân loại, điều phối và nhắc hạn văn bản đến.
- Soạn thảo, thẩm định, phê duyệt, cấp số và lưu trữ văn bản đi.
- Quản lý attachment và tìm kiếm toàn văn trên PostgreSQL.
- Tích hợp RAG/LLM qua service abstraction cho các tác vụ AI được phê duyệt.

Trạng thái triển khai chi tiết được theo dõi tại
[Task Board](Project-Document/05-tasks/01-task-board.md).

## Công nghệ

| Thành phần | Công nghệ |
| --- | --- |
| Backend | ASP.NET Core 10 Web API, controller-based |
| Authentication | ASP.NET Core Identity, JWT, role/policy authorization |
| Data | Entity Framework Core 10, PostgreSQL |
| Frontend | React 19, TypeScript, Vite 8, Ant Design 6 |
| Test | xUnit, SQLite in-memory, Vitest, Testing Library |
| API contract | REST `/api/v1`, OpenAPI, ProblemDetails |

## Cấu trúc repository

```text
DigitalOps/
├── DigitalOps.API/          # ASP.NET Core API, Identity và EF Core
├── DigitalOps.API.Tests/    # Unit/integration test backend
├── DigitalOps.Web/          # React/Vite web client
├── Project-Document/        # Yêu cầu, kiến trúc, UI, task và session log
├── DigitalOps.slnx          # .NET solution
└── SETUP.md                 # Hướng dẫn cài đặt và chạy local
```

## Bắt đầu nhanh

Đọc [SETUP.md](SETUP.md) để:

1. cài .NET, Node.js và PostgreSQL;
2. cấu hình `.env` mà không commit secret;
3. áp dụng EF Core migration;
4. bootstrap Administrator đầu tiên;
5. chạy API và Web;
6. xử lý lỗi HTTPS certificate, `Bad Gateway`, database và đăng nhập.

Sau khi cấu hình xong, chạy API và Web trong hai terminal:

```powershell
dotnet run --project DigitalOps.API --launch-profile https
```

```powershell
Set-Location DigitalOps.Web
npm.cmd run dev
```

Swagger development: <https://localhost:7162/swagger>

## Kiểm tra chất lượng

```powershell
dotnet test DigitalOps.slnx --no-restore --verbosity minimal
dotnet format DigitalOps.slnx --no-restore --verify-no-changes

Set-Location DigitalOps.Web
npm.cmd test -- --run
npm.cmd run lint
npm.cmd run build
```

## Tài liệu dự án

- [Mục lục tài liệu](Project-Document/README.md)
- [Phạm vi và ý tưởng](Project-Document/01-project/01-ideas-and-scope.md)
- [Database Designer](Project-Document/02-architecture/01-database-designer.md)
- [API Specification](Project-Document/02-architecture/02-api-spec.md)
- [AI RAG & LLM Design](Project-Document/02-architecture/03-ai-rag-design.md)
- [Functional Requirements](Project-Document/03-functional/01-functional-requirements.md)
- [UI Sitemap và Wireframe](Project-Document/04-ui/01-ui-sitemap-and-wireframe.md)

## Bảo mật cấu hình

Không commit `.env`, `.env.local`, password database, JWT signing key hoặc mật
khẩu bootstrap. Chỉ các file `.env.example` chứa placeholder được lưu trong
repository. Production phải dùng secret store phù hợp thay vì lưu credential
trong source hoặc image triển khai.
