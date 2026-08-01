# Session Log - 2026-08-02 T0-00 Documentation Normalization and Dev Providers

- **Ngày**: 2026-08-02
- **Người thực hiện**: Codex
- **Task liên quan**: T0-00 follow-up / development enablement
- **Loại**: Documentation normalization, provider abstraction và test
- **Trạng thái**: Completed
- **Log state**: `Closed`

## Trạng thái baseline

- Giữ `T0-00-RAG-MVP-20260801-v3-no-ram-preflight` là baseline official cho
  MVP/demo; T0-00 tiếp tục `[x]`.
- Đồng bộ Ideas and Scope, API Specification, Functional Requirements, README,
  AGENT, AI evaluation README và handoff để không còn mô tả active là
  `Draft/chờ evaluation`.
- Ghi rõ `MinScore=0.316666` là official; `0.320682` chỉ là provisional v1.
- Ghi rõ runner v3 dùng context/output rút gọn và deterministic safeguard để
  evaluation; production contract vẫn là context 8192 và budget
  assignment/review/draft 256/768/1024.
- Không sửa các session log đã `Closed`. Raw result v3 không nằm trong repository;
  approval và SHA raw tiếp tục được tham chiếu theo log v3 của Project Owner.

## Hai provider Development

- Thêm `IAiChatClient` với `OllamaAiChatClient` và OpenAI-compatible
  `ExternalAiChatClient`; chọn một provider khi startup qua `Ai__Provider`.
- Thêm `IEmbeddingClient` chỉ dùng `OllamaEmbeddingClient`; External không thay
  embedding model/dimension hoặc Qdrant retrieval.
- Options validate-on-start khóa context/output budget, timeout tối đa 60 giây,
  cấm automatic fallback, cấm External ngoài Development và yêu cầu External
  strict structured output/API key.
- Hai client dùng typed `HttpClient`, cùng internal JSON Schema contract và không
  log raw prompt/completion/API key. Startup chỉ log provider/model và embedding.
- Thêm `.env.ollama.example` và `.env.external.example`; `.env` thật vẫn bị
  ignore. Không thêm endpoint, DTO public, EF entity hoặc migration.

## Kiểm tra

- `dotnet build DigitalOps.slnx --no-restore`: đạt, 0 warning/0 error.
- `dotnet test DigitalOps.API.Tests/DigitalOps.API.Tests.csproj --no-build`:
  121/121 đạt.
- Test mới phủ options validation, Development-only External, missing secret/
  structured output, cấm fallback/contract/model-digest drift, DI provider
  selection, Ollama payload, OpenAI-compatible payload/Bearer header, embedding
  local và từ chối vector sai dimension.
- JSON appsettings/fixture, 40 file Markdown local links, env profile sanity,
  secret-pattern scan và `git diff --check`: đạt.
- T0-00 v3 runner self-test: đạt (8/8 checks); không khởi động lại workload
  45 ca vì raw v3 evidence đã được Project Owner phê duyệt và không nằm trong
  repository của máy này.
- Không gọi External provider thật vì chưa khóa vendor/model/base URL/API key;
  HTTP contract được kiểm tra bằng fake handler, không dùng network hoặc dữ liệu
  thật.

## Quyết định vận hành

- Máy AI/demo/report dùng `.env.ollama.example` làm mẫu và Ollama là official.
- Máy yếu dùng `.env.external.example`, chỉ trong Development và chỉ với dữ liệu
  synthetic/redacted.
- Lỗi provider không chuyển sang provider còn lại. External result nếu benchmark
  phải ghi `Supplemental-External`, không thay thế evidence Ollama v3.
