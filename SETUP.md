# Cài đặt và chạy DigitalOps

Tài liệu này hướng dẫn cấu hình môi trường development và chạy DigitalOps trên
máy local. Các lệnh mẫu dùng PowerShell trên Windows. Với macOS/Linux, thay
`Copy-Item` bằng `cp` và `npm.cmd` bằng `npm`.

## 1. Yêu cầu hệ thống

Cài đặt các thành phần sau:

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- Node.js 24 và npm.
- PostgreSQL server đang hoạt động.
- Docker Desktop hoặc Docker Engine để chạy Qdrant cho các chức năng AI.
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
| `MemberImport__MaxFileSizeBytes` | Không | Dung lượng tối đa file XLSX; mặc định 10 MiB |
| `MemberImport__MaxRows` | Không | Số dòng dữ liệu tối đa mỗi lần import; mặc định 10.000 |
| `MemberImport__MaxExpandedWorkbookBytes` | Không | Tổng dung lượng giải nén XLSX tối đa; mặc định 100 MiB |
| `AttachmentStorage__RootPath` | Không | Thư mục local lưu attachment ngoài web root; mặc định `App_Data/attachments` |
| `AttachmentStorage__MaxFileSizeBytes` | Không | Dung lượng tối đa mỗi attachment; mặc định 10 MiB, tối đa cấu hình 100 MiB |
| `ReminderWorker__Enabled` | Không | Bật Reminder Worker; mặc định `true` |
| `ReminderWorker__RunIntervalMinutes` | Không | Chu kỳ quét reminder; mặc định 15 phút, từ 1 đến 1.440 phút |
| `ReminderWorker__BeforeDeadlineDays` | Không | Số ngày lịch nhắc trước hạn; mặc định 3, từ 1 đến 365 |
| `ReminderWorker__TimeZoneId` | Không | Múi giờ ngày nghiệp vụ; mặc định `Asia/Ho_Chi_Minh`, nhận IANA hoặc Windows ID |
| `DocumentCatalogSeed__Enabled` | Không | Seed 7 loại và 7 mẫu văn bản cho local/demo; mặc định `false` |
| `IdentityBootstrap__Enabled` | Có | Bật/tắt tạo Administrator đầu tiên |
| `IdentityBootstrap__UserName` | Khi bootstrap | Username Administrator |
| `IdentityBootstrap__Email` | Khi bootstrap | Email Administrator |
| `IdentityBootstrap__TemporaryPassword` | Khi bootstrap | Mật khẩu tạm đạt Identity policy |
| `IdentityBootstrap__FullName` | Khi bootstrap | Họ tên Administrator |
| `IdentityBootstrap__Position` | Không | Chức vụ |
| `IdentityBootstrap__Department` | Không | Bộ phận |
| `IdentityBootstrap__Phone` | Không | Điện thoại |
| `Ai__Provider` | Có | `Ollama` cho demo chính thức hoặc `External` chỉ trong Development |
| `Ai__External__StructuredOutputMode` | Khi `External` | `JsonSchema` cho provider hỗ trợ schema; `JsonObject` cho DeepSeek |
| `Ai__External__DisableThinking` | Không | Đặt `true` cho DeepSeek JSON mode để nhận `message.content` thay vì chỉ reasoning |
| `Ai__Qdrant__BaseUrl` | Có | Qdrant HTTP loopback; baseline dùng `http://127.0.0.1:6333` |
| `Ai__Qdrant__ApiKey` | Có | API key riêng của Qdrant, không commit hoặc đưa vào frontend |
| `Rag__QdrantGrpcHost` | Worker | Qdrant gRPC loopback; mặc định `127.0.0.1` |
| `Rag__QdrantGrpcPort` | Worker | Qdrant gRPC; mặc định `6334` |
| `Ai__Qdrant__CollectionName` | Có | Khóa ở `digitalops_knowledge_v1` |
| `Ai__Qdrant__MinScore` | Có | Khóa ở `0.316666` theo baseline v3 |

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

### Seed danh mục loại và mẫu văn bản cho local/demo

Database phải được migrate trước khi seed. Khi cần dữ liệu mẫu, đặt trong
`DigitalOps.API/.env`:

```dotenv
DocumentCatalogSeed__Enabled=true
```

