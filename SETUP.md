# Cài đặt và chạy DigitalOps

Tài liệu này hướng dẫn cấu hình môi trường development và chạy DigitalOps trên
máy local. Các lệnh mẫu dùng PowerShell trên Windows. Với macOS/Linux, thay
`Copy-Item` bằng `cp` và `npm.cmd` bằng `npm`.

## 1. Yêu cầu hệ thống

Cài đặt các thành phần sau:

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- Node.js 24 và npm.
- PostgreSQL server đang hoạt động.
- Git.
- EF Core CLI 10.x để áp dụng migration.

Kiểm tra phiên bản:

```powershell
dotnet --version
node --version
npm.cmd --version
psql --version
```

Cài EF Core CLI nếu máy chưa có:

```powershell
dotnet tool install --global dotnet-ef --version "10.*"
```

Nếu đã cài từ trước:

```powershell
dotnet tool update --global dotnet-ef --version "10.*"
```

## 2. Chuẩn bị PostgreSQL

Khởi động PostgreSQL và tạo database dành cho DigitalOps. Ví dụ với user
`postgres`:

```powershell
createdb -U postgres DigitalOps
```

Hoặc chạy SQL:

```sql
CREATE DATABASE "DigitalOps";
```

Tên database, username và password có thể khác; chúng phải khớp connection
string được cấu hình ở bước tiếp theo.

## 3. Cấu hình API

Từ thư mục gốc repository:

```powershell
Copy-Item DigitalOps.API/.env.example DigitalOps.API/.env
```

Mở `DigitalOps.API/.env` và thay toàn bộ placeholder. Không commit file này.

| Biến | Bắt buộc | Ý nghĩa |
| --- | --- | --- |
| `ConnectionStrings__DigitalOps` | Có | Connection string PostgreSQL |
| `Jwt__Issuer` | Có | Issuer của access token |
| `Jwt__Audience` | Có | Audience của access token |
| `Jwt__SigningKey` | Có | Secret ký JWT, tối thiểu 32 byte UTF-8 |
| `Jwt__AccessTokenLifetimeMinutes` | Có | Thời hạn access token |
| `IdentityBootstrap__Enabled` | Có | Bật/tắt tạo Administrator đầu tiên |
| `IdentityBootstrap__UserName` | Khi bootstrap | Username Administrator |
| `IdentityBootstrap__Email` | Khi bootstrap | Email Administrator |
| `IdentityBootstrap__TemporaryPassword` | Khi bootstrap | Mật khẩu tạm đạt Identity policy |
| `IdentityBootstrap__FullName` | Khi bootstrap | Họ tên Administrator |
| `IdentityBootstrap__Position` | Không | Chức vụ |
| `IdentityBootstrap__Department` | Không | Bộ phận |
| `IdentityBootstrap__Phone` | Không | Điện thoại |

Ví dụ connection string:

```dotenv
ConnectionStrings__DigitalOps=Host=localhost;Port=5432;Database=DigitalOps;Username=postgres;Password=YOUR_PASSWORD
```

Tạo JWT signing key ngẫu nhiên bằng PowerShell:

```powershell
[Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
)
```

Sao chép kết quả vào `Jwt__SigningKey`. Không dùng giá trị placeholder trong
`.env.example`.

## 4. Restore và áp dụng migration

Initializer của API không tự chạy migration. Database phải được migrate trước
khi khởi động API:

```powershell
dotnet restore DigitalOps.slnx
dotnet ef database update `
  --project DigitalOps.API `
  --startup-project DigitalOps.API
```

Migration hiện có sẽ tạo các bảng nghiệp vụ và bảng ASP.NET Core Identity trong
PostgreSQL.

## 5. Tạo Administrator đầu tiên

Bỏ qua bước này nếu database đã có một Staff active mang role
`Administrator`.

Trong `DigitalOps.API/.env`, cấu hình:

```dotenv
IdentityBootstrap__Enabled=true
IdentityBootstrap__UserName=admin
IdentityBootstrap__Email=admin@example.local
IdentityBootstrap__TemporaryPassword=REPLACE_WITH_A_STRONG_TEMPORARY_PASSWORD
IdentityBootstrap__FullName=Quản trị viên hệ thống
```

Khi API khởi động:

1. bốn role `Administrator`, `Clerk`, `Drafter`, `Leader` luôn được bảo đảm tồn
   tại theo cách idempotent;
2. nếu chưa có Administrator active, hệ thống tạo Identity user và Staff;
3. tài khoản mới bắt buộc đổi mật khẩu tạm trước khi truy cập nghiệp vụ.

Sau khi bootstrap thành công:

1. đăng nhập và đổi mật khẩu tạm;
2. đặt `IdentityBootstrap__Enabled=false`;
3. khởi động lại API.

Bootstrap không reset hoặc ghi đè tài khoản đã tồn tại. Không để mật khẩu tạm
thật trong `.env.example`, source code hoặc log.

## 6. Chuẩn bị HTTPS development certificate

Web mặc định proxy `/api` tới `https://localhost:7162`. Kiểm tra certificate:

```powershell
dotnet dev-certs https --check
```

Nếu không có certificate hợp lệ:

```powershell
dotnet dev-certs https --trust
```

