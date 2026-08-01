# T0-00 Handoff — Setup và chạy evaluation trên thiết bị khác

> **Mục đích:** bàn giao việc dựng môi trường và chạy lại evaluation T0-00 cho
> thành viên/thiết bị khác. Đây là runbook thực thi, không phải phê duyệt mới.

## 1. Quyền quyết định và trạng thái hiện tại

- Baseline lịch sử của runbook: `T0-00-RAG-MVP-20260731-v1`; official hiện tại là
  `T0-00-RAG-MVP-20260801-v3-no-ram-preflight`.
- Owner/người duyệt: **Project Owner**.
- T0-00 đã `[x]`; AI RAG Design đã `Approved for MVP/demo` theo log v3. Runbook
  này giữ lại để truy nguyên các lượt v1/v2; không dùng các ngưỡng cũ làm official.
- Log máy trước đã đóng và không được sửa:
  [`log-20260731-t0-00.md`](../session-log/log-20260731-t0-00.md).
- Chỉ được mở khóa T2-04, T3-02, T3-03 và đánh dấu T0-00 `[x]` sau khi mọi gate
  tự động và human gate đạt.

Người thực hiện được phép cài đặt dependency, pull đúng artifact, start/stop
runtime, chạy **nguyên vẹn** fixture và runner, rồi ghi evidence. Không tự đổi
model/digest, embedding/dimension, Qdrant, nguồn index, prompt/output contract,
SLO, gate, fixture hoặc public API. Nếu cần sửa runner để sửa bug, phải giữ
fixture version `1.0`, ghi change proposal và xin Project Owner trước khi chạy
official evaluation.

## 2. Artifact và profile bắt buộc

| Artifact/hạng mục | Giá trị phải khớp |
| --- | --- |
| Fixture | `Project-Document/06-logs/ai-evaluation/t0-00-cases.json`, version `1.0`, đúng 45 ca |
| Runner | `tools/ai-rag-eval/Invoke-T000Evaluation.ps1` |
| LLM | `qwen3:4b-instruct-2507-q4_K_M`, digest `0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0` |
| Embedding | `qwen3-embedding:0.6b`, digest `ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d`, 1024 chiều |
| Qdrant | `qdrant/qdrant:v1.18.3`, image digest `sha256:0bd98fa7977f1e75694779359ca4e212822e5a71334e28421182f72f209d5286` |
| Collection evaluation | `digitalops_t000_eval`, runner tự xóa/tạo lại collection này |
| Collection production | `digitalops_knowledge_v1` — **không tạo hoặc ghi dữ liệu production trong T0-00** |
| Ollama endpoint | `http://127.0.0.1:11434` |
| Qdrant endpoint | `http://127.0.0.1:6333`, chỉ loopback, API key bắt buộc |
| Runtime | Một AI request đồng thời; `OLLAMA_MAX_LOADED_MODELS=1`, `OLLAMA_NUM_PARALLEL=1`, context 8192 |

Profile dùng cho baseline v3 là Windows 16 GB CPU-first. V3 không còn preflight
9 GB; chỉ giữ gate runtime tối thiểu 2 GB khả dụng và peak AI services tối đa
10 GB. Profile khác phải ghi `Supplemental`, không ghép metric.
Thiết bị khác profile phải ghi `Supplemental` và không tự dùng kết quả đó để
đánh dấu `[x]`; không ghép metric giữa các thiết bị. Một lượt official phải
chạy đủ 45 ca trên cùng một host và cùng runtime.

## 3. Đọc trước khi chạy

1. [`03-ai-rag-design.md`](../../02-architecture/03-ai-rag-design.md), đặc biệt mục 6 và 8.
2. [`t0-00-cases.json`](t0-00-cases.json) — không sửa fixture.
3. [`README.md`](README.md) của thư mục evaluation.
4. Log đã đóng và task board để hiểu blocker trước đó.

## 4. Chuẩn bị secret và Qdrant

Mở PowerShell tại root repository. API key chỉ tồn tại trong process environment;
không ghi vào file, command history chia sẻ, log hoặc output JSON.

```powershell
Set-Location E:\DigitalOps
$env:QDRANT_API_KEY = '<local-secret-do-not-commit>'

docker version
docker pull qdrant/qdrant:v1.18.3
$repoDigests = @(docker image inspect qdrant/qdrant:v1.18.3 --format '{{json .RepoDigests}}' | ConvertFrom-Json)
if (-not ($repoDigests -contains 'qdrant/qdrant@sha256:0bd98fa7977f1e75694779359ca4e212822e5a71334e28421182f72f209d5286')) {
    throw 'Qdrant image digest mismatch; stop and report, do not substitute another image.'
}

docker volume inspect digitalops-qdrant-storage *> $null
if ($LASTEXITCODE -ne 0) { docker volume create digitalops-qdrant-storage }

docker run --rm -d --name digitalops-t000-qdrant `
  -p 127.0.0.1:6333:6333 `
  -e QDRANT__SERVICE__API_KEY=$env:QDRANT_API_KEY `
  -e QDRANT__TELEMETRY_DISABLED=true `
  -v digitalops-qdrant-storage:/qdrant/storage `
  qdrant/qdrant:v1.18.3

