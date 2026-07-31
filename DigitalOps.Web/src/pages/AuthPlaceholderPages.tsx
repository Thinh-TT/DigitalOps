import { LockOutlined, LoginOutlined, LogoutOutlined } from "@ant-design/icons";
import { Alert, Button, Card, Space, Typography } from "antd";
import { Navigate, useNavigate } from "react-router";
import { useAuth } from "../shared/auth/auth-context";

export function LoginPlaceholderPage() {
  return (
    <main className="auth-page">
      <Card className="auth-card">
        <Space direction="vertical" size="large">
          <LoginOutlined className="auth-page-icon" />
          <div>
            <Typography.Title level={2}>Đăng nhập DigitalOps</Typography.Title>
            <Typography.Paragraph type="secondary">
              Truy cập hệ thống điều hành số văn bản và hồ sơ hội viên.
            </Typography.Paragraph>
          </div>
          <Alert
            type="info"
            showIcon
            message="Khung đăng nhập đã sẵn sàng"
            description="Form đăng nhập và kết nối POST /auth/login sẽ được triển khai trong task T1-01."
          />
        </Space>
      </Card>
    </main>
  );
}

export function ChangePasswordPlaceholderPage() {
  const { status, currentUser, logout } = useAuth();
  const navigate = useNavigate();

  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }

  if (status === "forbidden") {
    return <Navigate to="/forbidden" replace />;
  }

  return (
    <main className="auth-page">
      <Card className="auth-card">
        <Space direction="vertical" size="large">
          <LockOutlined className="auth-page-icon" />
          <div>
            <Typography.Title level={2}>Đổi mật khẩu</Typography.Title>
            <Typography.Paragraph type="secondary">
              {currentUser?.mustChangePassword
                ? "Bạn phải đổi mật khẩu tạm trước khi sử dụng chức năng nghiệp vụ."
                : "Bạn có thể thay đổi mật khẩu của tài khoản hiện tại."}
            </Typography.Paragraph>
          </div>
          <Alert
            type="info"
            showIcon
            message="Form đổi mật khẩu thuộc task T1-01"
            description="Route guard hiện chỉ cho phép trang này và đăng xuất khi tài khoản bắt buộc đổi mật khẩu."
          />
          <Button
            icon={<LogoutOutlined />}
            onClick={() => {
              logout();
              navigate("/login", { replace: true });
            }}
          >
            Đăng xuất
          </Button>
        </Space>
      </Card>
    </main>
  );
}
