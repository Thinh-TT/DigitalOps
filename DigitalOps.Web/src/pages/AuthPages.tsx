import {
  KeyOutlined,
  LockOutlined,
  LoginOutlined,
  LogoutOutlined,
  UserOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  Space,
  Typography,
  type FormInstance,
} from "antd";
import { useState } from "react";
import { Navigate, useNavigate } from "react-router";
import { ApiError } from "../shared/api/api-client";
import { useAuth } from "../shared/auth/auth-context";
import { changePassword, login } from "../shared/auth/auth-service";
import type {
  ChangePasswordRequest,
  LoginRequest,
} from "../shared/auth/types";

interface ChangePasswordFormValues extends ChangePasswordRequest {
  confirmPassword: string;
}

export function LoginPage() {
  const { establishSession } = useAuth();
  const navigate = useNavigate();
  const [form] = Form.useForm<LoginRequest>();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (values: LoginRequest) => {
    setSubmitting(true);
    setErrorMessage(null);

    try {
      const response = await login(values);
      await establishSession(response);
      navigate(
        response.mustChangePassword
          ? "/change-password"
          : "/incoming-documents",
        { replace: true },
      );
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          error instanceof ApiError && error.status === 401
            ? "Tên đăng nhập/email hoặc mật khẩu không đúng."
            : getErrorMessage(error, "Không thể đăng nhập. Vui lòng thử lại."),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="auth-page">
      <Card className="auth-card">
        <Space className="auth-card-content" orientation="vertical" size="large">
          <LoginOutlined className="auth-page-icon" />
          <div>
            <Typography.Title level={2}>Đăng nhập DigitalOps</Typography.Title>
            <Typography.Paragraph type="secondary">
              Truy cập hệ thống điều hành số văn bản và hồ sơ hội viên.
            </Typography.Paragraph>
          </div>
          {errorMessage !== null && (
            <Alert type="error" showIcon title={errorMessage} />
          )}
          <Form
            className="auth-form"
            form={form}
            layout="vertical"
            requiredMark={false}
            onFinish={(values) => void handleSubmit(values)}
          >
            <Form.Item
              name="userNameOrEmail"
              label="Tên đăng nhập hoặc email"
              rules={[
                {
                  required: true,
                  whitespace: true,
                  message: "Vui lòng nhập tên đăng nhập hoặc email.",
                },
              ]}
            >
              <Input
                autoComplete="username"
                prefix={<UserOutlined />}
                placeholder="Tên đăng nhập hoặc email"
              />
            </Form.Item>
            <Form.Item
              name="password"
              label="Mật khẩu"
              rules={[
                {
                  required: true,
                  message: "Vui lòng nhập mật khẩu.",
                },
              ]}
            >
              <Input.Password
                autoComplete="current-password"
                prefix={<LockOutlined />}
                placeholder="Mật khẩu"
              />
            </Form.Item>
            <Button
              block
              htmlType="submit"
              icon={<LoginOutlined />}
              loading={submitting}
              type="primary"
            >
              Đăng nhập
            </Button>
          </Form>
        </Space>
      </Card>
    </main>
  );
}

export function ChangePasswordPage() {
  const { status, currentUser, establishSession, logout } = useAuth();
  const navigate = useNavigate();
  const [form] = Form.useForm<ChangePasswordFormValues>();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }

  if (status === "forbidden") {
    return <Navigate to="/forbidden" replace />;
  }

  const handleSubmit = async (values: ChangePasswordFormValues) => {
    setSubmitting(true);
    setErrorMessage(null);

    try {
      const response = await changePassword({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      await establishSession(response);
      navigate("/incoming-documents", { replace: true });
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể đổi mật khẩu. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="auth-page">
      <Card className="auth-card">
        <Space className="auth-card-content" orientation="vertical" size="large">
          <LockOutlined className="auth-page-icon" />
          <div>
            <Typography.Title level={2}>Đổi mật khẩu</Typography.Title>
            <Typography.Paragraph type="secondary">
              {currentUser?.mustChangePassword
                ? "Bạn phải đổi mật khẩu tạm trước khi sử dụng chức năng nghiệp vụ."
                : "Bạn có thể thay đổi mật khẩu của tài khoản hiện tại."}
            </Typography.Paragraph>
          </div>
          {currentUser?.mustChangePassword && (
            <Alert
              type="warning"
              showIcon
              title="Bạn cần đổi mật khẩu trước khi tiếp tục."
            />
          )}
          {errorMessage !== null && (
            <Alert type="error" showIcon title={errorMessage} />
          )}
          <Form
            className="auth-form"
            form={form}
            layout="vertical"
            requiredMark={false}
            onFinish={(values) => void handleSubmit(values)}
          >
            <Form.Item
              name="currentPassword"
              label="Mật khẩu hiện tại"
              rules={[
                {
                  required: true,
                  message: "Vui lòng nhập mật khẩu hiện tại.",
                },
              ]}
            >
              <Input.Password
                autoComplete="current-password"
                prefix={<LockOutlined />}
              />
            </Form.Item>
            <Form.Item
              name="newPassword"
              label="Mật khẩu mới"
              rules={[
                {
                  required: true,
                  message: "Vui lòng nhập mật khẩu mới.",
                },
              ]}
            >
              <Input.Password
                autoComplete="new-password"
                prefix={<KeyOutlined />}
              />
            </Form.Item>
            <Form.Item
              name="confirmPassword"
              label="Xác nhận mật khẩu mới"
              dependencies={["newPassword"]}
              rules={[
                {
                  required: true,
                  message: "Vui lòng xác nhận mật khẩu mới.",
                },
                ({ getFieldValue }) => ({
                  validator(_, value: string | undefined) {
                    return value === getFieldValue("newPassword")
                      ? Promise.resolve()
                      : Promise.reject(
                          new Error("Mật khẩu xác nhận không khớp."),
                        );
                  },
                }),
              ]}
            >
              <Input.Password
                autoComplete="new-password"
                prefix={<KeyOutlined />}
              />
            </Form.Item>
            <Space wrap>
              <Button
                htmlType="submit"
                icon={<KeyOutlined />}
                loading={submitting}
                type="primary"
              >
                Đổi mật khẩu
              </Button>
              {!currentUser?.mustChangePassword && (
                <Button
                  disabled={submitting}
                  onClick={() =>
                    navigate("/incoming-documents", { replace: true })
                  }
                >
                  Quay lại
                </Button>
              )}
              <Button
                disabled={submitting}
                icon={<LogoutOutlined />}
                onClick={() => {
                  logout();
                  navigate("/login", { replace: true });
                }}
              >
                Đăng xuất
              </Button>
            </Space>
          </Form>
        </Space>
      </Card>
    </main>
  );
}

function applyValidationErrors(
  error: unknown,
  form: FormInstance,
): boolean {
  if (!(error instanceof ApiError) || error.status !== 400) {
    return false;
  }

  const entries = Object.entries(error.problem.errors ?? {});
  if (entries.length === 0) {
    return false;
  }

  form.setFields(
    entries.map(([name, errors]) => ({
      name,
      errors,
    })),
  );
  return true;
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : fallback;
}