Invoke-RestMethod -Uri http://127.0.0.1:6333/ -Headers @{ 'api-key' = $env:QDRANT_API_KEY }
```

Nếu tên container đã tồn tại, dừng lại để kiểm tra cấu hình; không xóa container
hoặc volume của dự án khác. Named volume chỉ là persistence local của index dẫn
xuất, không phải backup nghiệp vụ.

## 5. Chuẩn bị Ollama và kiểm tra digest

Ollama phải bind loopback. Bản đã ghi nhận trong baseline là portable `v0.32.3`;
ghi lại version thực tế trong log. Nếu version hoặc digest không khớp, đánh dấu
`Supplemental/Blocked` và báo Project Owner trước khi benchmark.

```powershell
$ollama = (Get-Command ollama -ErrorAction Stop).Source
& $ollama --version

$env:OLLAMA_HOST = '127.0.0.1:11434'
$env:OLLAMA_MAX_LOADED_MODELS = '1'
$env:OLLAMA_NUM_PARALLEL = '1'
$ollamaProcess = Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden -PassThru

& $ollama pull qwen3:4b-instruct-2507-q4_K_M
& $ollama pull qwen3-embedding:0.6b

$models = (Invoke-RestMethod -Uri http://127.0.0.1:11434/api/tags).models
foreach ($expected in @(
    @{ Name = 'qwen3:4b-instruct-2507-q4_K_M'; Digest = '0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0' },
    @{ Name = 'qwen3-embedding:0.6b'; Digest = 'ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d' }
)) {
    $actual = $models | Where-Object { $_.name -eq $expected.Name } | Select-Object -First 1
    if ($null -eq $actual) {
        throw "Ollama model missing: $($expected.Name); stop and report."
    }
    $actualDigest = ([string]$actual.digest) -replace '^sha256:', ''
    if ($actualDigest -ne $expected.Digest) {
        throw "Ollama digest mismatch for $($expected.Name); stop and report."
    }
}
```

Pull có thể cần network và quyền cài đặt. Không dùng Docker Ollama thay cho
Windows Ollama trong lượt baseline nếu chưa có quyết định mới. Trước khi đo
preflight, bảo đảm không còn một Ollama service cũ dùng cấu hình khác.

## 6. Preflight và chạy đủ 45 ca

Đo RAM khả dụng **trước khi** khởi động workload. Runner yêu cầu tối thiểu 8 GB
ở preflight và luôn dừng nếu RAM khả dụng trong lượt chạy xuống dưới 2 GB.

```powershell
$os = Get-CimInstance Win32_OperatingSystem
$preflightAvailableGb = [math]::Round(($os.FreePhysicalMemory * 1KB) / 1GB, 3)
if ($preflightAvailableGb -lt 8) {
    throw "Preflight failed: $preflightAvailableGb GB available; need at least 8 GB."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultPath = Join-Path $env:TEMP "digitalops-t0-00-$env:COMPUTERNAME-$stamp.json"
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\ai-rag-eval\Invoke-T000Evaluation.ps1 `
  -PreflightAvailableMemoryGb $preflightAvailableGb `
  -OutputPath $resultPath

Get-FileHash -Algorithm SHA256 -LiteralPath $resultPath
```

Không chạy từng nhóm riêng, không đổi `-MinimumPreflightAvailableMemoryGb`,
`-MinimumAvailableMemoryDuringRunGb`, timeout, model, fixture hoặc gate. Giữ raw
JSON ngoài repository cho đến khi Project Owner xác nhận đã ghi đủ summary/hash;
raw output không đưa vào public API và không commit mặc định.

## 7. Phân loại kết quả và việc phải ghi

Session log mới phải có tên `log-yyyyMMdd-t0-00-<device>.md` và ghi tối thiểu:

- người thực hiện, thời gian bắt đầu/kết thúc, hostname và `Official` hay
  `Supplemental`;
- Windows build, CPU/logical processors, RAM vật lý, GPU nếu có;
- Ollama version, Qdrant/Docker version, hai model digest, Qdrant image digest;
- collection evaluation, preflight captured, runner-start/minimum available RAM,
  peak AI services memory;
- lệnh runner (không ghi API key), đường dẫn tạm và SHA-256 raw result;
- toàn bộ metric/gate trong output, `MinScore`, cold/warm và các gate thất bại;
- quyết định của Project Owner và link đến log/issue tiếp theo.

`AutomatedGatePassed = false` nghĩa là chưa được chấm human draft và chưa được
phê duyệt. Nếu tự động đạt, Project Owner phải chấm đủ 9 draft; ít nhất 8/9 bản
đạt 4/5 về tiếng Việt, bám template và không bịa dữ kiện. Chỉ khi automated gate
và human gate cùng đạt mới đổi AI RAG Design sang `Approved for MVP/demo` và
T0-00 sang `[x]`.

## 8. Xử lý lỗi và dọn môi trường

- Sai digest/API key/endpoint: dừng, ghi blocker, không thay artifact.
- Preflight < 8 GB hoặc minimum available < 2 GB: dừng; không nới ngưỡng.
- Quality/SLO fail: giữ `[~]`/`Draft`, ghi metric; không tự đổi model, prompt
  contract hoặc SLO.
- Sau khi ghi hash và summary, chỉ dừng các process/container do người thực hiện
  khởi động. Không xóa model cache hoặc named volume nếu chưa có chỉ đạo.

## 9. Definition of Done cho người nhận handoff

- [ ] Đã xác minh baseline/digest và API key không được ghi vào repo.
- [ ] Qdrant loopback + named volume + auth + telemetry disabled hoạt động.
- [ ] Ollama loopback, một model resident và hai model đúng digest.
- [ ] Runner chạy đủ 45 ca, raw result được hash và giữ ngoài repo.
- [ ] Session log mới đầy đủ metric/gate, phân loại host và link ngược log cũ.
- [ ] Không sửa log `Closed`, không đổi `[~]`/`Draft` nếu chưa đủ gate và human review.
