# Task Board

## 1. Mục Tiêu

Bảng theo dõi công việc triển khai dự án **DigitalOps** — Hệ thống điều hành số văn bản và hồ sơ hội viên tích hợp AI cho UBMTTQ cấp phường — từ khởi tạo solution ASP.NET Core Web API và React đến khi MVP chạy end-to-end sẵn sàng demo.

Task board này bám theo:

- `Project-Document/01-project/01-ideas-and-scope.md`
- `Project-Document/02-architecture/01-database-designer.md`
- `Project-Document/02-architecture/02-api-spec.md`
- `Project-Document/02-architecture/03-ai-rag-design.md`
- `Project-Document/03-functional/01-functional-requirements.md`
- `Project-Document/04-ui/01-ui-sitemap-and-wireframe.md`

## 2. Quy Ước Trạng Thái

| Ký hiệu | Trạng thái  | Ý nghĩa                       |
| ------- | ----------- | ----------------------------- |
| `[ ]`   | Todo        | Chưa bắt đầu                  |
| `[~]`   | In Progress | Đang thực hiện                |
| `[x]`   | Done        | Hoàn thành                    |
| `[!]`   | Blocked     | Bị chặn, cần xử lý dependency |

## 3. Quy Ước Ưu Tiên

| Ưu tiên | Ý nghĩa                                |
| ------- | -------------------------------------- |
| `P0`    | Bắt buộc cho MVP chạy được end-to-end  |
| `P1`    | Quan trọng cho trải nghiệm hoàn chỉnh  |
| `P2`    | Có thể làm sau MVP nếu thiếu thời gian |

## 4. Phase 0 — Quyết Định Nền Tảng, Repository Và Môi Trường

| ID    | Task                                                                                                                                      | Use Case                    | Status | Priority | Dependency | Definition of Done                                                                                                                                                                                                                                                                                                                 |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- | ------ | -------- | ---------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T0-00 | Chốt quyết định AI RAG: LLM provider/model, embedding, vector store, nguồn tri thức, guardrail, SLO/cost, evaluation                      | `03-ai-rag-design.md` mục 6 | `[~]`  | P0       | Không      | Người phụ trách AI được chỉ định; mọi mục ở bảng "Các quyết định chờ phê duyệt" có quyết định + lý do + ngày hiệu lực ghi trong session log; tài liệu chuyển trạng thái **Approved**; cập nhật Database Designer/API Specification nếu quyết định có tác động đến schema/contract. **Chốt hạn: trước khi bắt đầu T2-04 và T3-02.** |
| T0-01 | Khởi tạo solution ASP.NET Core Web API, tổ chức theo feature (`Members`, `IncomingDocuments`, `Drafting`, `Review`, `Approval`, `Shared`) | Ideas and Scope — Công nghệ | `[x]`  | P0       | Không      | Solution build thành công; cấu trúc thư mục đúng Ideas and Scope; kết nối PostgreSQL qua EF Core; `Program.cs` đăng ký `AddProblemDetails`, JSON camelCase.                                                                                                                                                                        |
| T0-02 | Migration EF Core baseline: Identity tables, `staff`, `document_types`, `document_templates`, `members`                                   | Database Designer 3.1–3.4   | `[x]`  | P0       | T0-01      | Migration áp dụng thành công lên DB dev; snake_case/uuid/timestamptz đúng quy ước; index chính đã tạo.                                                                                                                                                                                                                             |
| T0-03 | Cấu hình ASP.NET Core Identity + JWT + policy khung (Administrator/Clerk/Drafter/Leader, BusinessAccess, PasswordChangeRequired)          | API Spec 3.1–3.2            | `[x]`  | P0       | T0-02      | Policy đăng ký trong `Program.cs`; JWT chứa `sub`, `staffId`, role, `mustChangePassword`; `UseAuthentication()` trước `UseAuthorization()`.                                                                                                                                                                                        |
| T0-04 | Khởi tạo React + Vite + TS + AntD: App Shell, route guard, gọi `GET /auth/me` sau login                                                   | UI Sitemap 3.1              | `[x]`  | P0       | T0-03      | App shell render; route guard xử lý đúng 401 (về `/login`), 403 (Forbidden), `mustChangePassword` (chỉ cho đổi mật khẩu/đăng xuất).                                                                                                                                                                                                |
| T0-05 | Swagger/OpenAPI + xác thực response `ProblemDetails`/`ValidationProblemDetails` toàn cục                                                  | API Spec 2.2, 4.1           | `[x]`  | P1       | T0-01      | Swagger hiển thị đúng DTO, Bearer security scheme; lỗi trả đúng định dạng.                                                                                                                                                                                                                                                         |

