import { Button, Result, Space } from "antd";
import { Navigate, useNavigate } from "react-router";
import { useAuth } from "../shared/auth/auth-context";

export function ForbiddenPage() {
  const { status, currentUser, logout } = useAuth();
  const navigate = useNavigate();

  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }

  if (currentUser?.mustChangePassword) {
    return <Navigate to="/change-password" replace />;
  }

  return (
    <main className="fullscreen-state">
      <Result
        status="403"
        title="Không có quyền truy cập"
        subTitle="Tài khoản hiện tại không được phép mở trang hoặc thực hiện thao tác này."
        extra={
          <Space>
            {currentUser !== null && (
              <Button
                type="primary"
                onClick={() => navigate("/incoming-documents", { replace: true })}
              >
                Về văn bản đến
              </Button>
            )}
            <Button
              onClick={() => {
                logout();
                navigate("/login", { replace: true });
              }}
            >
              Đăng xuất
            </Button>
          </Space>
        }
      />
    </main>
  );
}

export function NotFoundPage() {
  const navigate = useNavigate();

  return (
    <main className="fullscreen-state">
      <Result
        status="404"
        title="Không tìm thấy trang"
        subTitle="Đường dẫn bạn yêu cầu không tồn tại trong DigitalOps."
        extra={
          <Button type="primary" onClick={() => navigate("/", { replace: true })}>
            Về trang mặc định
          </Button>
        }
      />
    </main>
  );
}
