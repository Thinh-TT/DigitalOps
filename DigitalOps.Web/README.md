# DigitalOps Web

React web client cho hệ thống DigitalOps, dùng Vite, TypeScript và Ant Design.

## Yêu cầu

- Node.js 24 hoặc phiên bản tương thích với Vite hiện tại.
- DigitalOps API chạy tại `https://localhost:7162` khi phát triển.

## Chạy local

```powershell
cmd /c npm install
cmd /c npm run dev
```

Vite proxy các request `/api` tới API development. Có thể sao chép `.env.example`
thành `.env.local` để thay đổi URL API.

## Kiểm tra

```powershell
cmd /c npm run lint
cmd /c npm test
cmd /c npm run build
```

T0-04 chỉ cung cấp App Shell, route guard và route placeholder. Form đăng nhập,
đổi mật khẩu và các màn hình nghiệp vụ được triển khai ở các task tiếp theo.