## 5. Phase 1 — Identity, Staff Và Hồ Sơ Hội Viên

| ID    | Task                                                                      | Use Case       | Status | Priority | Dependency | Definition of Done                                                                                                                                                    |
| ----- | ------------------------------------------------------------------------- | -------------- | ------ | -------- | ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T1-01 | Đăng nhập, đổi mật khẩu tạm, đăng xuất (API + UI SCR-001, SCR-002)        | FR-001         | `[x]`  | P0       | T0-04      | Luồng chính và ngoại lệ FR-001 hoạt động đúng; Staff inactive không đăng nhập được; `mustChangePassword` chặn đúng route nghiệp vụ; unit + integration test JWT/role. |
| T1-02 | Quản lý tài khoản, Staff và role (API + UI SCR-003)                       | FR-002         | `[x]`  | P0       | T1-01      | Administrator tạo/sửa/reset mật khẩu/vô hiệu hóa Staff; một Staff có nhiều role; test theo tiêu chí nghiệm thu FR-002.                                                |
| T1-03 | Xem, tìm kiếm, tạo, cập nhật, ngừng hoạt động hội viên (API + UI SCR-004) | FR-003, FR-004 | `[x]`  | P0       | T1-01      | CRUD + tìm kiếm hội viên đúng rule; ngừng hoạt động không xóa cứng, không mất `RelatedMemberId` cũ; test theo tiêu chí nghiệm thu.                                    |
| T1-04 | Import hội viên từ Excel (API + UI SCR-005)                               | FR-005         | `[x]`  | P0       | T1-03      | Transaction all-or-nothing; báo lỗi theo dòng/cột khi 422; template download hoạt động; test rollback khi có dòng lỗi.                                                |

## 6. Phase 2 — Danh Mục Văn Bản Và Văn Bản Đến

| ID    | Task                                                                                          | Use Case | Status | Priority | Dependency   | Definition of Done                                                                                                                                           |
| ----- | --------------------------------------------------------------------------------------------- | -------- | ------ | -------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| T2-01 | Quản lý loại văn bản, mẫu và FormatRules (API + UI SCR-006, SCR-007)                          | FR-006   | `[x]`  | P0       | T1-01        | CRUD type/template; validate `FormatRules` là JSON hợp lệ; chỉ template active dùng khi tạo văn bản đi.                                                      |
| T2-02 | Tiếp nhận và quản lý văn bản đến (API + UI SCR-008, SCR-009)                                  | FR-007   | `[x]`  | P0       | T2-01        | Tạo/sửa incoming đúng trạng thái `New`; validate `ReceivedDate <= Deadline`; khóa sửa dữ liệu hành chính sau `Completed`.                                    |
| T2-03 | Quản lý attachment incoming và trigger trích xuất text (API + UI, phần attachment của SCR-009) | FR-008   | `[x]`  | P0       | T2-02        | Upload/download qua API đúng quyền theo tài liệu cha; `ExtractionStatus` đặt đúng `Pending`/`Unsupported` theo loại file; test `413`/`415`.                  |
| T2-04 | AI gợi ý và xác nhận điều phối (API + UI SCR-009)                                             | FR-009   | `[ ]`  | P0       | T0-00, T2-02 | AI service gọi qua interface/typed client; lỗi/timeout AI không đổi assignment (trả `503`); Văn thư luôn là người xác nhận cuối; test AI lỗi không mutation. |
| T2-05 | Nhắc hạn tự động và đọc thông báo (Reminder Worker + UI SCR-010)                              | FR-010   | `[ ]`  | P0       | T2-02        | Worker idempotent theo unique key `(incoming_document_id, recipient, kind, date)`; chuyển `Overdue` đúng lúc; test chạy lại không tạo reminder trùng.        |

## 7. Phase 3 — Văn Bản Đi: Soạn Thảo, AI, Thẩm Định, Phê Duyệt, Lưu Trữ

