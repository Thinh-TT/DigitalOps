# Mẫu thử FR-007 — Tiếp nhận và quản lý văn bản đến

## Mục tiêu

Xác nhận Clerk tạo/sửa văn bản đến, mọi BusinessAccess được tra cứu, ngày tháng và
state transition hợp lệ, đồng thời khóa dữ liệu hành chính sau `Completed`.

## Payload mẫu

```json
{
  "referenceNumber": "TEST/20260802/IN-01",
  "senderOrg": "Đơn vị synthetic",
  "summary": "TEST đề nghị phối hợp hoạt động nội bộ",
  "receivedDate": "2026-08-02",
  "deadline": "2026-08-10",
  "documentTypeId": "<active-document-type-id>"
}
```

## Ca thử

| ID | Ca thử | Role | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| FR007-01 | Tạo hợp lệ | Clerk | POST payload mẫu | `201`; status `New`; assigned/suggestion/completed rỗng |
| FR007-02 | Validation ngày | Clerk | Deadline trước receivedDate hoặc thiếu field bắt buộc | `400 ValidationProblemDetails`; không tạo document |
| FR007-03 | Type inactive/not found | Clerk | Dùng type inactive hoặc GUID lạ | `400`/`404` theo contract; không tạo document |
| FR007-04 | Danh sách/filter | BusinessAccess | Tìm q, type, status, assigned staff, deadline range, paging | Items và metadata đúng tất cả filter; dateFrom > dateTo trả `400` |
| FR007-05 | Xem chi tiết | BusinessAccess | Mở ID hợp lệ và GUID lạ | `200` với metadata/attachment; GUID lạ `404` |
| FR007-06 | PATCH partial | Clerk | Chỉ đổi summary, sau đó deadline | Chỉ field gửi thay đổi; status vẫn hợp lệ; audit time cập nhật |
| FR007-07 | Omitted/null/empty | Clerk | Gửi `{}`, field bắt buộc null/rỗng | `400`; dữ liệu cũ giữ nguyên |
| FR007-08 | Hoàn tất đúng quyền | Assigned Staff hoặc Clerk | Sau khi assignment, gọi `/complete` | `200`; status `Completed`, completedAt có giá trị |
| FR007-09 | Hoàn tất sai người/trạng thái | Staff khác hoặc document chưa đủ điều kiện | Gọi `/complete` | `403` hoặc `409`; không đổi status |
| FR007-10 | Khóa sau Completed | Clerk | PATCH summary/deadline của document Completed | `409`; dữ liệu hành chính và updatedAt không bị ghi đè |
| FR007-11 | Sai quyền tạo/sửa | Drafter/anonymous | Gọi POST/PATCH | `403`/`401`; vẫn có thể GET nếu là BusinessAccess active |

## Dọn dữ liệu

Ghi lại ID document synthetic. Không có public delete endpoint; chỉ cleanup trong
database development theo quy trình đã duyệt và đúng ID/prefix.
