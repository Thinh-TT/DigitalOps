# Mẫu thử FR-014 — Phê duyệt hoặc trả lại văn bản SCR-014

## Mục tiêu

Xác nhận chỉ Lãnh đạo đang hoạt động có thể xử lý văn bản `PendingApproval` khi
lần ReviewHistory mới nhất là `Passed`. Phê duyệt phải ghi người/thời điểm duyệt;
trả lại phải đưa văn bản về `Editing`, xóa dữ liệu duyệt và bắt buộc review lại
trước lần trình duyệt tiếp theo.

## Tiền điều kiện

- Outgoing synthetic `TEST-<YYYYMMDD>-FR014` do `DRAFTER-A` soạn, đã tạo một
  review `Passed` và có status `PendingApproval`.
- `LEADER-A` là tài khoản Leader active; `ADMIN-A`, `DRAFTER-A`, `DRAFTER-B`,
  `INACTIVE-A` và `TEMP-A` sẵn sàng để kiểm tra boundary phân quyền.
- Ghi lại Content, AiDraftContent, ReviewIssues, AttemptNo mới nhất và danh
  sách attachment trước các thao tác Return/Approve.

## Endpoint

- `GET /api/v1/outgoing-documents?status=PendingApproval&page=1&pageSize=20`
- `POST /api/v1/outgoing-documents/{id}/approval`

```json
{ "decision": "Approve" }
```

hoặc

```json
{ "decision": "Return" }
```

MVP không có approval comment, approval history riêng hoặc notification cho
người soạn.

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR014-01 | Hàng chờ Leader | LEADER-A mở SCR-014; đổi page khi có nhiều hơn 20 document | Chỉ gọi/filter `PendingApproval`; table có title/template/drafter/updatedAt, loading/empty/retry/paging đúng. |
| FR014-02 | Drawer evidence | Mở một document trong hàng chờ | Hiển thị Content, AiDraftContent nếu có, liên kết liên quan, latest issues, review history và contentSnapshot; không có trường comment. |
| FR014-03 | Quyền endpoint | POST bằng anonymous, ADMIN-A, DRAFTER-A, DRAFTER-B, INACTIVE-A, TEMP-A và LEADER-A | Lần lượt `401`, `403`, `403`, `403`, chặn theo access policy, chặn must-change-password, `200` cho LEADER-A. Administrator không có quyền ngầm. |
| FR014-04 | Validate request | LEADER-A gửi body thiếu decision, `null`, số enum hoặc giá trị khác `Approve`/`Return` | `400 ValidationProblemDetails` theo field `decision`; không mutation. |
| FR014-05 | Điều kiện trạng thái | LEADER-A gửi approve tại AiDraft, Editing, PendingReview, ReviewFailed, Approved hoặc Archived | `409 ProblemDetails`; approval tuple, Content và history giữ nguyên. |
| FR014-06 | Latest review | Dùng document có status PendingApproval nhưng latest review Failed/không có history | `409`; không thể bypass thẩm định bằng đổi status thủ công. |
| FR014-07 | Phê duyệt | LEADER-A chọn Duyệt và xác nhận modal | `200`; response/status là `Approved`, `approvedByStaff` là LEADER-A, `approvedAt` UTC; queue refresh và document biến mất. |
| FR014-08 | Trả lại | LEADER-A chọn Trả lại chỉnh sửa và xác nhận modal | `200`; status `Editing`, approval tuple null, Content/AiDraftContent/issues/attachments/ReviewHistory cũ giữ nguyên; queue refresh và drawer đóng. |
| FR014-09 | Review lại bắt buộc | Ngay sau FR014-08 gọi approval lần nữa; DRAFTER-A sửa rồi review Passed, sau đó LEADER-A approve | Lần đầu `409`; chỉ review attempt mới đạt đưa document trở lại PendingApproval, lúc đó approve thành công. |
| FR014-10 | Cạnh tranh | Hai request Approve/Return của Leader bắt đầu đồng thời trên cùng document | Chính xác một `200`, một `409`; trạng thái cuối nhất quán Approved hoặc Editing, không có approval tuple nửa vời. |
| FR014-11 | UI double-submit/xung đột | Double-click action khi request đang chạy; mô phỏng `409` từ API | Chỉ một POST được gửi; UI giữ thông báo lỗi, refresh queue/resource và đóng drawer nếu document không còn PendingApproval. |
| FR014-12 | OpenAPI và read-only | Kiểm tra Swagger/OpenAPI và mở outgoing detail sau Approve/Return | Schema chỉ có enum Approve/Return và response 200/400/401/403/404/409; Approved read-only, Return cho owner sửa/review lại. |

## Dọn dữ liệu

- Chỉ dùng document/template/history synthetic của lượt chạy và ghi lại ID trong
  evidence.
- Không xóa hoặc sửa ReviewHistory cũ của dữ liệu không thuộc ca thử.
- Nếu cần cleanup development, dùng quy trình đã được phê duyệt; không thay đổi
  document đã Archived hay dữ liệu production.
