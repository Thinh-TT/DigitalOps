import { Button, Result, Spin } from "antd";
import { Navigate, Outlet, useLocation } from "react-router";
import { useAuth } from "../shared/auth/auth-context";
import type { Role } from "../shared/auth/types";

export function AuthStatusBoundary() {
  const { status, errorMessage, refreshCurrentUser } = useAuth();

  if (status === "initializing") {
    return (
      <main className="fullscreen-state" aria-label="Đang tải phiên làm việc">
        <Spin size="large" tip="Đang tải phiên làm việc..." />
      </main>
    );
  }

  if (status === "error") {
    return (
      <main className="fullscreen-state">
        <Result
          status="error"
          title="Không thể tải phiên làm việc"
          subTitle={
            errorMessage ??
            "Vui lòng kiểm tra kết nối và thử lại. Phiên hiện tại chưa bị xóa."
          }
          extra={
            <Button type="primary" onClick={() => void refreshCurrentUser()}>
              Thử lại
            </Button>
          }
        />
      </main>
    );
  }

  return <Outlet />;
}

export function PublicOnlyRoute() {
  const { status, currentUser } = useAuth();

  if (status === "forbidden") {
    return <Navigate to="/forbidden" replace />;
  }

  if (status === "authenticated") {
    return (
      <Navigate
        to={
          currentUser?.mustChangePassword
            ? "/change-password"
            : "/incoming-documents"
        }
        replace
      />
    );
  }

  return <Outlet />;
}

export function AuthenticatedRoute() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === "anonymous") {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: `${location.pathname}${location.search}` }}
      />
    );
  }

  if (status === "forbidden") {
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}

export function BusinessAccessRoute() {
  const { currentUser } = useAuth();

  if (currentUser?.mustChangePassword) {
    return <Navigate to="/change-password" replace />;
  }

  return <Outlet />;
}

export function RoleRoute({ allowedRoles }: { allowedRoles: readonly Role[] }) {
  const { currentUser } = useAuth();

  if (
    currentUser === null ||
    !allowedRoles.some((role) => currentUser.roles.includes(role))
  ) {
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}

export function RootRedirect() {
  const { status, currentUser } = useAuth();

  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }

  if (status === "forbidden") {
    return <Navigate to="/forbidden" replace />;
  }

  return (
    <Navigate
      to={
        currentUser?.mustChangePassword
          ? "/change-password"
          : "/incoming-documents"
      }
      replace
    />
  );
}
