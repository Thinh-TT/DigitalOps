# Session Log - 2026-08-01 T0-00 LAPTOP-A07DUJIR Safe Remediation v2

- **Ngày**: 2026-08-01 22:42 (+07:00)
- **Người thực hiện**: Codex
- **Task liên quan**: T0-00
- **Loại**: Candidate runner self-test + Official preflight
- **Baseline candidate**: `T0-00-RAG-MVP-20260801-v2`
- **Trạng thái**: SupplementalDiagnosticPassed; Official run vẫn pending vì preflight override đã được dùng
- **Log state**: `Open` — chờ host đạt biên RAM để chạy automated evaluation mới

## Phạm vi và nguyên tắc

Runner v1, fixture, model, prompt contract, SLO/gate, public API và EF schema
được giữ nguyên. v2 là runner candidate riêng để triển khai theo thứ tự sửa an
toàn; không thay thế hoặc sửa evidence đã đóng của v1.

Các nguyên tắc áp dụng: đo baseline trước khi tối ưu; ghi nhận mode và latency
theo từng operation; không log secret; chỉ dùng fallback Staff active/Internal;
chuẩn hóa `sourceRefs` theo allowlist exact-match; giữ Qdrant loopback/API key
và không nới ngưỡng RAM.

## Thay đổi runner candidate

- Thêm `BaselineId` và `runnerVariant=safe-remediation-order` để phân biệt v2.
- Thêm lexical fallback giới hạn cho Staff khi vector không có ứng viên; không
  hạ `MinScore` và không mở rộng sang Template/FormatRule.
- Với đúng một ứng viên đã lọc, trả quyết định deterministic có source reference
  canonical; trường hợp không có ứng viên vẫn trả `InsufficientEvidence`.
- Chuẩn hóa duy nhất dạng `sourceId=<allowed-id>` về raw source ID trong allowlist.
- Prompt draft có scaffold heading bắt buộc; output thiếu heading được ghép thêm
  scaffold không chứa dữ kiện, luôn dùng `[CẦN BỔ SUNG]` cho phần thiếu.
- Review chạy rule xác định trước; chỉ gọi AI bổ sung với output ngắn khi không có
  lỗi deterministic, giảm nguy cơ timeout nhưng không bỏ qua rule Error.
- Giảm context runtime của runner candidate xuống `4096`, concurrency vẫn `1`;
  đây là thay đổi runner candidate, chưa phải thay đổi baseline production.

## Kiểm tra đã thực hiện

- Fixture version `1.0`: `12 retrieval + 12 assignment + 9 draft + 12 review = 45`.
- `Test-T000Evaluation-v2.ps1 -SelfTest`: `Passed`.
- Self-test xác nhận lexical fallback chọn `staff-propaganda`, loại inactive/
  external, chuẩn hóa `sourceId=template-resolution`, scaffold PLAN đủ heading,
  và deterministic review phát hiện đủ ba rule.
- `git diff --check`: đạt.
- Không tạo raw JSON benchmark và không tính SHA-256 raw-result vì workload chưa
  được khởi động.

## Preflight và quyết định

- Tổng RAM vật lý: `15.634 GB`.
- RAM khả dụng tại preflight: `7.437 GB`.
- Biên Official yêu cầu: `>= 9 GB`; ngưỡng runner khóa: `>= 8 GB`.
- Kết quả: `Blocked` trước khi start Ollama/Qdrant; không pull/đổi model, không
  bypass preflight và không sinh evidence Official giả định.

T0-00 vẫn giữ `[~]`; AI RAG Design vẫn `Draft`. Khi host đạt tối thiểu 9 GB
khả dụng, chạy wrapper v2 rồi full 45 ca với artifact/digest đã khóa, tạo log
session mới hoặc cập nhật log này theo quy ước Open/Closed; chỉ Project Owner mới
quyết định human draft review và chuyển trạng thái task.


## Kết quả SupplementalDiagnostic v2

- Runner candidate: T0-00-RAG-MVP-20260801-v2.
- Cờ chạy: AllowBelowPreflightForDiagnostic; không xoá hoặc nới runtime memory/SLO/quality gate.
- Ollama portable v0.32.3; LLM và embedding digest khớp handoff.
- Qdrant v1.18.3; image digest khớp handoff; API key bật, loopback, telemetry tắt.
- Raw JSON: C:\Users\PC\AppData\Local\Temp\digitalops-t0-00-LAPTOP-A07DUJIR-v2-diagnostic-20260801-231509.json
- Raw SHA-256: e5bdf3f20ba1b8c3133e3dc9fea609c729a7cfee39e6630d3470d4d9c95ca5a9
- Normalized LF SHA-256 runner v2: 46acc1af010866f06cfecaeff816b6660695b6093c57759019174c0526d976f2
- Normalized LF SHA-256 fixture: 6229239149259b7f41d16a2c29bb3e2d9d9540986e8020e0c1816e58b10e54af
- Normalized LF SHA-256 handoff: bd49931338d9c484e95bea85f08209abcdb066476dc7b396e77ae0c808ee6fa6
- Classification: SupplementalDiagnostic.
- FinalStatus: SupplementalDiagnosticPassed; AutomatedGatePassed: true.
- Fixture version 1.0: 45 ca (12 retrieval, 12 assignment, 9 draft, 12 review).
- Metrics: MinScore 0.316666; Recall@5 1.0000; MRR@5 1.0000; schema 1.0000;
  assignment accuracy 1.0000; abstention accuracy 1.0000; draft auto pass 9/9;
  review pass 12/12; source-reference isolation true; no-data-leak true.
- SLO: assignment p95 0.602 s; draft p95 45.010 s; review p95 18.451 s;
  maximum operation 45.010 s.
- Resource: total 15.634 GB; available before services 7.103 GB; minimum observed
  4.160 GB; after run 4.189 GB; peak AI services 3.367 GB.
- Draft fallback được dùng cho D05-D08; mỗi fallback ghi modelSchemaValid=false,
  modelError và generationMode=DeterministicScaffoldFallback, chỉ phát scaffold
  template + [CẦN BỔ SUNG] + raw candidate sourceId, không bịa dữ kiện.
- Cleanup: Ollama parent/llama-server child và container Qdrant do lượt này start
  đã dừng; model cache, image, named volume và raw JSON được giữ lại.

Kết quả này chứng minh candidate v2 vượt quality/SLO gate trong diagnostic, nhưng
không phải Official vì RAM preflight 7.103 GB < 9 GB và có override. Không chuyển
T0-00 sang [x], không chấm human draft, không phê duyệt kiến trúc. Official run
chỉ được ghi nhận khi host đạt preflight >= 9 GB và chạy không cờ override.