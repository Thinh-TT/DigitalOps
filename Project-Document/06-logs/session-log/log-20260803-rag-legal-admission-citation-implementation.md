# Session Log - RAG Legal Admission and Citation Implementation

- **Ngày**: 2026-08-03
- **Người thực hiện**: Codex
- **Task liên quan**: T4-03 — Kho tham chiếu pháp luật có quản trị và retrieval hỗ trợ review
- **Loại**: Implementation / Contract Synchronization
- **Trạng thái**: Implementation và automated tests hoàn tất; evaluation/approval gate còn mở

## Phạm vi đã triển khai

1. Python scraper xuất staging contract `schema_version=1.0`, corpus type,
   source registry version/entry, typed provenance, source version và legal
   metadata. Registry mặc định phân biệt nguồn chính thức với nguồn tổng hợp
   `cross_check_only`.
2. `DigitalOps.RagIngestion` có bốn command ổn định
   `validate|plan|admit|publish`. `admit` ghi `admission.json` gắn SHA-256 digest
   package; `publish` đánh giá lại registry/eligibility và chỉ chọn observation
   đã duyệt. Package legacy chỉ được validate/plan.
3. Admission quarantine nguồn chưa đăng ký, trust/policy không publishable,
   URL ngoài allowed host, extraction kém, legal metadata thiếu/sai hoặc
   effectivity mâu thuẫn. Pair hợp lệ để publish là
   `official/authoritative` hoặc `verified_copy/verified_copy`.
4. Migration `20260803150752_AddLegalRagGovernance` bổ sung typed legal,
   effectivity, provenance, admission audit và chunk soft/hard limits vào derived
   RAG catalog. PostgreSQL vẫn không phải source of truth pháp luật; vector vẫn
   nằm ở Qdrant.
5. Retrieval chỉ nhận legal source đã admission thuộc trust/policy hợp lệ, áp
   ACL và mặc định loại văn bản hết hiệu lực/bị bãi bỏ/bị thay thế hoặc ngoài
   khoảng hiệu lực. Chế độ historical phải được gọi tường minh.
6. FR-013 đưa nguồn legal đã admit vào prompt ở vai trò dữ liệu không tin cậy,
   chỉ cho AI tạo `Warning`/`Info`. `ReviewResponse.citations` expose metadata tối
   thiểu; mỗi review lưu immutable citation snapshot để GET lịch sử không chạy
   retrieval lại.
7. Web hiển thị số hiệu/title, cơ quan ban hành, tier nguồn, source version,
   trạng thái/khoảng hiệu lực và link nguồn; metadata hiệu lực chưa xác định có
   cảnh báo kiểm tra bản gốc.

## Contract và tài liệu đã đồng bộ

- `02-architecture/01-database-designer.md`: migrations, columns và invariants.
- `02-architecture/02-api-spec.md`: `ReviewCitationResponse` và semantics snapshot.
- `02-architecture/03-ai-rag-design.md`: admission đã cưỡng chế, retrieval/citation boundary.
- `04-ui/01-ui-sitemap-and-wireframe.md`: citation trong SCR-013/approval history.
- `SETUP.md`, scraper SETUP và ingestion README: luồng vận hành
  `crawl → validate → admit → publish`, registry path và exit code `5`.
- Task T4-03 giữ `[~]`; không sửa các session log đã đóng.

## Kiểm tra đã thực hiện

- `python -m pytest -q`: **64 passed**. Bao gồm Python staging schema 1.0 →
  .NET `validate` → .NET `admit` và kiểm tra receipt.
- `dotnet test tools/DigitalOps.RagIngestion.Tests/...`: **18 passed**. Bao gồm
  official admission, aggregator/mismatched policy quarantine, tamper rejection,
  legacy rejection và metadata/Qdrant mapping.
- `dotnet test DigitalOps.API.Tests/...`: **178 passed**. Bao gồm ACL, source
  trust, effectivity/historical retrieval, review citation validation và
  immutable snapshot history.
- `npm test -- --maxWorkers=4`: **98 passed**.
- `npm run lint`: thành công.
- `npm run build`: thành công.
- `dotnet build DigitalOps.slnx --no-restore`: thành công, 0 warning/error.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: không có
  model change chưa được migration ghi nhận.
- `git diff --check`: thành công; chỉ có cảnh báo line-ending theo cấu hình Git.

## Giới hạn còn lại

- Chưa chạy live `publish` vào PostgreSQL/Qdrant trong lượt này vì đó là mutation
  môi trường và không cần để chứng minh contract/unit integration.
- Chưa chạy baseline mới gồm đủ 45 ca regression cộng legal fixture và chưa có
  Project Owner approval cho corpus mới. Vì vậy T4-03 chưa được chuyển `[x]` và
  không có tuyên bố demo/production-ready cho legal corpus.
- `admission.json` là receipt vận hành local gắn digest package, chưa phải chữ ký
  số/identity attestation. Production cần cơ chế approval identity, secret/key
  governance, freshness monitoring, rollback drill và backup/restore riêng.