| ID    | Task                                                    | Use Case | Status | Priority | Dependency   | Definition of Done                                                                                                                                  |
| ----- | ------------------------------------------------------- | -------- | ------ | -------- | ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| T3-01 | Tạo văn bản đi theo mẫu (API + UI SCR-011, SCR-012)     | FR-011   | `[ ]`  | P0       | T2-01        | Placeholder template thay bằng dữ liệu hội viên/incoming liên quan; liên kết incoming/member là optional; bổ sung attachment outgoing; test template/member inactive bị từ chối. |
| T3-02 | AI sinh nháp và chỉnh sửa (API + UI SCR-012)            | FR-012   | `[ ]`  | P0       | T0-00, T3-01 | `AiDraftContent` chỉ ghi ở lần sinh đầu tiên, không bị ghi đè; lỗi/timeout AI không mất `Content` đã lưu; test theo tiêu chí nghiệm thu FR-012.     |
| T3-03 | Thẩm định thể thức và lịch sử review (API + UI SCR-013) | FR-013   | `[ ]`  | P0       | T0-00, T3-02 | Mỗi lần review tạo đúng một dòng `ReviewHistory`, `AttemptNo` tăng tuần tự trong transaction; `Passed` không chứa issue `severity = Error`.         |
| T3-04 | Phê duyệt hoặc trả lại văn bản (API + UI SCR-014)       | FR-014   | `[ ]`  | P0       | T3-03        | Chỉ role Leader duyệt/trả; `Return` đưa về `Editing` và bắt buộc review lại trước khi trình duyệt lại.                                              |
| T3-05 | Cấp số, phát hành và lưu trữ (API + UI SCR-015)         | FR-015   | `[ ]`  | P0       | T3-04        | `ReferenceNumber` và `IssuedDate` luôn cùng có/cùng không có; `Archived` là trạng thái cuối, khóa `Content` và attachment.                          |

## 8. Phase 4 — Tìm Kiếm Toàn Văn

| ID    | Task                                                     | Use Case       | Status | Priority | Dependency          | Definition of Done                                                                                                                                         |
| ----- | -------------------------------------------------------- | -------------- | ------ | -------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T4-01 | Text Extraction Worker cho PDF có text layer, DOCX, XLSX | FR-008, FR-016 | `[ ]`  | P0       | T2-03               | `ExtractionStatus` chuyển đúng `Pending → Succeeded/Failed/Unsupported`; worker idempotent, có scope/`IDbContextFactory` riêng, không chặn upload khi lỗi. |
| T4-02 | Tìm kiếm toàn văn: GIN index + API + UI (SCR-016)        | FR-016         | `[ ]`  | P0       | T4-01, T2-02, T3-01 | Từ khóa tối thiểu 2 ký tự; `matchSource` đúng nguồn khớp; chỉ attachment `Succeeded` tham gia kết quả; test filter/paging/score.                           |

## 9. Phase 5 — Kiểm Thử Tổng Hợp Và Chuẩn Bị Demo

| ID    | Task                                                                                                                                                    | Use Case   | Status | Priority | Dependency | Definition of Done                                                                                     |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------- | ------ | -------- | ---------- | ------------------------------------------------------------------------------------------------------ |
| T5-01 | End-to-end luồng chính: nhập hội viên → incoming → assignment → reminder → complete; outgoing → AI/review → approval → archive; import rollback; search | Tất cả FR  | `[ ]`  | P0       | Phase 1–4  | Toàn bộ kịch bản E2E ở AGENT.md mục 6 chạy pass thủ công.                                              |
| T5-02 | Rà soát checklist UI (UI Sitemap mục 11)                                                                                                                | UI Sitemap | `[ ]`  | P1       | Phase 1–4  | Checklist 11.1–11.4 đạt tại viewport 1280×720 và 1024px.                                               |
| T5-03 | Chuẩn bị kịch bản demo cuộc thi: dữ liệu mẫu, câu chuyện luồng khép kín                                                                                 | —          | `[ ]`  | P1       | T5-01      | Có bộ dữ liệu mẫu (hội viên, văn bản đến, template) và kịch bản trình bày theo đúng luồng đã thiết kế. |

## 10. Backlog — Mở Rộng (P2, Chỉ Nếu Còn Thời Gian, Không Cam Kết)

| ID   | Task                                               | Ghi chú                                                                                               |
| ---- | -------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| B-01 | AI tự trích xuất hạn xử lý từ nội dung văn bản đến | Chỉ làm sau khi toàn bộ Phase 0–5 hoàn tất; không đánh đổi thời gian của core scope.                  |
| B-02 | AI hỗ trợ viết bài tin hoạt động từ hình ảnh       | Nhánh kỹ thuật khác (xử lý ảnh), không tái dùng pipeline soạn thảo văn bản; chỉ làm nếu dư thời gian. |

## Ghi Chú Blockers

| Ngày       | Task  | Vấn đề                           | Hướng giải quyết                                                                                            |
| ---------- | ----- | -------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| 2026-07-31 | T0-02 | Thiếu connection string ban đầu. | Đã cấu hình PostgreSQL local, tạo database `DigitalOps`, preflight và áp dụng `InitialBaseline` thành công. |

## Ghi Chú Kỹ Thuật
