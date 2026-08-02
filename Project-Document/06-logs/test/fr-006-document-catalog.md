# Mẫu thử FR-006 — Loại văn bản, template và FormatRules

## Mục tiêu

Xác nhận Administrator quản lý loại/mẫu văn bản bằng create/partial update, code
duy nhất, JSON FormatRules hợp lệ và quy tắc active/inactive.

## Dữ liệu mẫu

Document type:

```json
{
  "code": "TEST-TB-20260802",
  "name": "TEST Thông báo synthetic",
  "description": "Chỉ dùng cho kiểm thử",
  "isActive": true
}
```

Document template:

```json
{
  "documentTypeId": "<document-type-id>",
  "name": "TEST Mẫu thông báo synthetic",
  "templateContent": "# THÔNG BÁO\nKính gửi {{member.fullName}}\nNội dung: {{incoming.summary}}",
  "formatRules": {
    "requiredSections": ["THÔNG BÁO", "Nội dung"]
  },
  "isActive": true
}
```

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR006-01 | Tạo loại hợp lệ | Administrator POST document type | `201`; code/name được chuẩn hóa đúng; isActive=true |
| FR006-02 | Code duplicate/validation | Tạo lại code khác hoa thường; gửi code/name rỗng | `400` hoặc `409` theo contract; không tạo duplicate |
| FR006-03 | PATCH partial type | Chỉ đổi description, sau đó đặt `isActive=false` | Field omitted giữ nguyên; inactive biến mất khỏi lookup active |
| FR006-04 | Tạo template hợp lệ | POST payload template với parent active | `201`; FormatRules là JSON object; template đọc lại đúng nội dung |
| FR006-05 | FormatRules không hợp lệ | Gửi array/string/null không hợp lệ hoặc JSON hỏng từ UI | UI chặn JSON hỏng; API trả `400` cho shape sai; không tạo/cập nhật template |
| FR006-06 | Parent inactive/not found | Tạo/activate template dưới type inactive hoặc GUID lạ | Bị từ chối; không có template active mồ côi |
| FR006-07 | Unique template name | Tạo cùng name trong cùng type, sau đó cùng name ở type khác | Cùng type bị từ chối; type khác xử lý theo unique rule hiện hành |
| FR006-08 | PATCH template | Đổi content/FormatRules, rồi inactive | Response và GET phản ánh đúng field; template inactive không dùng tạo outgoing |
| FR006-09 | Template đang được tham chiếu | Sửa/inactive template đã có outgoing | Outgoing cũ giữ content độc lập; liên kết vẫn đọc được; không cascade delete |
| FR006-10 | Sai quyền | Clerk/Drafter/anonymous gọi mutation | `403`/`401`; BusinessAccess chỉ được đọc danh mục theo quyền công khai hiện hành |

## Dọn dữ liệu

- Đặt template synthetic inactive trước, sau đó document type inactive.
- Không thay đổi template seed hoặc FormatRules dùng cho demo thật.