Lần khởi động tiếp theo, API bổ sung 7 loại văn bản và 7 mẫu còn thiếu. Seed dùng
`Code` của loại và cặp `Code + tên mẫu` làm khóa nhận diện; dữ liệu đã được
Administrator sửa hoặc vô hiệu hóa không bị ghi đè. Nếu một loại đã vô hiệu hóa,
mẫu còn thiếu của loại đó được bỏ qua và ghi warning.

Sau khi log báo seed hoàn tất, đặt lại:

```dotenv
DocumentCatalogSeed__Enabled=false
```

Seed chỉ dành cho local/demo, không bật trong production. Cơ chế này không tự áp
dụng migration và không tạo schema mới.

### Chuẩn bị local attachment storage

Mặc định API lưu file dưới `DigitalOps.API/App_Data/attachments`; thư mục được
tạo ở lần upload đầu tiên, nằm ngoài web root và đã được gitignore. Có thể cấu
hình đường dẫn tuyệt đối ngoài repository:

```dotenv
AttachmentStorage__RootPath=D:\DigitalOpsData\attachments
AttachmentStorage__MaxFileSizeBytes=10485760
```

Tài khoản chạy API phải có quyền tạo/đọc/ghi/xóa trong thư mục này. Khi deploy,
mount storage bền vững và backup riêng cùng database; không trỏ root vào
`wwwroot`, ổ đĩa gốc hoặc thư mục tạm không được persist. T2-03 chỉ lưu file và
đặt trạng thái `Pending`/`Unsupported`; worker trích xuất text thuộc T4-01.

### Reminder Worker

Reminder Worker chạy trong chính API, khởi chạy một vòng ngay khi host sẵn sàng
và sau đó quét theo `ReminderWorker__RunIntervalMinutes`. Ngày nghiệp vụ mặc
định theo Việt Nam; timestamp API/database vẫn là UTC. Mặc định worker tạo một
nhắc trước hạn đúng 3 ngày lịch, nhắc vào ngày đến hạn và một nhắc quá hạn mỗi
ngày cho đến khi tài liệu hoàn tất.

```dotenv
ReminderWorker__Enabled=true
ReminderWorker__RunIntervalMinutes=15
ReminderWorker__BeforeDeadlineDays=3
ReminderWorker__TimeZoneId=Asia/Ho_Chi_Minh
```

Không có endpoint hoặc nút UI chạy worker thủ công. Worker chỉ tạo reminder cho
incoming document đã giao Staff; incoming document chưa hoàn tất nhưng quá hạn
vẫn được chuyển `Overdue`. Unique key database ngăn reminder trùng nếu job chạy
lại cùng ngày. API sẽ fail-fast khi múi giờ hoặc giới hạn cấu hình không hợp lệ.

### Chuẩn bị Ollama embedding cho AI/RAG

Trên Windows, cài Ollama native theo user scope từ
`https://ollama.com/download/OllamaSetup.exe`. Ollama chạy API loopback tại
`http://127.0.0.1:11434`; không cần tải local chat model khi `Ai__Provider=External`.

Chỉ pull model embedding baseline:

```powershell
ollama pull qwen3-embedding:0.6b
ollama list
```

Model phải có digest bắt đầu `ac6da0dfba84` và endpoint `/api/embed` phải trả
vector 1024 chiều. `Ai__Embedding__Model`, digest và dimensions phải giữ đúng
baseline trong `.env`.

Khi dùng DeepSeek V4 Flash, đặt:

```dotenv
Ai__Provider=External
Ai__External__StructuredOutputMode=JsonObject
Ai__External__DisableThinking=true
```

Client sẽ gửi `response_format: { "type": "json_object" }` và tự kiểm tra
schema/guardrail ở server. Provider khác có thể tiếp tục dùng `JsonSchema`.

### Chuẩn bị Qdrant cho AI/RAG

Qdrant chỉ bind loopback, dùng named volume và bắt buộc API key. Tạo một key
ngẫu nhiên, lưu cùng giá trị vào `Ai__Qdrant__ApiKey` trong `.env`, rồi chạy:

```powershell
$env:DIGITALOPS_QDRANT_API_KEY = [Convert]::ToBase64String(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
)
docker volume create digitalops-qdrant-storage
docker run --name digitalops-qdrant `
  --detach `
  --restart unless-stopped `
  --publish 127.0.0.1:6333:6333 `
  --publish 127.0.0.1:6334:6334 `
  --volume digitalops-qdrant-storage:/qdrant/storage `
  --env QDRANT__SERVICE__API_KEY=$env:DIGITALOPS_QDRANT_API_KEY `
  --env QDRANT__TELEMETRY_DISABLED=true `
  qdrant/qdrant@sha256:0bd98fa7977f1e75694779359ca4e212822e5a71334e28421182f72f209d5286
```

API tạo collection `digitalops_knowledge_v1` ở lần gọi AI đầu tiên và đồng bộ
lazy các Staff active cho gợi ý điều phối, cùng các Template active cho sinh nháp.
Mỗi nguồn được đồng bộ/xóa stale độc lập; thao tác Template không ảnh hưởng point
Staff hoặc FormatRule. PostgreSQL vẫn là source of truth; Qdrant không cần EF
migration và named volume không thay thế backup nghiệp vụ. Nếu Ollama/Qdrant
không khả dụng, endpoint AI trả `503`; dữ liệu nghiệp vụ hiện có không bị thay đổi.

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

## 10. Multi-source RAG Data Crawler & Ingestion Worker

Hệ thống RAG Pipeline bao gồm 2 thành phần: Python Scraper (cào & trích xuất dữ liệu) và .NET 10 Ingestion Worker (nạp dữ liệu vào PostgreSQL & Qdrant).

### 10.1. Cài đặt Python Data Scraper

Thư mục: `tools/rag-data-scraper`

Yêu cầu: Python 3.11+.

```powershell
Set-Location tools/rag-data-scraper
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e .
```

### 10.2. Chạy Tool Cào dữ liệu (Crawler CLI)

Tạo dữ liệu Staging JSONL từ các cổng thông tin pháp luật:

#### Cách 1: Giao diện Trang Web 1-Click (Khuyên dùng)

Chạy 1 câu lệnh để mở giao diện quản trị cào dữ liệu trên trình duyệt:

```powershell
python -m rag_data_scraper.cli web --open
```
*(Hoặc nhấp đúp file `tools/rag-data-scraper/run_web.bat`)*

Trình duyệt sẽ tự động mở trang web tại `http://localhost:8000`. Dashboard
chỉ bind loopback và chỉ nhận URL HTTPS thuộc đúng phạm vi của adapter đã chọn.

#### Cách 2: Chạy dòng lệnh CLI (Nâng cao)

1. **Khởi tạo cơ sở dữ liệu SQLite theo dõi trạng thái cào (Chỉ cần chạy 1 lần đầu)**:
   ```powershell
   python -m rag_data_scraper.cli init-db
   ```

2. **Cào dữ liệu từ Cổng thông tin Chính phủ (`vanban.chinhphu.vn`)**:
   ```powershell
   python -m rag_data_scraper.cli crawl --source gov_portal --job-id JOB_GOV_01 --urls https://vanban.chinhphu.vn
   ```

3. **Cào dữ liệu từ Thư Viện Pháp Luật (`thuvienphapluat.vn`)**:
   ```powershell
   python -m rag_data_scraper.cli crawl --source legal_aggregator --job-id JOB_LEGAL_01 --urls https://thuvienphapluat.vn
   ```

4. **Xem Báo cáo trực quan dạng Bảng (HTML Preview)**:
   ```powershell
   python -m rag_data_scraper.cli preview --job-id JOB_GOV_01 --open
   ```

Dữ liệu cào & chunking được xuất thành package tự chứa tại
`tools/rag-data-scraper/storage/staging/<job_id>/` (gồm artifact và `preview.html`).

Preview mới là **RAG Inspector** tự chứa: có tab Tổng quan/Văn bản/Chunks/
Vấn đề/Kỹ thuật, phân trang 50 dòng, bộ lọc và drawer chi tiết. RAG Health phát
hiện sai lệch manifest, quan hệ mồ côi, token budget, offset, ACL, extraction,
duplicate và crawler error; đây là cảnh báo review trước ingestion, không thay
thế bước validate của `DxOs.Workers`.

#### Xuất định dạng phục vụ RAG từ Dashboard

Sau khi job hoàn tất, tại cột **Hành động**, chọn định dạng và bấm **Xuất**:

- `chunks_jsonl` (khuyên dùng): một chunk mỗi dòng, giữ nguyên `text`, source,
  hash, offset và ACL để nạp vector pipeline;
