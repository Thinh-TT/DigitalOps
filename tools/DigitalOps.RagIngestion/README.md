# DigitalOps.RagIngestion

One-shot .NET CLI để kiểm tra và publish staging package từ
`tools/rag-data-scraper`. Đây là tool chạy ngoài process DigitalOps API, không
phải background worker và không mở HTTP endpoint.

## Interface

Chạy từ thư mục gốc repository:

```powershell
dotnet run --project tools/DigitalOps.RagIngestion -- validate --staging-dir <path>
dotnet run --project tools/DigitalOps.RagIngestion -- plan --staging-dir <path>
dotnet run --project tools/DigitalOps.RagIngestion -- admit --staging-dir <path> --source-registry tools/rag-data-scraper/config/source-registry.json --approved-by <name> --approval-reference <id>
dotnet run --project tools/DigitalOps.RagIngestion -- publish --staging-dir <path> --source-registry tools/rag-data-scraper/config/source-registry.json [--resume]
```

| Command | Network/ghi dữ liệu | Kết quả |
| --- | --- | --- |
| `validate` | Không | Kiểm tra manifest, artifact, hash, quan hệ, offset và ACL |
| `plan` | Không | Chạy validation và tính deterministic point ID |
| `admit` | Ghi local `admission.json` | Đối chiếu registry/provenance/legal metadata, duyệt observation hợp lệ và quarantine phần còn lại |
| `publish` | Có | Kiểm tra receipt/digest lại, chỉ ghi observation đã duyệt vào derived ingestion catalog và Qdrant |

Flag cũ `--validate-only`, `--dry-run` và cách gọi không có command được giữ tạm
thời như compatibility alias. Caller mới phải dùng command ở trên và dựa vào
exit code, không parse câu log.

Để gọi như executable độc lập:

```powershell
dotnet publish tools/DigitalOps.RagIngestion -c Release -o <output-dir>
<output-dir>\DigitalOps.RagIngestion.exe validate --staging-dir <path>
```

## Exit code

| Code | Ý nghĩa |
| --- | --- |
| `0` | Thành công hoặc hiển thị help/version |
| `1` | Command/argument không hợp lệ |
| `2` | Staging package không hợp lệ |
| `3` | Thiếu hoặc sai cấu hình publish |
| `4` | Publish thất bại sau khi khởi tạo pipeline |
| `5` | Source registry/admission không hợp lệ hoặc package không có observation được duyệt |

## Contract staging và admission

- Package mới dùng `schema_version = 1.0`, `corpus_type = legal_reference` và
  ghi `source_registry_version` trong manifest. Package legacy vẫn có thể
  `validate`/`plan`, nhưng không được `admit` hoặc `publish`.
- Mỗi observation legal phải có `source_provenance`, `source_version`, ngôn ngữ,
  chất lượng extraction và metadata tối thiểu: số hiệu, loại, cơ quan ban hành,
  ngày ban hành, trạng thái hiệu lực.
- Registry chỉ cho phép `official/authoritative` hoặc
  `verified_copy/verified_copy` đi vào publish. Nguồn `aggregator` với policy
  `cross_check_only` được ghi quarantine, không được dùng một mình làm corpus.
- `admission.json` chứa người duyệt, approval reference, registry version, danh
  sách observation được duyệt/quarantine và SHA-256 digest của package. Mọi thay
  đổi core staging file sau admission làm `publish` thất bại; phải validate và
  admit lại.
- `publish` đánh giá eligibility lại bằng registry hiện tại rồi chỉ chọn đúng
  observation/chunk đã duyệt. Receipt không phải cách bỏ qua validation.

## Cấu hình cho `publish`

- `ConnectionStrings__DigitalOps`
- `Ai__Ollama__BaseUrl` — mặc định `http://127.0.0.1:11434`, chỉ loopback
- `Ai__Qdrant__ApiKey`
- `Rag__QdrantGrpcHost` — mặc định `127.0.0.1`, chỉ loopback
- `Rag__QdrantGrpcPort` — mặc định `6334`

`validate`, `plan` và `admit` không cần các biến kết nối trên. `admit` và
`publish` bắt buộc nhận `--source-registry`.

## Ranh giới kiến trúc

- Input duy nhất là staging package; CLI không crawl URL.
- Scraper không tham chiếu assembly hoặc class nội bộ của CLI.
- DigitalOps API không spawn CLI trong request path.
- `publish` cưỡng chế source-admission theo
  `Project-Document/02-architecture/03-ai-rag-design.md`; crawler không thể tự
  ghi vào PostgreSQL/Qdrant.
- T4-03 vẫn chưa hoàn tất cho đến khi baseline 45 ca regression cộng legal
  fixture đạt gate citation/safety và được duyệt. Admission hoạt động không đồng
  nghĩa corpus đã production-ready.
