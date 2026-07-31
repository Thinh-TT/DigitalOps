import {
  ArrowLeftOutlined,
  KeyOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  StopOutlined,
  UserAddOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Result,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
  type FormInstance,
  type TableProps,
} from "antd";
import { useEffect, useState } from "react";
import {
  useLocation,
  useNavigate,
  useParams,
} from "react-router";
import { ApiError } from "../shared/api/api-client";
import type { PagedResponse } from "../shared/api/types";
import { useAuth } from "../shared/auth/auth-context";
import {
  systemRoles,
  type Role,
} from "../shared/auth/types";
import {
  createStaff,
  getStaff,
  getStaffList,
  replaceStaffRoles,
  resetStaffPassword,
  updateStaff,
} from "../shared/staff/staff-service";
import type {
  StaffCreateRequest,
  StaffResponse,
  StaffUpdateRequest,
} from "../shared/staff/types";

const roleLabels: Record<Role, string> = {
  Administrator: "Quản trị viên",
  Clerk: "Văn thư",
  Drafter: "Cán bộ xử lý/soạn thảo",
  Leader: "Lãnh đạo",
};

const roleOptions = systemRoles.map((role) => ({
  value: role,
  label: roleLabels[role],
}));

interface StaffCreateFormValues {
  userName: string;
  email: string;
  temporaryPassword: string;
  confirmPassword: string;
  fullName: string;
  position?: string;
  department?: string;
  phone?: string;
  roles: Role[];
}

interface StaffProfileFormValues {
  userName: string;
  fullName: string;
  position?: string;
  department?: string;
  email: string;
  phone?: string;
}

interface ResetPasswordFormValues {
  temporaryPassword: string;
  confirmPassword: string;
}

export function StaffListPage() {
  const navigate = useNavigate();
  const [activeOnly, setActiveOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [data, setData] =
    useState<PagedResponse<StaffResponse> | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let ignored = false;

    void (async () => {
      await Promise.resolve();
      if (ignored) {
        return;
      }

      setLoading(true);
      setErrorMessage(null);

      try {
        const response = await getStaffList({ activeOnly, page, pageSize });
        if (!ignored) {
          setData(response);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(
            getErrorMessage(
              error,
              "Không thể tải danh sách Staff. Vui lòng thử lại.",
            ),
          );
        }
      } finally {
        if (!ignored) {
          setLoading(false);
        }
      }
    })();

    return () => {
      ignored = true;
    };
  }, [activeOnly, page, pageSize, reloadVersion]);

  const columns: TableProps<StaffResponse>["columns"] = [
    {
      title: "Họ và tên",
      dataIndex: "fullName",
      key: "fullName",
    },
    {
      title: "Tài khoản",
      key: "account",
      render: (_, staff) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text>{staff.userName}</Typography.Text>
          <Typography.Text type="secondary">{staff.email}</Typography.Text>
        </Space>
      ),
    },
    {
      title: "Bộ phận / Chức vụ",
      key: "organization",
      render: (_, staff) =>
        [staff.department, staff.position].filter(Boolean).join(" · ") || "—",
    },
    {
      title: "Role",
      dataIndex: "roles",
      key: "roles",
      render: (roles: Role[]) => (
        <Space wrap size={[4, 4]}>
          {roles.map((role) => (
            <Tag key={role} color="blue">
              {roleLabels[role]}
            </Tag>
          ))}
        </Space>
      ),
    },
    {
      title: "Trạng thái",
      dataIndex: "isActive",
      key: "isActive",
      render: (isActive: boolean) => (
        <Tag color={isActive ? "success" : "default"}>
          {isActive ? "Đang hoạt động" : "Ngừng hoạt động"}
        </Tag>
      ),
    },
    {
      title: "Thao tác",
      key: "action",
      render: (_, staff) => (
        <Button type="link" onClick={() => navigate(`/staff/${staff.id}`)}>
          Xem
        </Button>
      ),
    },
  ];

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Staff và role</Typography.Title>
          <Typography.Text type="secondary">
            Quản lý tài khoản nội bộ, hồ sơ cán bộ và phân quyền.
          </Typography.Text>
        </div>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => navigate("/staff/new")}
        >
          Tạo Staff
        </Button>
      </div>

      <Card>
        <Space wrap>
          <Typography.Text>Chỉ Staff đang hoạt động</Typography.Text>
          <Switch
            aria-label="Chỉ Staff đang hoạt động"
            checked={activeOnly}
            onChange={(checked) => {
              setActiveOnly(checked);
              setPage(1);
            }}
          />
          <Button
            icon={<ReloadOutlined />}
            onClick={() => setReloadVersion((version) => version + 1)}
          >
            Tải lại
          </Button>
        </Space>
      </Card>

      {errorMessage !== null && (
        <Alert
          type="error"
          showIcon
          title={errorMessage}
          action={
            <Button
              size="small"
              onClick={() => setReloadVersion((version) => version + 1)}
            >
              Thử lại
            </Button>
          }
        />
      )}

      <Card>
        <Table
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={loading}
          locale={{ emptyText: "Không có Staff phù hợp." }}
          pagination={{
            current: data?.page ?? page,
            pageSize: data?.pageSize ?? pageSize,
            total: data?.totalCount ?? 0,
            showSizeChanger: true,
            pageSizeOptions: [10, 20, 50, 100],
            showTotal: (total, range) =>
              `Hiển thị ${range[0]}-${range[1]}/${total}`,
          }}
          onChange={(pagination) => {
            setPage(pagination.current ?? 1);
            setPageSize(pagination.pageSize ?? 20);
          }}
        />
      </Card>
    </Space>
  );
}

