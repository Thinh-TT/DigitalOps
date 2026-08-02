# Mẫu thử FR-002 — Quản lý Staff và role

## Mục tiêu

Xác nhận Administrator có thể tạo, tìm, sửa, phân role, reset mật khẩu và vô hiệu
hóa Staff; các role khác không có quyền quản trị.

## Dữ liệu mẫu

```json
{
  "userName": "test-20260802-staff01",
  "email": "test-20260802-staff01@example.invalid",
  "temporaryPassword": "<strong-temporary-password>",
  "fullName": "TEST 20260802 Staff 01",
  "position": "Chuyên viên thử nghiệm",
  "department": "Bộ phận synthetic",
  "phone": "0900000001",
  "roles": ["Clerk", "Drafter"]
}
```

## Ca thử

| ID | Ca thử | Role | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| FR002-01 | Tạo Staff nhiều role | Administrator | Tạo bằng payload mẫu | `201`; Identity và Staff cùng được tạo; roles gồm Clerk/Drafter; bắt buộc đổi mật khẩu |
| FR002-02 | Duplicate username/email | Administrator | Tạo lại cùng username, rồi cùng email | `400`; lỗi theo đúng field; không tạo Staff rác |
| FR002-03 | Validation role/password | Administrator | Gửi role lạ, roles rỗng và mật khẩu yếu | `400 ValidationProblemDetails`; transaction không tạo Identity/Staff |
| FR002-04 | Danh sách/phân trang | Administrator | Tìm theo prefix TEST, đổi page/pageSize | `200`; total/page đúng; không lộ password hash hoặc mật khẩu tạm |
| FR002-05 | PATCH partial và clear field | Administrator | Chỉ đổi fullName; tiếp theo gửi `position: null` | Field không gửi giữ nguyên; position được xóa; email Identity/Staff đồng bộ nếu đổi email |
| FR002-06 | Cập nhật role | Administrator | PUT roles còn `Drafter`; đăng nhập lại Staff | Token mới chỉ có Drafter; token cũ giữ snapshot role đến khi hết hạn |
| FR002-07 | Reset mật khẩu | Administrator | Reset password, thử token/mật khẩu cũ rồi đăng nhập mật khẩu tạm mới | Token nghiệp vụ cũ bị chặn; password cũ sai; password tạm mới đăng nhập và bị buộc đổi |
| FR002-08 | Vô hiệu hóa | Administrator | PATCH `isActive=false`; thử login và token đang có | Login và business access đều bị chặn ngay |
| FR002-09 | Bảo vệ Administrator cuối | Administrator | Thử vô hiệu hóa hoặc bỏ role Administrator cuối cùng | `409`; vẫn còn ít nhất một Administrator active |
| FR002-10 | Sai quyền | Clerk/Drafter/anonymous | Gọi create, patch, roles, reset | `403` cho sai role; `401` khi anonymous; dữ liệu không đổi |

## Dọn dữ liệu

- Đặt Staff synthetic `isActive=false` nếu không có delete endpoint.
- Không dùng tài khoản Administrator thật làm target của ca destructive.