- `staging_zip`: package lossless dùng trực tiếp với `DxOs.Workers`;
- dữ liệu có cấu trúc: `chunks_json`, `chunks_csv`, `chunks_xlsx` và
  `documents_xml`;
- tài liệu: `documents_html`, `documents_pdf`, `documents_docx`,
  `documents_txt_zip`, `documents_markdown_zip`, `documents_pptx` và
  `documents_svg_zip`.

API tải xuống ổn định:

```text
GET http://127.0.0.1:8000/api/jobs/<job_id>/exports
GET http://127.0.0.1:8000/api/jobs/<job_id>/exports/<format>
```

Server chỉ export job đã hoàn tất và kiểm tra lại manifest, SHA-256, quan hệ
observation/chunk-set/chunk, offset, content hash và ACL. Package không hợp lệ
trả `409`; package hoặc output vượt giới hạn 1 GiB trả `413`; writer runtime bị
thiếu trả `503` mà không lộ chi tiết dependency. Xem bảng contract chi tiết
trong `tools/rag-data-scraper/SETUP.md`.


### 10.3. Nạp dữ liệu vào PostgreSQL & Qdrant (Ingestion CLI Worker)

Thư mục gốc: `DxOs.Workers`

1. **Áp dụng EF migration RAG trên PostgreSQL**:
   ```powershell
   dotnet ef database update `
     --project DigitalOps.API/DigitalOps.API.csproj `
     --startup-project DigitalOps.API/DigitalOps.API.csproj
   ```

   EF migration là nguồn schema chính thức; không chạy thêm
   `tools/rag-data-scraper/sql/001_init_rag_schema.sql` trên cùng database.

2. **Cấu hình worker ngoài source control**:
   ```powershell
   $env:ConnectionStrings__DigitalOps = "<PostgreSQL connection string>"
   $env:Ai__Ollama__BaseUrl = "http://127.0.0.1:11434"
   $env:Ai__Qdrant__ApiKey = "<same key configured in Qdrant>"
   $env:Rag__QdrantGrpcHost = "127.0.0.1"
   $env:Rag__QdrantGrpcPort = "6334"
   ```

3. **Kiểm tra toàn vẹn package, không gọi mạng/không ghi DB**:
   ```powershell
   dotnet run --project DxOs.Workers -- --staging-dir tools/rag-data-scraper/storage/staging/<job_id> --validate-only
   ```

4. **Mô phỏng deterministic ID, không gọi mạng/không ghi DB**:
   ```powershell
   dotnet run --project DxOs.Workers -- --staging-dir tools/rag-data-scraper/storage/staging/<job_id> --dry-run
   ```

5. **Nạp dữ liệu vào PostgreSQL và Qdrant**:
   ```powershell
   dotnet run --project DxOs.Workers -- --staging-dir tools/rag-data-scraper/storage/staging/<job_id>
   ```

   Nếu lần chạy trước bị gián đoạn, chạy lại với `--resume`. Worker kiểm tra hash,
   offset, ACL, kích thước embedding 1024 và cấu hình collection trước khi kích
   hoạt version/chunk-set mới:
   ```powershell
   dotnet run --project DxOs.Workers -- --staging-dir tools/rag-data-scraper/storage/staging/<job_id> --resume
   ```

## 11. Kiểm tra hệ thống

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

### Live smoke AI review (opt-in)

Smoke này dùng provider Development, Ollama embedding, Qdrant và PostgreSQL thực.
Nó tạo dữ liệu synthetic, xác nhận nhánh deterministic `Rule/Failed` và nhánh
`Hybrid/Passed`, sau đó xóa đúng outgoing, review history, template/type và FormatRule
point đã tạo. Chưa bao giờ chạy trong suite mặc định:

```powershell
$env:DIGITALOPS_RUN_AI_REVIEW_SMOKE = "1"
dotnet test DigitalOps.API.Tests/DigitalOps.API.Tests.csproj --no-restore --filter "Category=LiveAiReview"
Remove-Item Env:DIGITALOPS_RUN_AI_REVIEW_SMOKE
```

Chỉ chạy khi connection string Development, Qdrant API key và provider đã được cấu hình;
không ghi prompt thô, API key hay dữ liệu cảm vào evidence.

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
