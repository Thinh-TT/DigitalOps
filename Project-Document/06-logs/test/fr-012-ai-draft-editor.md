# Mẫu thử FR-012 — AI sinh nháp và editor SCR-012

## Mục tiêu

Xác nhận owner Drafter chỉnh title/Content và sinh AI draft có grounded Template
RAG; lần đầu bảo tồn `AiDraftContent`, các lần sau không ghi đè và failure/concurrent
update không làm mất dữ liệu mới hơn.

## Tiền điều kiện

- Outgoing synthetic của `DRAFTER-A` ở `Editing`, dùng template/type active.
- `DRAFTER-B` dùng để kiểm tra non-owner.
- Provider Development, Ollama và Qdrant hoạt động cho các ca success.
- Ghi snapshot `title`, `content`, `aiDraftContent`, `status`, `updatedAt` trước ca lỗi.

## Payload mẫu

PATCH:

```json
{
  "title": "TEST 20260802 Tiêu đề đã chỉnh",
  "content": "Nội dung synthetic đã chỉnh thủ công."
}
```

AI draft:

```json
{
  "instruction": "Viết ngắn gọn bằng tiếng Việt, chỉ dùng dữ liệu synthetic đã cung cấp."
}
```

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR012-01 | Quyền editor | Mở detail bằng A, B, Clerk và anonymous | A thấy Save/AI ở state editable; B/Clerk read-only; anonymous bị `401` |
| FR012-02 | PATCH partial | A chỉ sửa title, sau đó chỉ Content | `200`; field omitted giữ nguyên; status `Editing`; AiDraftContent/review issue không đổi |
| FR012-03 | PATCH validation | Gửi `{}`, title/content null/rỗng hoặc title >500 | `400 ValidationProblemDetails`; database giữ snapshot |
| FR012-04 | Sinh lần đầu | A mở modal, nhập instruction và xác nhận | Một request; `200`; Content = AiDraftContent; status `AiDraft`; modal đóng; tab AI read-only hiển thị bản đầu |
| FR012-05 | Sửa sau AI | Sửa Content/title rồi Save | `200`; status `Editing`; AiDraftContent vẫn bằng bản FR012-04 |
| FR012-06 | Sinh lại | Xác nhận AI lần hai | `200`; Content cập nhật; status `Editing`; AiDraftContent tuyệt đối không đổi |
| FR012-07 | Instruction trắng/optional | Gửi `{}` hoặc instruction chỉ whitespace | Được chuẩn hóa thành không instruction; success vẫn tuân schema/grounding |
| FR012-08 | Dirty guard | Sửa title hoặc Content nhưng chưa Save rồi bấm Sinh nháp AI | UI chặn request và yêu cầu lưu trước; form local không mất |
| FR012-09 | Double submit | Bấm xác nhận AI liên tục khi request đang chạy | Submit bị disable; chỉ có một request/mutation |
| FR012-10 | AI/provider/schema lỗi | Dùng failure stub hoặc ngắt provider/Qdrant trong test environment | `503`; modal vẫn mở, instruction/form giữ nguyên; DB Content/AiDraftContent/status/updatedAt không đổi |
| FR012-11 | Concurrent update | Session A bắt đầu AI; session khác PATCH cùng document trước khi AI trả | AI trả `409`; không ghi đè title/content/status mới hơn; UI không reload resource tự động |
| FR012-12 | Template/type đổi trong lúc AI chạy | Inactive/sửa template test trong thời gian provider xử lý | `409`; không áp dụng output; không mutation outgoing |
| FR012-13 | Locked state | Thử PATCH/AI khi PendingReview/PendingApproval/Approved/Archived | `409`; UI hoàn toàn read-only; attachment action cũng bị khóa |
| FR012-14 | Non-owner | `DRAFTER-B` gọi PATCH/AI document của A | `403`; generator không được gọi; database không đổi |
| FR012-15 | Missing document | Gọi hai endpoint với GUID lạ | `404 ProblemDetails` |
| FR012-16 | Source isolation | Kiểm tra Qdrant/structured logs sau success | Chỉ Template được index/retrieve; không index Member/Incoming/Outgoing/draft; không xóa point Staff/FormatRule |
| FR012-17 | Data minimization | Rà prompt log/evidence và response công khai | Chỉ field Member/Incoming allow-list; có marker untrusted; không lộ raw provider response/API key |

## Chuỗi smoke bắt buộc

1. Sinh AI lần đầu và lưu lại checksum/text của `AiDraftContent`.
2. Sửa thủ công rồi Save; xác nhận status `Editing`.
3. Sinh AI lại; xác nhận Content mới nhưng checksum/text `AiDraftContent` không đổi.
4. Xóa point và dữ liệu synthetic đúng ID; xác nhận source Staff không bị ảnh hưởng.

## Evidence AI

Ghi provider/model, thời gian, HTTP status và ID synthetic. Không ghi raw prompt,
token, API key hoặc dữ liệu cá nhân thật.
