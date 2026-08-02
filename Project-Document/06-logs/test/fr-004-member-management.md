# Mẫu thử FR-004 — Tạo, sửa và ngừng hoạt động hội viên

## Mục tiêu

Xác nhận Administrator/Clerk quản lý hồ sơ hội viên theo partial update, validation
và quy tắc ngừng hoạt động không xóa cứng.

## Payload mẫu

```json
{
  "fullName": "TEST 20260802 Hội viên 01",
  "dateOfBirth": "1990-05-20",
  "gender": "Nữ",
  "address": "Địa chỉ synthetic",
  "phone": "0900000011",
  "email": "test.member01@example.invalid",
  "position": "Tổ viên",
  "joinDate": "2026-08-01",
  "status": "Active",
  "notes": "Dữ liệu phục vụ kiểm thử"
}
```

## Ca thử

| ID | Ca thử | Role | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| FR004-01 | Tạo hợp lệ | Administrator/Clerk | POST payload mẫu | `201`; status Active; normalized dữ liệu chuỗi theo service |
| FR004-02 | Thiếu/sai dữ liệu | Administrator/Clerk | FullName rỗng, email sai, ngày sinh tương lai hoặc joinDate không hợp lệ | `400 ValidationProblemDetails`; không tạo bản ghi |
| FR004-03 | Status mặc định | Administrator/Clerk | Tạo payload hợp lệ nhưng bỏ field status | `201`; status mặc định là `Active` |
| FR004-04 | PATCH partial | Administrator/Clerk | Chỉ gửi `address` | Chỉ address và audit time đổi; field khác giữ nguyên |
| FR004-05 | Phân biệt omitted/null | Administrator/Clerk | PATCH `{ "position": null }`, rồi PATCH không chứa position | Lần đầu xóa position; lần sau giữ nguyên null và cập nhật field được gửi |
| FR004-06 | Ngừng hoạt động | Administrator/Clerk | Xác nhận modal deactivate | Status thành Inactive; bản ghi vẫn đọc được; không còn trong lookup Active |
| FR004-07 | Deactivate lặp lại | Administrator/Clerk | Gọi deactivate lần hai | Kết quả idempotent/conflict đúng contract; không xóa dữ liệu hoặc liên kết cũ |
| FR004-08 | Giữ liên kết outgoing | Administrator/Clerk | Deactivate hội viên đã có outgoing liên quan | `RelatedMemberId` của văn bản cũ còn nguyên; văn bản vẫn đọc được |
| FR004-09 | Sai quyền/not found | Drafter/anonymous | Thử create/patch/deactivate và GUID lạ | `403`/`401`/`404` tương ứng; không mutation |

## Dọn dữ liệu

- Deactivate toàn bộ hội viên synthetic còn Active.
- Giữ ID trong evidence nếu hội viên đang được dùng bởi test outgoing.
