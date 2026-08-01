# Session Log - 2026-08-01 T0-00 LAPTOP-A07DUJIR

- **Ngày**: 2026-08-01 20:46:20 — 21:35:30 (+07:00)
- **Người thực hiện**: Codex
- **AI owner/người duyệt**: Project Owner
- **Task liên quan**: T0-00
- **Loại**: Official preflight và artifact preparation
- **Phân loại host**: `Official` — Windows 16 GB CPU-first
- **Trạng thái**: `Blocked` trước workload; automated evaluation chưa chạy
- **Log state**: `Closed` — không có metric benchmark để nối vào log cũ

## Baseline và evidence

- Baseline bất biến: `T0-00-RAG-MVP-20260731-v1`.
- Fixture version `1.0`, đủ 45 ca và 45 ID duy nhất: 12 retrieval, 12 assignment,
  9 draft, 12 review.
- PowerShell parser của runner đạt.
- Hash SHA-256 chuẩn hóa LF của fixture, runner và handoff khớp baseline:

  | File | SHA-256 (UTF-8, LF) |
  | --- | --- |
  | `Project-Document/06-logs/ai-evaluation/t0-00-cases.json` | `ca7687bf307ca674112a2ac4f8a843c1965ac9d0226c945db22b239810970916` |
  | `tools/ai-rag-eval/Invoke-T000Evaluation.ps1` | `a55e5d7b9ac5956e32452598702d39952ddba6da59417947c264c7f1d0d37a84` |
  | `Project-Document/06-logs/ai-evaluation/t0-00-handoff.md` | `22ea64914f76a6f4709e06258fa6634564130c3e82df7f3e373c92e9991fbbd5` |

- Hash chuẩn hóa LF của `03-ai-rag-design.md` hiện là
  `d44155e5ec2922f5bfc11be3b9376257ef79b71664b570394432574487f24354`, khác
  hash `fc67c8...` ghi trong handoff. File working tree sạch và nội dung trùng
  file ở commit handoff `58248b5`; không sửa log `Closed` hoặc thay baseline.

## Môi trường

| Hạng mục | Giá trị |
| --- | --- |
| Host | `LAPTOP-A07DUJIR` |
| Windows | Windows 11 Pro, build `26200` |
| CPU | 13th Gen Intel(R) Core(TM) i7-13700H, 20 logical processors |
| RAM vật lý | 15.634 GB |
| RAM khả dụng lúc preflight | 8.499 GB |
| GPU | Intel(R) Iris(R) Xe Graphics |
| Docker CLI | 29.1.3; daemon chưa chạy |
| Ollama | Portable `v0.32.3`, client kiểm tra thành công |
| Ollama archive | SHA-256 `c66dd7dde4d5ec4822eaa57dd421d51aa7c633a3ff36a974040837df73a5969e` |
| Qdrant | Chưa pull/chưa start |

## Kết quả preflight

Artifact Ollama portable đã được tải bằng HTTP Range và xác minh toàn bộ hash,
giải nén ngoài repository tại `C:\tmp\digitalops-t0-00`. Không khởi động Docker,
Qdrant hoặc Ollama; không pull model; không tạo collection evaluation; không tạo
raw result JSON.

Preflight dừng vì RAM khả dụng `8.499 GB` thấp hơn biên vận hành `9 GB` đã khóa
trong kế hoạch/T0-00 blocker trước đó. Giá trị này vẫn trên ngưỡng runner 8 GB,
nhưng không đủ điều kiện để bắt đầu workload theo handoff. Không tự dừng tiến
trình hệ thống/Codex để giải phóng RAM.

## Quyết định và việc tiếp theo

- Giữ T0-00 `[~]` và AI RAG Design `Draft`; chưa có automated gate hoặc human
  draft review.
- Không đổi model/digest, fixture, runner, prompt, SLO, gate, public API hoặc
  EF schema.
- Lượt sau cần giải phóng RAM để preflight đạt tối thiểu 9 GB, xác minh lại
  artifact/runtime rồi chạy nguyên vẹn 45 ca trên cùng host. Lượt đó phải tạo
  session log mới và không nối metric vào log này hoặc log `Closed` trước đó.