export function StaffCreatePage() {
  const navigate = useNavigate();
  const [form] = Form.useForm<StaffCreateFormValues>();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (values: StaffCreateFormValues) => {
    setSubmitting(true);
    setErrorMessage(null);

    const request: StaffCreateRequest = {
      userName: values.userName.trim(),
      email: values.email.trim(),
      temporaryPassword: values.temporaryPassword,
      fullName: values.fullName.trim(),
      position: normalizeOptional(values.position),
      department: normalizeOptional(values.department),
      phone: normalizeOptional(values.phone),
      roles: values.roles,
    };

    try {
      const created = await createStaff(request);
      navigate(`/staff/${created.id}`, {
        replace: true,
        state: {
          success:
            "Đã tạo Staff với mật khẩu tạm. Người dùng phải đổi mật khẩu trước khi thao tác nghiệp vụ.",
        },
      });
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(error, "Không thể tạo Staff. Vui lòng thử lại."),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title="Tạo Staff"
        description="Tạo tài khoản, hồ sơ cán bộ và gán một hoặc nhiều role."
      />
      {errorMessage !== null && (
        <Alert type="error" showIcon title={errorMessage} />
      )}
      <Card>
        <Form
          className="staff-form"
          form={form}
          layout="vertical"
          requiredMark
          onFinish={(values) => void handleSubmit(values)}
        >
          <div className="staff-form-grid">
            <Form.Item
              name="userName"
              label="Tên đăng nhập"
              rules={[
                {
                  required: true,
                  whitespace: true,
                  message: "Vui lòng nhập tên đăng nhập.",
                },
              ]}
            >
              <Input autoComplete="username" />
            </Form.Item>
            <Form.Item
              name="email"
              label="Email"
              rules={[
                {
                  required: true,
                  message: "Vui lòng nhập email.",
                },
                {
                  type: "email",
                  message: "Email không đúng định dạng.",
                },
              ]}
            >
              <Input autoComplete="email" />
            </Form.Item>
            <Form.Item
              name="fullName"
              label="Họ và tên"
              rules={[
                {
                  required: true,
                  whitespace: true,
                  message: "Vui lòng nhập họ và tên.",
                },
              ]}
            >
              <Input autoComplete="name" />
            </Form.Item>
            <Form.Item name="phone" label="Số điện thoại">
              <Input autoComplete="tel" />
            </Form.Item>
            <Form.Item name="department" label="Bộ phận">
              <Input />
            </Form.Item>
            <Form.Item name="position" label="Chức vụ">
              <Input />
            </Form.Item>
            <Form.Item
              name="temporaryPassword"
              label="Mật khẩu tạm"
              rules={[
                {
                  required: true,
                  message: "Vui lòng nhập mật khẩu tạm.",
                },
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            <Form.Item
              name="confirmPassword"
              label="Xác nhận mật khẩu tạm"
              dependencies={["temporaryPassword"]}
              rules={[
                {
                  required: true,
                  message: "Vui lòng xác nhận mật khẩu tạm.",
                },
                passwordConfirmationRule("temporaryPassword"),
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
          </div>
          <Form.Item
            name="roles"
            label="Role"
            rules={[
              {
                required: true,
                type: "array",
                min: 1,
                message: "Vui lòng chọn ít nhất một role.",
              },
            ]}
          >
            <Select
              mode="multiple"
              options={roleOptions}
              placeholder="Chọn role"
            />
          </Form.Item>
          <Space wrap>
            <Button
              type="primary"
              htmlType="submit"
              icon={<UserAddOutlined />}
              loading={submitting}
            >
              Tạo Staff
            </Button>
            <Button disabled={submitting} onClick={() => navigate("/staff")}>
              Hủy
            </Button>
          </Space>
        </Form>
      </Card>
    </Space>
  );
}

export function StaffDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { currentUser, refreshCurrentUser, logout } = useAuth();
  const [profileForm] = Form.useForm<StaffProfileFormValues>();
  const [roleForm] = Form.useForm<{ roles: Role[] }>();
  const [resetForm] = Form.useForm<ResetPasswordFormValues>();
  const [staff, setStaff] = useState<StaffResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [profileSubmitting, setProfileSubmitting] = useState(false);
  const [roleSubmitting, setRoleSubmitting] = useState(false);
  const [statusSubmitting, setStatusSubmitting] = useState(false);
  const [resetSubmitting, setResetSubmitting] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [deactivateOpen, setDeactivateOpen] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(
    readNavigationSuccess(location.state),
  );
  const [reloadVersion, setReloadVersion] = useState(0);

  useEffect(() => {
    let ignored = false;

    void (async () => {
      await Promise.resolve();
      if (ignored) {
        return;
      }

      setLoading(true);
      setNotFound(false);
      setErrorMessage(null);

      try {
        const response = await getStaff(id);
        if (ignored) {
          return;
        }

        setStaff(response);
      } catch (error) {
        if (ignored) {
          return;
        }

        if (error instanceof ApiError && error.status === 404) {
          setNotFound(true);
          return;
        }

        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể tải thông tin Staff. Vui lòng thử lại.",
          ),
        );
      } finally {
        if (!ignored) {
          setLoading(false);
        }
      }
    })();

    return () => {
      ignored = true;
    };
  }, [id, profileForm, reloadVersion, roleForm]);

  useEffect(() => {
    if (staff === null) {
      return;
    }

    setProfileForm(staff, profileForm);
    roleForm.setFieldsValue({ roles: staff.roles });
  }, [profileForm, roleForm, staff]);

  if (notFound) {
    return (
      <Result
        status="404"
        title="Không tìm thấy Staff"
        subTitle="Staff có thể không tồn tại hoặc đã thay đổi."
        extra={<Button onClick={() => navigate("/staff")}>Về danh sách</Button>}
      />
    );
  }

  const handleProfileSubmit = async (values: StaffProfileFormValues) => {
    if (staff === null) {
      return;
    }

    const request: StaffUpdateRequest = {};

    if (profileForm.isFieldTouched("fullName")) {
      request.fullName = values.fullName.trim();
    }
    if (profileForm.isFieldTouched("email")) {
      request.email = values.email.trim();
    }
    if (profileForm.isFieldTouched("position")) {
      request.position = normalizeOptional(values.position);
    }
    if (profileForm.isFieldTouched("department")) {
      request.department = normalizeOptional(values.department);
    }
    if (profileForm.isFieldTouched("phone")) {
      request.phone = normalizeOptional(values.phone);
    }

    if (Object.keys(request).length === 0) {
      setSuccessMessage("Không có thay đổi cần lưu.");
      return;
    }

    setProfileSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const updated = await updateStaff(staff.id, request);
      setStaff(updated);
      setProfileForm(updated, profileForm);
      setSuccessMessage("Đã cập nhật hồ sơ Staff.");

      if (currentUser?.staff.id === updated.id) {
        await refreshCurrentUser();
      }
    } catch (error) {
      if (!applyValidationErrors(error, profileForm)) {
        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể cập nhật Staff. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setProfileSubmitting(false);
    }
  };

  const handleRolesSubmit = async ({ roles }: { roles: Role[] }) => {
    if (staff === null) {
      return;
    }

    setRoleSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const updated = await replaceStaffRoles(staff.id, { roles });
      setStaff(updated);
      roleForm.setFieldsValue({ roles: updated.roles });
      setSuccessMessage(
        "Đã cập nhật role. Quyền mới có hiệu lực khi tài khoản nhận JWT tiếp theo.",
      );
    } catch (error) {
      if (!applyValidationErrors(error, roleForm)) {
        setErrorMessage(
          getErrorMessage(error, "Không thể cập nhật role. Vui lòng thử lại."),
        );
      }
    } finally {
      setRoleSubmitting(false);
    }
  };

  const changeStatus = async (isActive: boolean) => {
    if (staff === null) {
      return;
    }

    setStatusSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const updated = await updateStaff(staff.id, { isActive });
      setDeactivateOpen(false);

      if (!updated.isActive && currentUser?.staff.id === updated.id) {
        logout();
        navigate("/login", { replace: true });
        return;
      }

      setStaff(updated);
      setSuccessMessage(
        updated.isActive
          ? "Đã kích hoạt lại Staff."
          : "Đã vô hiệu hóa Staff.",
      );
    } catch (error) {
      setErrorMessage(
        getErrorMessage(
          error,
          isActive
            ? "Không thể kích hoạt lại Staff."
            : "Không thể vô hiệu hóa Staff.",
        ),
      );
    } finally {
      setStatusSubmitting(false);
    }
  };

  const handleResetPassword = async (values: ResetPasswordFormValues) => {
    if (staff === null) {
      return;
    }

    setResetSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      await resetStaffPassword(staff.id, {
        temporaryPassword: values.temporaryPassword,
      });
      setResetOpen(false);
      resetForm.resetFields();
      setSuccessMessage(
        "Đã đặt mật khẩu tạm. Staff phải đổi mật khẩu trước khi thao tác nghiệp vụ.",
      );
    } catch (error) {
      if (!applyValidationErrors(error, resetForm)) {
        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể reset mật khẩu. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setResetSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title={staff?.fullName ?? "Chi tiết Staff"}
        description={
          staff === null
            ? "Đang tải thông tin Staff."
            : `Tài khoản: ${staff.userName}`
        }
      />

      {successMessage !== null && (
        <Alert
          type="success"
          showIcon
          closable
          title={successMessage}
          onClose={() => setSuccessMessage(null)}
        />
      )}
      {errorMessage !== null && (
        <Alert
          type="error"
          showIcon
          title={errorMessage}
          action={
            staff === null ? (
              <Button
                size="small"
                onClick={() => setReloadVersion((version) => version + 1)}
              >
                Thử lại
              </Button>
            ) : undefined
          }
        />
      )}

      <Card loading={loading} title="Hồ sơ và tài khoản">
        {staff !== null && (
          <Form
            className="staff-form"
            form={profileForm}
            layout="vertical"
            onFinish={(values) => void handleProfileSubmit(values)}
          >
            <div className="staff-form-grid">
              <Form.Item name="userName" label="Tên đăng nhập">
                <Input disabled />
              </Form.Item>
              <Form.Item
                name="email"
                label="Email"
                rules={[
                  { required: true, message: "Vui lòng nhập email." },
                  { type: "email", message: "Email không đúng định dạng." },
                ]}
              >
                <Input autoComplete="email" />
              </Form.Item>
              <Form.Item
                name="fullName"
                label="Họ và tên"
                rules={[
                  {
                    required: true,
                    whitespace: true,
                    message: "Vui lòng nhập họ và tên.",
                  },
                ]}
              >
                <Input autoComplete="name" />
              </Form.Item>
              <Form.Item name="phone" label="Số điện thoại">
                <Input autoComplete="tel" />
              </Form.Item>
              <Form.Item name="department" label="Bộ phận">
                <Input />
              </Form.Item>
              <Form.Item name="position" label="Chức vụ">
                <Input />
              </Form.Item>
            </div>
            <Space wrap>
              <Button
                type="primary"
                htmlType="submit"
                icon={<SaveOutlined />}
                loading={profileSubmitting}
              >
                Lưu hồ sơ
              </Button>
              <Tag color={staff.isActive ? "success" : "default"}>
                {staff.isActive ? "Đang hoạt động" : "Ngừng hoạt động"}
              </Tag>
              {staff.isActive ? (
                <Button
                  danger
                  icon={<StopOutlined />}
                  loading={statusSubmitting}
                  onClick={() => setDeactivateOpen(true)}
                >
                  Vô hiệu hóa
                </Button>
              ) : (
                <Button
                  loading={statusSubmitting}
                  onClick={() => void changeStatus(true)}
                >
                  Kích hoạt lại
                </Button>
              )}
            </Space>
          </Form>
        )}
      </Card>

      <div className="staff-detail-grid">
        <Card title="Phân quyền">
          {staff !== null && (
            <Form
              form={roleForm}
              layout="vertical"
              onFinish={(values) => void handleRolesSubmit(values)}
            >
              <Form.Item
                name="roles"
                label="Role"
                rules={[
                  {
                    required: true,
                    type: "array",
                    min: 1,
                    message: "Vui lòng chọn ít nhất một role.",
                  },
                ]}
              >
                <Select mode="multiple" options={roleOptions} />
              </Form.Item>
              <Button
                htmlType="submit"
                icon={<SaveOutlined />}
                loading={roleSubmitting}
                type="primary"
              >
                Lưu role
              </Button>
            </Form>
          )}
        </Card>

        <Card title="Bảo mật tài khoản">
          <Space orientation="vertical">
            <Typography.Text>
              Reset mật khẩu sẽ buộc Staff đổi mật khẩu tạm trước khi tiếp tục.
            </Typography.Text>
            <Button
              icon={<KeyOutlined />}
              onClick={() => {
                setErrorMessage(null);
                setResetOpen(true);
              }}
            >
              Reset mật khẩu
            </Button>
          </Space>
        </Card>
      </div>

      <Modal
        title="Xác nhận vô hiệu hóa Staff"
        open={deactivateOpen}
        okText="Vô hiệu hóa"
        okButtonProps={{ danger: true }}
        confirmLoading={statusSubmitting}
        cancelText="Hủy"
        onCancel={() => setDeactivateOpen(false)}
        onOk={() => void changeStatus(false)}
      >
        Staff sẽ không thể đăng nhập hoặc tiếp tục thao tác bằng phiên hiện có.
        Dữ liệu lịch sử vẫn được giữ nguyên.
      </Modal>

      <Modal
        title="Đặt mật khẩu tạm"
        open={resetOpen}
        okText="Reset mật khẩu"
        confirmLoading={resetSubmitting}
        cancelText="Hủy"
        onCancel={() => {
          if (!resetSubmitting) {
            setResetOpen(false);
            resetForm.resetFields();
          }
        }}
        onOk={() => resetForm.submit()}
      >
        <Form
          form={resetForm}
          layout="vertical"
          preserve={false}
          onFinish={(values) => void handleResetPassword(values)}
        >
          <Form.Item
            name="temporaryPassword"
            label="Mật khẩu tạm mới"
            rules={[
              {
                required: true,
                message: "Vui lòng nhập mật khẩu tạm.",
              },
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label="Xác nhận mật khẩu tạm"
            dependencies={["temporaryPassword"]}
            rules={[
              {
                required: true,
                message: "Vui lòng xác nhận mật khẩu tạm.",
              },
              passwordConfirmationRule("temporaryPassword"),
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}

function PageBackHeading({
  title,
  description,
}: {
  title: string;
  description: string;
}) {
  const navigate = useNavigate();

  return (
    <div className="page-heading-row">
      <div>
        <Typography.Title level={2}>{title}</Typography.Title>
        <Typography.Text type="secondary">{description}</Typography.Text>
      </div>
      <Button
        icon={<ArrowLeftOutlined />}
        onClick={() => navigate("/staff")}
      >
        Về danh sách
      </Button>
    </div>
  );
}

function setProfileForm(
  staff: StaffResponse,
  form: FormInstance<StaffProfileFormValues>,
) {
  form.setFields(
    [
      ["userName", staff.userName],
      ["fullName", staff.fullName],
      ["position", staff.position ?? undefined],
      ["department", staff.department ?? undefined],
      ["email", staff.email],
      ["phone", staff.phone ?? undefined],
    ].map(([name, value]) => ({
      name: name as keyof StaffProfileFormValues,
      value,
      touched: false,
      errors: [],
    })),
  );
}

function passwordConfirmationRule(dependency: string) {
  return ({ getFieldValue }: { getFieldValue: (name: string) => unknown }) => ({
    validator(_: unknown, value: string | undefined) {
      return value === getFieldValue(dependency)
        ? Promise.resolve()
        : Promise.reject(new Error("Mật khẩu xác nhận không khớp."));
    },
  });
}

function normalizeOptional(value: string | undefined): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0 ? null : normalized;
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

function readNavigationSuccess(state: unknown): string | null {
  if (
    typeof state === "object"
    && state !== null
    && "success" in state
    && typeof state.success === "string"
  ) {
    return state.success;
  }

  return null;
}
