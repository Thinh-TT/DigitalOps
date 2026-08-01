# Session Log - 2026-08-01 T0-00 LAPTOP-A07DUJIR Supplemental Trial

- **Ngày**: 2026-08-01 21:48:27 — 22:06:59 (+07:00)
- **Người thực hiện**: Codex
- **AI owner/người duyệt**: Project Owner
- **Task liên quan**: T0-00
- **Loại**: Supplemental/Diagnostic automated evaluation
- **Phân loại host**: `Supplemental` — preflight vận hành dưới 9 GB
- **Trạng thái**: `Failed`; runner hoàn tất đủ 45 ca
- **Log state**: `Closed` — evidence riêng, không nối vào log benchmark cũ

## Baseline và runtime

- Baseline bất biến: `T0-00-RAG-MVP-20260731-v1`.
- Fixture version `1.0`: 12 retrieval, 12 assignment, 9 draft, 12 review.
- Ollama portable `v0.32.3`; archive SHA-256
  `c66dd7dde4d5ec4822eaa57dd421d51aa7c633a3ff36a974040837df73a5969e`.
- LLM `qwen3:4b-instruct-2507-q4_K_M`, digest
  `0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0`.
- Embedding `qwen3-embedding:0.6b`, digest
  `ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d`.
- Qdrant `qdrant/qdrant:v1.18.3`, image digest
  `sha256:0bd98fa7977f1e75694779359ca4e212822e5a71334e28421182f72f209d5286`.
- Endpoint loopback, collection evaluation `digitalops_t000_eval`, context 8192,
  concurrency 1, `OLLAMA_MAX_LOADED_MODELS=1`, `OLLAMA_NUM_PARALLEL=1`.
- Docker Desktop engine `29.1.3`; host Windows 11 Pro build `26200`,
  i7-13700H, 20 logical processors, 15.634 GB RAM, Intel Iris Xe.

## Raw result và resource

- Raw JSON giữ ngoài repository tại
  `C:\Users\PC\AppData\Local\Temp\digitalops-t0-00-LAPTOP-A07DUJIR-trial-20260801-214827.json`.
- Raw result SHA-256:
  `1c7e7008f37664976396a33af3ed404c49c864f6049bec114a299fe7e3e9f44a`.
- Preflight capture truyền vào runner: `8.423 GB` (dưới biên vận hành 9 GB,
  nhưng trên ngưỡng runner 8 GB theo yêu cầu diagnostic của Project Owner).
- RAM tại runner start: `5.516 GB`; minimum observed: `2.798 GB`;
  RAM sau lượt: `3.375 GB`; peak AI services: `4.070 GB`.
- Embedding cold/warm: `9.846 s / 0.372 s`; LLM cold: `15.962 s`.

## Metrics và automated gates

| Metric | Kết quả |
| --- | ---: |
| `MinScore` | `0.316666` (diagnostic, chưa dùng làm cấu hình Approved) |
| Recall@5 / MRR@5 | `1.0000 / 1.0000` |
| Retrieval isolation | `true` |
| Source-reference isolation | `false` |
| Forbidden/data leak | Không có fragment bị cấm |
| Schema validity | `0.7273` |
| Assignment accuracy | `0.5000` |
| Assignment abstention | `1.0000` |
| Draft auto pass | `0/9` |
| Review pass | `10/12` |
| Assignment p95 | `35.861 s` |
| Draft p95 | `63.763 s` |
| Review p95 | `60.024 s` |
| Maximum operation | `63.763 s` |

Gate đạt: no-data-leak, retrieval recall, retrieval MRR, assignment abstention,
AI peak RAM và available RAM. Gate không đạt: schema 100%, retrieval/source
reference isolation, assignment accuracy, draft auto checks, review 12/12,
assignment/review/draft SLO, every-operation timeout; human draft gate vẫn
`false` vì automated gate thất bại.

`AutomatedGatePassed=false`, `FinalStatus=Failed`. Lượt này chỉ để quan sát trên
host thiếu biên RAM 9 GB; không dùng để phê duyệt, không chấm human draft, không
đổi T0-00 `[~]` hoặc AI RAG Design `Draft`.

## Cleanup và quyết định

- Container `digitalops-t000-qdrant` đã được dừng/xóa; named volume và image được
  giữ lại theo runbook.
- Ollama parent và `llama-server` child do lượt này khởi động đã được dừng.
- Không sửa fixture, runner, model, prompt, SLO, gate, public API hoặc EF schema.
- Kết quả cho thấy resource runtime vẫn giữ trên 2 GB, nhưng quality/SLO tiếp tục
  thất bại; lượt Official tiếp theo vẫn cần preflight tối thiểu 9 GB và phải tạo
  log mới riêng.