Chấp nhận hộp thoại trust của Windows, sau đó đóng và mở lại terminal/IDE nếu
cần.

## 7. Chạy API

Từ thư mục gốc repository:

```powershell
dotnet run --project DigitalOps.API --launch-profile https
```

Khi thành công, terminal hiển thị:

```text
Now listening on: https://localhost:7162
Now listening on: http://localhost:5162
```

Các URL development:

- Swagger: <https://localhost:7162/swagger>
- OpenAPI JSON: <https://localhost:7162/openapi/v1.json>
- API base URL: `https://localhost:7162/api/v1`

Giữ terminal API đang chạy.

## 8. Cấu hình và chạy Web

Tạo cấu hình local cho Vite:

```powershell
Copy-Item DigitalOps.Web/.env.example DigitalOps.Web/.env.local
```

Giá trị mặc định:

```dotenv
VITE_API_BASE_URL=/api/v1
VITE_DEV_API_TARGET=https://localhost:7162
```

Không đưa JWT signing key, database password hoặc credential bootstrap vào biến
`VITE_*`; các biến này có thể xuất hiện trong bundle phía trình duyệt.

Cài dependency và chạy Web:

```powershell
Set-Location DigitalOps.Web
npm.cmd ci
npm.cmd run dev
```

Mở URL Vite in ra terminal, mặc định là <http://localhost:5173>. Vite chuyển
tiếp request `/api` tới API; vì vậy nên khởi động API trước Web.

## 9. Chạy bằng HTTP khi chưa dùng certificate

Chỉ dùng phương án này cho development local.

Terminal API:

```powershell
dotnet run --project DigitalOps.API --launch-profile http
```

Đổi `DigitalOps.Web/.env.local`:

```dotenv
VITE_API_BASE_URL=/api/v1
VITE_DEV_API_TARGET=http://localhost:5162
```

Sau đó khởi động lại Vite:

```powershell
Set-Location DigitalOps.Web
npm.cmd run dev
```

## 10. Kiểm tra hệ thống

Sau khi API và Web chạy:

1. mở Swagger và xác nhận OpenAPI tải được;
2. mở Web và đăng nhập bằng Administrator;
3. nếu là tài khoản bootstrap, đổi mật khẩu tạm;
4. mở màn hình Staff để xác nhận role và dữ liệu database.

Chạy test và quality gate:

```powershell
dotnet test DigitalOps.slnx --no-restore --verbosity minimal
dotnet format DigitalOps.slnx --no-restore --verify-no-changes
dotnet list DigitalOps.slnx package --vulnerable --include-transitive

Set-Location DigitalOps.Web
npm.cmd test -- --run
npm.cmd run lint
npm.cmd run build
npm.cmd audit --audit-level=moderate
```

## 11. Xử lý lỗi thường gặp

### Web hiển thị `Bad Gateway`

Vite không kết nối được tới API upstream.

Kiểm tra:

1. API có đang chạy và có dòng `Now listening on` hay không;
2. `VITE_DEV_API_TARGET` có đúng scheme/port của profile API hay không;
3. với HTTPS, chạy `dotnet dev-certs https --check`;
4. sau khi đổi `.env.local`, khởi động lại Vite.

Mapping mặc định:

| API profile | API URL | `VITE_DEV_API_TARGET` |
| --- | --- | --- |
| `https` | `https://localhost:7162` | `https://localhost:7162` |
| `http` | `http://localhost:5162` | `http://localhost:5162` |

### API báo lỗi certificate

Nếu có lỗi `Unable to configure HTTPS endpoint` hoặc
`No valid certificate found`:

```powershell
dotnet dev-certs https --trust
```

Sau đó khởi động lại API.

### API báo không kết nối được PostgreSQL

- Xác nhận PostgreSQL đang chạy và lắng nghe đúng host/port.
- Kiểm tra database, username và password trong
  `ConnectionStrings__DigitalOps`.
- Không dán connection string hoặc password vào issue/log công khai.

### API báo thiếu bảng hoặc relation

Áp dụng migration trước khi chạy API:

```powershell
dotnet ef database update `
  --project DigitalOps.API `
  --startup-project DigitalOps.API
```

### Đăng nhập trả `401`

- Kiểm tra Administrator đầu tiên đã được bootstrap hay chưa.
- Xác nhận Staff đang active.
- Username/email hoặc mật khẩu sai đều trả cùng một lỗi xác thực chung.

### Nghiệp vụ trả `403 password-change-required`

Tài khoản đang dùng mật khẩu tạm hoặc vừa được Administrator reset mật khẩu.
Mở `/change-password`, đổi mật khẩu và đăng nhập/tiếp tục bằng token mới.

## 12. Lưu ý bảo mật

- `.env` và `.env.local` đã được Git ignore; vẫn cần kiểm tra trước khi commit.
- Không commit secret, credential, access token hoặc file log chứa dữ liệu nhạy
  cảm.
- Chỉ dùng HTTPS cho môi trường được chia sẻ hoặc production.
- Production nên lấy secret từ secret manager/vault của nền tảng triển khai,
  không phụ thuộc file `.env`.
- Tắt bootstrap sau khi provision Administrator đầu tiên.
