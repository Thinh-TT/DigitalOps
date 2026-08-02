# Mẫu thử FR-011 — Tạo văn bản đi theo mẫu

## Mục tiêu

Xác nhận Drafter tạo văn bản đi từ template active, render placeholder theo
allow-list, hỗ trợ liên kết optional và giữ Content độc lập sau khi nguồn thay đổi.

## Dữ liệu mẫu

- Template active chứa đầy đủ token Member/Incoming được phép và token lạ
  `{{unknown.token}}`.
- Member synthetic active có gender `Female`, dateOfBirth/joinDate và một field null.
- Incoming synthetic có referenceNumber, senderOrg, summary, receivedDate, deadline.

```json
{
  "title": "TEST 20260802 Văn bản đi 01",
  "templateId": "<active-template-id>",
  "relatedIncomingDocumentId": "<incoming-id>",
  "relatedMemberId": "<member-id>"
}
```

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR011-01 | Tạo đủ liên kết | `DRAFTER-A` POST payload mẫu | `201`; owner là A; status `Editing`; liên kết incoming/member đúng |
| FR011-02 | Render placeholder | Kiểm tra Content vừa tạo | Token có dữ liệu được thay; ngày `dd/MM/yyyy`; gender thành `Nữ`; không chèn dữ liệu ngoài allow-list |
| FR011-03 | Token thiếu/lạ | Để một field Member null và giữ token lạ | Token tương ứng field null và token không biết được giữ nguyên để sửa thủ công |
| FR011-04 | Liên kết optional | Tạo chỉ template/title; sau đó chỉ member và chỉ incoming | Tất cả biến thể hợp lệ; token thiếu nguồn được giữ nguyên |
| FR011-05 | Title/template bắt buộc | Gửi title rỗng/quá 500, templateId null | `400 ValidationProblemDetails`; không tạo outgoing |
| FR011-06 | Nguồn inactive/not found | Dùng template/member inactive hoặc GUID liên kết lạ | Bị từ chối; không tạo document |
| FR011-07 | Content độc lập | Sau khi tạo, sửa template/member/incoming nguồn | Content và liên kết của outgoing cũ không tự thay đổi |
| FR011-08 | Danh sách/filter | Tìm q, template, relatedIncoming, relatedMember, status, owner, date range | Chỉ item khớp; paging đúng; dateFrom > dateTo trả `400` |
| FR011-09 | Quyền | `DRAFTER-B` tạo document của chính B; Clerk/anonymous thử tạo | B tạo thành công và là owner B; sai role `403`, anonymous `401` |
| FR011-10 | Chi tiết | BusinessAccess mở document; GUID lạ | `200` read-only theo role; GUID lạ `404` |

## Dọn dữ liệu

Ghi ID outgoing, template, incoming và member synthetic để cleanup chính xác. Không
inactive nguồn chung trước khi hoàn thành các ca FR-012 phụ thuộc document này.
