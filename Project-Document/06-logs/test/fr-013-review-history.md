# Mẫu thử FR-013 — Thẩm định thể thức và lịch sử review SCR-013

## Mục tiêu

Xác nhận Drafter owner gửi thẩm định thể thức cho outgoing document; mỗi lần hợp lệ
tạo duy nhất một lịch sử bất biến với snapshot nội dung, đồng thời workflow chuyển
`ReviewFailed` khi deterministic rule có `Error` hoặc `PendingApproval` khi review
`Passed`. Người có BusinessAccess có thể xem lịch sử nhưng không thể gửi thay owner.

## Tiền điều kiện

- Outgoing synthetic `TEST-<YYYYMMDD>-FR013` của `DRAFTER-A`, trạng thái `Editing`,
  với template có ba FormatRules bắt buộc: `national_header`, `reference_number`,
  `signature_block`.
- `DRAFTER-B` là non-owner; `ADMIN-A` hoặc `CLERK-A` dùng để kiểm tra BusinessAccess.
- Với nhánh Hybrid: PostgreSQL, Ollama embedding, Qdrant và provider Development sẵn sàng.
- Ghi lại `content`, `status`, `reviewIssues`, `updatedAt` trước các ca lỗi/timeout.

## Endpoint

- `POST /api/v1/outgoing-documents/{id}/reviews` không có request body.
- `GET /api/v1/outgoing-documents/{id}/reviews?page=1&pageSize=20` trả newest-first.

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR013-01 | Quyền gửi | Gửi POST bằng anonymous, Clerk, DRAFTER-B và DRAFTER-A | Lần lượt `401`, `403`, `403`, `200`; generator chỉ chạy cho A. |
| FR013-02 | Status hợp lệ | A gửi tại `Editing` rồi sau khi sửa gửi lại từ `ReviewFailed` | Hai lần được chấp nhận; trạng thái trung gian không lộ ra ngoài. |
| FR013-03 | Status khóa | POST tại `AiDraft`, `PendingReview`, `PendingApproval`, `Approved`, `Archived` | `409 ProblemDetails`; không có history mới. |
| FR013-04 | Quốc hiệu/tiêu ngữ | Dùng đầy đủ fixture T0-00 thiếu/sai quốc hiệu hoặc tiêu ngữ | Có `Error national_header`, source `Rule`, result `Failed`. |
| FR013-05 | Số/ký hiệu | Dùng fixture T0-00 không có dòng bắt đầu `Số:` | Có `Error reference_number`, source `Rule`, result `Failed`. |
| FR013-06 | Khối chữ ký | Dùng fixture T0-00 thiếu `ĐẠI DIỆN CƠ QUAN` hoặc `Ký, ghi rõ họ tên` | Có `Error signature_block`, source `Rule`, result `Failed`. |
| FR013-07 | Fixture sạch và injection | Chạy các fixture T0-00 đúng thể thức, bao gồm nội dung cố gắng đưa chỉ dẫn/prompt | Rule không tạo Error; AI chỉ được trả `Warning`/`Info`; không rò prompt. |
| FR013-08 | Rule không hỗ trợ | Bật `required=true` cho rule chưa có engine | `503`; Content/status/latest issues/history giữ snapshot trước request. |
| FR013-09 | AI/schema/provider lỗi | Mô phỏng timeout, provider lỗi, severity Error, sourceRef giả, schema sai hoặc kết luận pháp lý | `503`; không transaction/mutation nào được lưu. |
| FR013-10 | History và snapshot | Thẩm định Failed, sửa Content, rồi thẩm định Passed | Có attempt 1/2, contentSnapshot mỗi attempt đúng thời điểm; GET trả attempt 2 trước; không có API PATCH/DELETE history. |
| FR013-11 | Cạnh tranh | Hai POST của A bắt đầu cùng lúc trên document `Editing` | Chính xác một `200`, một `409`; chỉ attempt 1 trong database. |
| FR013-12 | Template đổi | Cập nhật FormatRules/template trong lúc review AI chờ kết quả | `409`; kết quả cũ không ghi đè document/history. |
| FR013-13 | Paging và đọc lịch sử | BusinessAccess non-owner gọi GET với page/pageSize và mở detail history ở SCR-013 | Xem được reviewer, thời điểm, source/result, snapshot read-only và issues; nút gửi không xuất hiện. |
| FR013-14 | Dirty/double guard | Sửa title/content chưa Save rồi gửi; sau đó double-click gửi trong lúc request chạy | UI chặn gửi khi dirty; chỉ một POST khi đang chạy. |
| FR013-15 | UI lỗi bảo toàn dữ liệu | Mô phỏng `409` hoặc `503` từ POST | Form và history đang hiện giữ nguyên, không auto-reload/ghi đè nội dung. |
| FR013-16 | Passed khóa editor | Review Hybrid/AI Passed có Warning/Info | Không có Error, document thành `PendingApproval`, toàn bộ editor read-only. |

## Live smoke opt-in

Đặt `DIGITALOPS_RUN_AI_REVIEW_SMOKE=1` rồi chạy test Category `LiveAiReview` theo
`SETUP.md`. Smoke phải xác nhận lần đầu `Rule/Failed`, sau khi sửa đúng thể thức là
`Hybrid/Passed`, và cleanup chính xác review history, outgoing, template/type cùng
FormatRule point synthetic. Evidence chỉ ghi ID synthetic, provider/model, thời điểm và
HTTP/result; không ghi prompt thô, API key hoặc dữ liệu cá nhân thật.
