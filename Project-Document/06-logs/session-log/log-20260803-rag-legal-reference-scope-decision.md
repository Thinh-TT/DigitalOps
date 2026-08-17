# Session Log - RAG Legal Reference Scope Decision

- **Ngày**: 2026-08-03
- **Người quyết định**: Project Owner
- **Task liên quan**: T4-03 — Kho tham chiếu pháp luật có quản trị và retrieval hỗ trợ review
- **Loại**: Architecture / Scope Decision
- **Trạng thái**: Accepted ở mức phạm vi và kiến trúc; implementation/evaluation gate đang mở

## Bối cảnh

AI RAG Design được duyệt ngày 2026-08-01 chỉ cho phép index Staff,
DocumentTemplates và FormatRules. Trong khi đó, `tools/rag-data-scraper` đã phát
triển theo hướng crawl, trích xuất, chuẩn hóa và đóng gói văn bản từ nguồn web.
Nếu tiếp tục mở rộng crawler mà không đổi nguồn sự thật, code và kiến trúc sẽ
lệch nhau; nếu coi mọi dữ liệu crawl được là knowledge source, sản phẩm sẽ rơi
vào crawler/RAG tổng quát ngoài phạm vi.

Project Owner xác nhận ý định sản phẩm là mở rộng RAG thành kho tra cứu tham
chiếu văn bản pháp luật/hướng dẫn nghiệp vụ, trước hết để hỗ trợ FR-013 thẩm định
tốt hơn.

## Quyết định

Chấp nhận **kho tham chiếu pháp luật có quản trị nguồn** như một nguồn tri thức
RAG mới với các ranh giới sau:

1. Legal corpus phục vụ retrieval nội bộ cho FR-013. AI chỉ trả `Warning`/`Info`
   kèm citation để cán bộ đối chiếu; FormatRules xác định vẫn là nguồn duy nhất
   tạo `Error` và quyết định Passed/Failed.
2. Pipeline tách thành `crawl -> staging -> validate/admit -> publish/index`.
   Crawler không được ghi thẳng vào corpus đang phục vụ ứng dụng.
3. Nguồn phải nằm trong registry/allowlist. Nguồn chính thức được ưu tiên; nguồn
   tổng hợp chỉ dùng discovery/cross-check và không đứng một mình làm căn cứ.
4. Legal document/chunk phải mang provenance, content hash, source/version,
   cơ quan ban hành, số hiệu/loại, trạng thái và mốc hiệu lực khi xác định được,
   quan hệ sửa đổi/thay thế và URL citation.
5. Thiếu hoặc mâu thuẫn hiệu lực/phiên bản dẫn đến quarantine, gắn
   `statusUnknown` hoặc abstain; không được suy đoán như văn bản hiện hành.
6. Baseline v3 vẫn là evidence hợp lệ cho 45 ca cũ, nhưng không phê duyệt legal
   corpus. Cần baseline ID mới, chạy lại 45 ca regression và legal fixture bổ
   sung trước khi bật trong demo/production.
7. Quyết định này không tạo public legal search/chat/API/UI, EF schema hoặc lời
   tư vấn pháp lý. Nếu cần trải nghiệm tra cứu độc lập hoặc expose citation,
   phải đồng bộ Functional Requirement, API Specification, UI Sitemap, quyền và
   audit contract trước khi triển khai.
8. Tách implementation thành hai module cùng tầng `tools`: Python
   `rag-data-scraper` tạo staging package và .NET `DigitalOps.RagIngestion` xử lý
   `validate|plan|publish`. Staging package là interface giữa hai module.
9. Đổi tên `DxOs.Workers` thành `DigitalOps.RagIngestion` vì executable hiện tại
   là one-shot CLI, không phải background worker. Script/orchestrator ngoài gọi
   CLI qua command/exit code; không thêm HTTP wrapper hoặc project trung gian.

## Phương án đã cân nhắc

### Giữ nguyên RAG chỉ có Staff/Template/FormatRules

- Ưu điểm: không đổi baseline và contract hiện tại.
- Loại: không phản ánh ý định sản phẩm và làm crawler trở thành code ngoài kiến
  trúc, khó giải thích mục đích/Definition of Done.

### Cho crawler tự động index mọi nội dung đã tải được

