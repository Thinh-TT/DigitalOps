# Session Log - 2026-08-02 AI Development Runtime Setup

- **Ngày**: 2026-08-02
- **Người thực hiện**: Codex
- **Loại**: Development environment and compatibility implementation
- **Trạng thái**: Hoàn thành
- **Log state**: `Closed`

## Bối cảnh

Máy development dùng DeepSeek V4 Flash cho chat generation. Ollama và Qdrant
được bổ sung để giữ embedding/RAG theo baseline local; T2-04 được smoke với
database development hiện tại.

## Quyết định

- Thêm `Ai:External:StructuredOutputMode` (`JsonSchema`/`JsonObject`). DeepSeek
  dùng `JsonObject`, đưa JSON Schema vào system instruction và giữ validate
  schema/guardrail ở server.
- Thêm `Ai:External:DisableThinking`; profile DeepSeek đặt `true` để nhận
  `message.content` JSON thay vì response chỉ có reasoning content.
- Cấu hình `.env` local dùng `External`, `JsonObject`, DeepSeek và Qdrant
  collection `digitalops_knowledge_v1`; Qdrant API key được tạo ngẫu nhiên và
  không ghi vào source/log.
- Qdrant chạy `qdrant/qdrant` digest baseline, named volume
  `digitalops-qdrant-storage`, restart `unless-stopped`, chỉ bind
  `127.0.0.1:6333`, bật API key và tắt telemetry.
- Ollama portable native đã có trên máy, được chạy tại `127.0.0.1:11434`.
  Model `qwen3-embedding:0.6b` đúng digest baseline và trả 1024 chiều.

## Kiểm tra đã thực hiện

- Qdrant không có API key trả `401`; có key trả `200`.
- Ollama `/api/embed` trả một vector 1024 chiều; model digest đúng baseline.
- `AiProviderTests` — 14/14 pass, bao gồm JsonSchema cũ và DeepSeek JsonObject
  + disabled thinking.
- `dotnet test DigitalOps.slnx --no-restore` — 133/133 pass.
- `dotnet format DigitalOps.slnx --no-restore --verify-no-changes` — pass.
- `dotnet ef migrations has-pending-model-changes` — không có model change.
- `git diff --check` — pass.
- EF database update — database development đã up-to-date, không có migration mới.
- Full T2-04 smoke:
  - tạo Clerk/Staff/incoming synthetic với prefix `AI-SMOKE`;
  - DeepSeek trả suggestion cho Staff synthetic;
  - Clerk xác nhận, document chuyển `New -> InProgress`;
  - suggestion metadata được giữ nguyên;
  - Staff synthetic được deactivate và vector synthetic bị xóa;
- incoming document `AI-SMOKE-20260802105957` được giữ lại để kiểm tra UI.

## Runtime bàn giao

- Container `digitalops-qdrant` đang chạy, dùng image digest baseline, restart
  `unless-stopped` và chỉ publish `127.0.0.1:6333`.
- Collection `digitalops_knowledge_v1` đã được tạo với cosine/1024 và còn 2
  Staff active thật sau khi xóa chính xác vector synthetic.

## Theo dõi tiếp

- Qdrant collection được tạo lazy trong smoke và giữ các Staff active thật đã
  được index. Qdrant là derived index, PostgreSQL vẫn là source of truth.
- Production TLS/secret store/HA Qdrant không nằm trong development setup này.
