# Mẫu thử FR-001 — Xác thực và đổi mật khẩu

## Mục tiêu

Xác nhận đăng nhập JWT, lấy thông tin hiện tại, đổi mật khẩu bắt buộc và client
logout hoạt động đúng mà không tiết lộ tài khoản có tồn tại.

## Tiền điều kiện và dữ liệu

- Có `ADMIN-A`, `TEMP-A`, `INACTIVE-A` như `00-readme.md`.
- Ghi nhận mật khẩu ban đầu của `TEMP-A` ngoài tài liệu này.
- Endpoint: `POST /auth/login`, `GET /auth/me`, `POST /auth/change-password`.

## Payload mẫu

```json
{
  "userNameOrEmail": "TEMP-A",
  "password": "<temporary-password>"
}
```

```json
{
  "currentPassword": "<temporary-password>",
  "newPassword": "<new-strong-password>"
}
```

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR001-01 | Đăng nhập hợp lệ | Đăng nhập bằng username, sau đó lặp lại bằng email của `ADMIN-A` | `200`; có access token, expiry, Staff, roles; `mustChangePassword=false`; UI vào trang nghiệp vụ |
| FR001-02 | Sai username hoặc mật khẩu | Thử một username không tồn tại và một mật khẩu sai | Cả hai trả `401` cùng thông báo chung; không phân biệt tài khoản tồn tại |
| FR001-03 | Staff inactive | Đăng nhập `INACTIVE-A` bằng credential đúng | `401`; không cấp token |
| FR001-04 | Mật khẩu tạm | Đăng nhập `TEMP-A`, mở một route/API nghiệp vụ | Login `200` với `mustChangePassword=true`; UI chuyển `/change-password`; API nghiệp vụ trả `403 password-change-required` |
| FR001-05 | Đổi mật khẩu | Gửi current/new password hợp lệ, sau đó gọi `/auth/me` bằng token mới | `200`; token mới có `mustChangePassword=false`; `/auth/me` trả đúng Staff/roles |
| FR001-06 | Đổi mật khẩu sai | Gửi current password sai hoặc new password không đạt policy | `400 ValidationProblemDetails`; field lỗi rõ; mật khẩu cũ vẫn đăng nhập được |
| FR001-07 | Token thiếu/hỏng | Gọi `/auth/me` không token và với token bị sửa | `401 ProblemDetails`; không trả dữ liệu Staff |
| FR001-08 | Đăng xuất client | Chọn Đăng xuất trong App Shell rồi dùng lại UI | Token/user state bị xóa ở client; route bảo vệ chuyển về `/login` |

## Tiêu chí kết thúc

- Không có response/log UI chứa password.
- Role và `staffId` trong phiên mới đúng với dữ liệu hiện tại.
- Khôi phục `TEMP-A` về trạng thái/mật khẩu dành riêng cho test nếu cần chạy lại.