- Ưu điểm: triển khai nhanh, ít bước vận hành.
- Loại: không kiểm soát nguồn, phiên bản, hiệu lực, quyền sử dụng và knowledge
  poisoning; crawl thành công không chứng minh nội dung đủ tin cậy cho RAG.

### Kho tham chiếu pháp luật có source admission và publish gate

- Ưu điểm: code có đích nghiệp vụ rõ, truy vết được citation, rollback được và
  giữ AI ở vai trò hỗ trợ.
- Nhược điểm chấp nhận: tăng metadata, validation, freshness monitoring,
  evaluation fixture và công việc vận hành nguồn.

## Tác động tài liệu

- `01-project/01-ideas-and-scope.md`: ghi nhận use case có giới hạn và loại trừ
  crawler/tư vấn pháp lý tổng quát.
- `03-functional/01-functional-requirements.md`: mở rộng FR-013 theo hướng tham
  chiếu có citation nhưng không kết luận pháp lý.
- `02-architecture/03-ai-rag-design.md`: cập nhật phạm vi, nguồn tri thức, legal
  metadata, safety/evaluation gate và ngoài phạm vi.
- `02-architecture/01-database-designer.md`: ghi nhận derived ingestion catalog
  đã tồn tại và phân biệt metadata/audit với source of truth pháp luật.
- `05-tasks/01-task-board.md`: thêm T4-03 để không trộn acquisition đã có với
  admission, integration và evaluation còn thiếu.
- `AGENT.md`: bổ sung guardrail cho các lượt triển khai sau.
- `DigitalOps.slnx`, SETUP và test pipeline: dùng tên/path CLI mới; alias flag cũ
  được giữ tạm để không phá script hiện có.

## Bằng chứng hiện có và giới hạn

Crawler đã chứng minh được acquisition/staging cho nguồn danh sách Mặt trận,
phân trang, attachment, legacy DOC và package validation theo
[log-20260803-rag-scraper-list-records-legacy-doc.md](log-20260803-rag-scraper-list-records-legacy-doc.md).
Đây không phải bằng chứng source admission, legal correctness, citation quality,
freshness hoặc integration với FR-013.

## Điều kiện đóng T4-03

- Source registry/allowlist và trust tier được cấu hình, review và audit được.
- Staging schema giữ đủ provenance/legal metadata; duplicate/version/replacement
  và quarantine/rollback được kiểm thử.
- Publish/index là bước riêng, idempotent; crawl thất bại không ảnh hưởng corpus
  đang active.
- API/UI contract được cập nhật trước khi citation hiển thị cho người dùng.
- Baseline mới chạy đủ regression + legal fixture và đạt citation, source
  precedence, time/effectivity, abstention, injection và no-mutation gate.

## Triển khai tách module và đổi tên

- Di chuyển project root `DxOs.Workers` sang
  `tools/DigitalOps.RagIngestion`, đổi assembly/root namespace tương ứng.
- Di chuyển test project sang `tools/DigitalOps.RagIngestion.Tests` và cập nhật
  solution/project reference.
- Chuẩn hóa interface ngoài thành `validate`, `plan`, `publish`; validate argument
  tại CLI seam, giữ alias cũ và output marker cũ để tương thích script.
- Loại package `System.CommandLine` beta không được dùng; CLI parser nhỏ nằm cùng
  project, không tạo thêm library/host/HTTP layer.
- Cập nhật Python end-to-end test để gọi command `validate` qua project path mới.

## Kiểm tra tài liệu

- Kiểm tra liên kết tương đối và nội dung mâu thuẫn bằng `rg`.
- Chạy `git diff --check`; không thay đổi log lịch sử đã đóng.
- `dotnet build DigitalOps.slnx --no-restore`: thành công, 0 warning/error.
- `dotnet test DigitalOps.slnx --no-build --no-restore`: 177 API tests và 12
  ingestion tests pass.
- `python -m pytest -q`: 62 scraper tests pass, gồm Python staging -> .NET CLI
  validation qua tên/command mới.
- `dotnet publish tools/DigitalOps.RagIngestion -c Release`: tạo executable độc
  lập; chạy `DigitalOps.RagIngestion.exe --version` trả exit code 0/version
  `1.0.0.0`.
