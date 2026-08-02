# Mẫu thử nền tảng — API, database, App Shell và AI runtime

## Mục tiêu

Xác nhận các thành phần nền tảng đã hoàn thành ở Phase 0 sẵn sàng trước khi chạy
các mẫu nghiệp vụ FR-001 đến FR-012.

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| PLAT-01 | Build backend | Chạy `dotnet build DigitalOps.slnx --no-restore` | Build thành công, không warning/error mới |
| PLAT-02 | Test backend | Chạy `dotnet test DigitalOps.slnx --no-restore` | Toàn bộ test pass; không test bị bỏ qua ngoài chủ đích |
| PLAT-03 | Frontend gates | Chạy `npm.cmd test -- --run`, `npm.cmd run lint`, `npm.cmd run build` trong `DigitalOps.Web` | Test, lint và production build đều pass |
| PLAT-04 | Migration/model | Chạy `dotnet ef migrations has-pending-model-changes --project DigitalOps.API --startup-project DigitalOps.API --no-build` | Không có pending model change ngoài task migration đã duyệt |
| PLAT-05 | API startup | Khởi động API bằng profile Development | API start, kết nối PostgreSQL; cấu hình sai bắt buộc fail-fast, không chạy nửa vời |
| PLAT-06 | OpenAPI | Mở document OpenAPI/Swagger | Tải được; có Bearer scheme, DTO camelCase, enum dạng string và các response công khai |
| PLAT-07 | ProblemDetails | Gọi một endpoint bảo vệ không token, một request validation sai và một GUID không tồn tại | Nhận `401`, `400`, `404` với `application/problem+json`, status/detail/instance đúng |
| PLAT-08 | App Shell | Đăng nhập tài khoản active, kiểm tra sidebar/header/user menu/reminder badge | Shell render đúng role; route hiện tại hoạt động; logout/change-password truy cập được |
| PLAT-09 | Route guard | Mở route bảo vệ khi anonymous, sai role và mustChangePassword | Chuyển Login/Forbidden/Change Password đúng trường hợp; không nháy nội dung trái quyền |
| PLAT-10 | Qdrant | Kiểm tra container loopback có API key và collection cấu hình | Không key bị từ chối; key đúng truy cập được; không bind public ngoài cấu hình |
| PLAT-11 | Ollama embedding | Gọi embedding synthetic/redacted | Vector trả đúng model/digest và 1024 chiều |
| PLAT-12 | AI provider | Chạy một request synthetic qua operation đã có | Dùng đúng provider/model Development; không automatic fallback; timeout/gate theo cấu hình |
| PLAT-13 | Secret/log safety | Rà log startup và response client | Không lộ connection password, JWT signing key, AI/Qdrant key, access token hoặc raw prompt |
| PLAT-14 | Repository hygiene | Chạy `dotnet format ... --verify-no-changes` và `git diff --check` | Formatter/diff check sạch; không có migration/file build ngoài dự kiến |

## Điều kiện cho phép chạy FR

- PLAT-05 đến PLAT-09 phải Pass cho mọi mẫu FR.
- PLAT-10 đến PLAT-12 có thể đánh dấu Blocked khi chỉ chạy ca không-AI; bắt buộc
  Pass trước FR-009 và FR-012.
