import {
  ArrowLeftOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  SearchOutlined,
  StopOutlined,
  UploadOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Empty,
  Form,
  Input,
  Modal,
  Result,
  Select,
  Space,
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
  useSearchParams,
} from "react-router";
import { ApiError } from "../shared/api/api-client";
import type { PagedResponse } from "../shared/api/types";
import {
  createMember,
  deactivateMember,
  getMember,
  getMembers,
  updateMember,
} from "../shared/members/member-service";
import type {
  MemberCreateRequest,
  MemberGender,
  MemberResponse,
  MemberStatus,
  MemberUpdateRequest,
} from "../shared/members/types";
import { getOutgoingDocuments } from "../shared/outgoing-documents/outgoing-document-service";
import type { OutgoingDocumentResponse } from "../shared/outgoing-documents/types";

const genderOptions: { value: MemberGender; label: string }[] = [
  { value: "Male", label: "Nam" },
  { value: "Female", label: "Nữ" },
  { value: "Other", label: "Khác" },
];

const statusOptions: { value: MemberStatus; label: string }[] = [
  { value: "Active", label: "Đang hoạt động" },
  { value: "Inactive", label: "Ngừng hoạt động" },
];

interface MemberFormValues {
  fullName: string;
  dateOfBirth?: string;
  gender?: MemberGender;
  address?: string;
  phone?: string;
  email?: string;
  position?: string;
  joinDate?: string;
  notes?: string;
}

export function MemberListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const q = searchParams.get("q")?.trim() ?? "";
  const status = parseStatus(searchParams.get("status"));
  const page = parsePositiveInteger(searchParams.get("page"), 1);
  const pageSize = parsePageSize(searchParams.get("pageSize"));
  const [draftFilters, setDraftFilters] = useState(() => ({
    sourceQuery: q,
    sourceStatus: status,
    query: q,
    status,
  }));
  const [data, setData] =
    useState<PagedResponse<MemberResponse> | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [reloadVersion, setReloadVersion] = useState(0);

  if (
    draftFilters.sourceQuery !== q
    || draftFilters.sourceStatus !== status
  ) {
    setDraftFilters({
      sourceQuery: q,
      sourceStatus: status,
      query: q,
      status,
    });
  }

  const draftQuery = draftFilters.query;
  const draftStatus = draftFilters.status;

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
        const response = await getMembers({
          q: q || undefined,
          status,
          page,
          pageSize,
        });
        if (!ignored) {
          setData(response);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(
            getErrorMessage(
              error,
              "Không thể tải danh sách hội viên. Vui lòng thử lại.",
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
  }, [page, pageSize, q, reloadVersion, status]);

  const columns: TableProps<MemberResponse>["columns"] = [
    {
      title: "Họ và tên",
      dataIndex: "fullName",
      key: "fullName",
    },
    {
      title: "Liên hệ",
      key: "contact",
      render: (_, member) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text>{member.phone ?? "—"}</Typography.Text>
          {member.email !== null && (
            <Typography.Text type="secondary">
              {member.email}
            </Typography.Text>
          )}
        </Space>
      ),
    },
    {
      title: "Chức vụ",
      dataIndex: "position",
      key: "position",
      render: (position: string | null) => position ?? "—",
    },
    {
      title: "Ngày gia nhập",
      dataIndex: "joinDate",
      key: "joinDate",
      render: (joinDate: string | null) => formatDate(joinDate),
    },
    {
      title: "Trạng thái",
      dataIndex: "status",
      key: "status",
      render: (memberStatus: MemberStatus) => (
        <MemberStatusTag status={memberStatus} />
      ),
    },
    {
      title: "Thao tác",
      key: "action",
      render: (_, member) => (
        <Button
          type="link"
          onClick={() =>
            navigate(`/members/${member.id}`, {
              state: { returnTo: `${location.pathname}${location.search}` },
            })
          }
        >
          Xem
        </Button>
      ),
    },
  ];

  const applyFilters = () => {
    setSearchParams(
      createListSearchParams({
        q: draftQuery,
        status: draftStatus,
        page: 1,
        pageSize,
      }),
    );
  };

  const clearFilters = () => {
    setDraftFilters({
      sourceQuery: "",
      sourceStatus: undefined,
      query: "",
      status: undefined,
    });
    setSearchParams(createListSearchParams({ page: 1, pageSize }));
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Hội viên</Typography.Title>
          <Typography.Text type="secondary">
            Tra cứu và quản lý hồ sơ hội viên đã số hóa.
          </Typography.Text>
        </div>
        <Space wrap>
          <Button
            icon={<UploadOutlined />}
            onClick={() => navigate("/members/import")}
          >
            Import Excel
          </Button>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => navigate("/members/new")}
          >
            Tạo hội viên
          </Button>
        </Space>
      </div>

      <Card>
        <Space wrap>
          <Input
            className="member-search-input"
            aria-label="Từ khóa hội viên"
            placeholder="Họ tên, điện thoại hoặc email"
            value={draftQuery}
            onChange={(event) =>
              setDraftFilters((current) => ({
                ...current,
                query: event.target.value,
              }))
            }
            onPressEnter={applyFilters}
          />
          <Select<MemberStatus>
            className="member-status-filter"
            aria-label="Trạng thái hội viên"
            allowClear
            placeholder="Tất cả trạng thái"
            options={statusOptions}
            value={draftStatus}
            onChange={(nextStatus) =>
              setDraftFilters((current) => ({
                ...current,
                status: nextStatus,
              }))
            }
          />
          <Button
            type="primary"
            icon={<SearchOutlined />}
            onClick={applyFilters}
          >
            Tìm
          </Button>
          <Button onClick={clearFilters}>Xóa bộ lọc</Button>
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
          locale={{
            emptyText: (
              <Empty description="Không có hội viên phù hợp.">
                {(q.length > 0 || status !== undefined) && (
                  <Button onClick={clearFilters}>Xóa bộ lọc</Button>
                )}
              </Empty>
            ),
          }}
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
            setSearchParams(
              createListSearchParams({
                q,
                status,
                page: pagination.current ?? 1,
                pageSize: pagination.pageSize ?? 20,
              }),
            );
          }}
        />
      </Card>
    </Space>
  );
}

export function MemberCreatePage() {
  const navigate = useNavigate();
  const [form] = Form.useForm<MemberFormValues>();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (values: MemberFormValues) => {
    setSubmitting(true);
    setErrorMessage(null);

    const request: MemberCreateRequest = {
      fullName: values.fullName.trim(),
      dateOfBirth: normalizeOptional(values.dateOfBirth),
      gender: values.gender ?? null,
      address: normalizeOptional(values.address),
      phone: normalizeOptional(values.phone),
      email: normalizeOptional(values.email),
      position: normalizeOptional(values.position),
      joinDate: normalizeOptional(values.joinDate),
      notes: normalizeOptional(values.notes),
    };

    try {
      const created = await createMember(request);
      navigate(`/members/${created.id}`, {
        replace: true,
        state: {
          success: "Đã tạo hội viên.",
          returnTo: "/members",
        },
      });
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể tạo hội viên. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title="Tạo hội viên"
        description="Hồ sơ mới được tạo ở trạng thái đang hoạt động."
        returnTo="/members"
      />
      {errorMessage !== null && (
        <Alert type="error" showIcon title={errorMessage} />
      )}
      <Card>
        <MemberForm
          form={form}
          submitting={submitting}
          submitLabel="Tạo hội viên"
          onFinish={handleSubmit}
          onCancel={() => navigate("/members")}
        />
      </Card>
    </Space>
  );
}

export function MemberDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<MemberFormValues>();
  const [member, setMember] = useState<MemberResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [statusSubmitting, setStatusSubmitting] = useState(false);
  const [deactivateOpen, setDeactivateOpen] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(
    readNavigationSuccess(location.state),
  );
  const [reloadVersion, setReloadVersion] = useState(0);
  const [relatedOutgoing, setRelatedOutgoing] = useState<OutgoingDocumentResponse[]>([]);
  const returnTo = readReturnTo(location.state);

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
        const response = await getMember(id);
        if (!ignored) {
          setMember(response);
          setMemberForm(response, form);
        }
      } catch (error) {
        if (ignored) {
          return;
        }

        if (error instanceof ApiError && error.status === 404) {
          setNotFound(true);
        } else {
          setErrorMessage(
            getErrorMessage(
              error,
              "Không thể tải hồ sơ hội viên. Vui lòng thử lại.",
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
  }, [form, id, reloadVersion]);

  useEffect(() => {
    let ignored = false;
    void getOutgoingDocuments({ relatedMemberId: id, pageSize: 100 })
      .then(response => { if (!ignored) setRelatedOutgoing(response.items); })
      .catch(() => { if (!ignored) setRelatedOutgoing([]); });
    return () => { ignored = true; };
  }, [id, reloadVersion]);

  if (notFound) {
    return (
      <Result
        status="404"
        title="Không tìm thấy hội viên"
        subTitle="Hồ sơ có thể không tồn tại hoặc đã thay đổi."
        extra={<Button onClick={() => navigate(returnTo)}>Về danh sách</Button>}
      />
    );
  }

  const handleSubmit = async (values: MemberFormValues) => {
    if (member === null) {
      return;
    }

    const request = createPatchRequest(form, values);
    if (Object.keys(request).length === 0) {
      setSuccessMessage("Không có thay đổi cần lưu.");
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const updated = await updateMember(member.id, request);
      setMember(updated);
      setMemberForm(updated, form);
      setSuccessMessage("Đã cập nhật hồ sơ hội viên.");
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(
            error,
            "Không thể cập nhật hội viên. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const changeStatus = async () => {
    if (member === null) {
      return;
    }

    setStatusSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const updated =
        member.status === "Active"
          ? await deactivateMember(member.id)
          : await updateMember(member.id, { status: "Active" });
      setMember(updated);
      setMemberForm(updated, form);
      setDeactivateOpen(false);
      setSuccessMessage(
        updated.status === "Active"
          ? "Đã kích hoạt lại hội viên."
          : "Đã ngừng hoạt động hội viên.",
      );
    } catch (error) {
      setErrorMessage(
        getErrorMessage(
          error,
          member.status === "Active"
            ? "Không thể ngừng hoạt động hội viên."
            : "Không thể kích hoạt lại hội viên.",
        ),
      );
    } finally {
      setStatusSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title={member?.fullName ?? "Chi tiết hội viên"}
        description="Cập nhật hồ sơ và trạng thái hoạt động."
        returnTo={returnTo}
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
            member === null ? (
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

      <Card
        loading={loading}
        title="Hồ sơ hội viên"
        extra={
          member === null ? undefined : (
            <MemberStatusTag status={member.status} />
          )
        }
      >
        {member !== null && (
          <MemberForm
            form={form}
            submitting={submitting}
            submitLabel="Lưu hồ sơ"
            onFinish={handleSubmit}
            statusAction={
              member.status === "Active" ? (
                <Button
                  danger
                  icon={<StopOutlined />}
                  loading={statusSubmitting}
                  onClick={() => setDeactivateOpen(true)}
                >
                  Ngừng hoạt động
                </Button>
              ) : (
                <Button
                  loading={statusSubmitting}
                  onClick={() => void changeStatus()}
                >
                  Kích hoạt lại
                </Button>
              )
            }
          />
        )}
      </Card>

      <Card title="Văn bản đi liên quan">
        {relatedOutgoing.length === 0 ? (
          <Empty description="Chưa có văn bản đi liên quan." />
        ) : (
          <Table<OutgoingDocumentResponse>
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={relatedOutgoing}
            columns={[
              { title: "Tiêu đề", dataIndex: "title", ellipsis: true },
              { title: "Mẫu", render: (_, item) => item.template.name },
              { title: "Trạng thái", dataIndex: "status" },
              { title: "", render: (_, item) => <Button type="link" onClick={() => navigate(`/outgoing-documents/${item.id}`)}>Xem</Button> },
            ]}
          />
        )}
      </Card>

      <Modal
        title="Xác nhận ngừng hoạt động hội viên"
        open={deactivateOpen}
        okText="Ngừng hoạt động"
        okButtonProps={{ danger: true }}
        confirmLoading={statusSubmitting}
        cancelText="Hủy"
        onCancel={() => setDeactivateOpen(false)}
        onOk={() => void changeStatus()}
      >
        Hội viên vẫn được giữ lại để tra cứu lịch sử và không xuất hiện trong
        bộ chọn cho văn bản mới.
      </Modal>
    </Space>
  );
}

function MemberForm({
  form,
  submitting,
  submitLabel,
  statusAction,
  onFinish,
  onCancel,
}: {
  form: FormInstance<MemberFormValues>;
  submitting: boolean;
  submitLabel: string;
  statusAction?: React.ReactNode;
  onFinish: (values: MemberFormValues) => void | Promise<void>;
  onCancel?: () => void;
}) {
  return (
    <Form
      className="member-form"
      form={form}
      layout="vertical"
      requiredMark
      onFinish={(values) => void onFinish(values)}
    >
      <div className="member-form-grid">
        <Form.Item
          name="fullName"
          label="Họ và tên"
          rules={[
            {
              required: true,
              whitespace: true,
              message: "Vui lòng nhập họ và tên.",
            },
            { max: 200, message: "Họ và tên không được vượt quá 200 ký tự." },
          ]}
        >
          <Input autoComplete="name" />
        </Form.Item>
        <Form.Item name="gender" label="Giới tính">
          <Select allowClear options={genderOptions} placeholder="Chọn giới tính" />
        </Form.Item>
        <Form.Item name="dateOfBirth" label="Ngày sinh">
          <Input type="date" />
        </Form.Item>
        <Form.Item name="joinDate" label="Ngày gia nhập">
          <Input type="date" />
        </Form.Item>
        <Form.Item
          name="phone"
          label="Số điện thoại"
          rules={[
            { max: 30, message: "Số điện thoại không được vượt quá 30 ký tự." },
            {
              pattern: /^[0-9+().\-\s]*$/,
              message: "Số điện thoại không đúng định dạng.",
            },
          ]}
        >
          <Input autoComplete="tel" />
        </Form.Item>
        <Form.Item
          name="email"
          label="Email"
          rules={[
            { type: "email", message: "Email không đúng định dạng." },
            { max: 254, message: "Email không được vượt quá 254 ký tự." },
          ]}
        >
          <Input autoComplete="email" />
        </Form.Item>
        <Form.Item
          name="position"
          label="Chức vụ"
          rules={[
            { max: 150, message: "Chức vụ không được vượt quá 150 ký tự." },
          ]}
        >
          <Input />
        </Form.Item>
      </div>
      <Form.Item name="address" label="Địa chỉ">
        <Input.TextArea autoSize={{ minRows: 2, maxRows: 5 }} />
      </Form.Item>
      <Form.Item name="notes" label="Ghi chú">
        <Input.TextArea autoSize={{ minRows: 3, maxRows: 8 }} />
      </Form.Item>
      <Space wrap>
        <Button
          type="primary"
          htmlType="submit"
          icon={<SaveOutlined />}
          loading={submitting}
        >
          {submitLabel}
        </Button>
        {statusAction}
        {onCancel !== undefined && (
          <Button disabled={submitting} onClick={onCancel}>
            Hủy
          </Button>
        )}
      </Space>
    </Form>
  );
}

function MemberStatusTag({ status }: { status: MemberStatus }) {
  return (
    <Tag color={status === "Active" ? "success" : "default"}>
      {status === "Active" ? "Đang hoạt động" : "Ngừng hoạt động"}
    </Tag>
  );
}

function PageBackHeading({
  title,
  description,
  returnTo,
}: {
  title: string;
  description: string;
  returnTo: string;
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
        onClick={() => navigate(returnTo)}
      >
        Về danh sách
      </Button>
    </div>
  );
}

function createPatchRequest(
  form: FormInstance<MemberFormValues>,
  values: MemberFormValues,
): MemberUpdateRequest {
  const request: MemberUpdateRequest = {};

  if (form.isFieldTouched("fullName")) {
    request.fullName = values.fullName.trim();
  }
  if (form.isFieldTouched("dateOfBirth")) {
    request.dateOfBirth = normalizeOptional(values.dateOfBirth);
  }
  if (form.isFieldTouched("gender")) {
    request.gender = values.gender ?? null;
  }
  if (form.isFieldTouched("address")) {
    request.address = normalizeOptional(values.address);
  }
  if (form.isFieldTouched("phone")) {
    request.phone = normalizeOptional(values.phone);
  }
  if (form.isFieldTouched("email")) {
    request.email = normalizeOptional(values.email);
  }
  if (form.isFieldTouched("position")) {
    request.position = normalizeOptional(values.position);
  }
  if (form.isFieldTouched("joinDate")) {
    request.joinDate = normalizeOptional(values.joinDate);
  }
  if (form.isFieldTouched("notes")) {
    request.notes = normalizeOptional(values.notes);
  }

  return request;
}

function setMemberForm(
  member: MemberResponse,
  form: FormInstance<MemberFormValues>,
) {
  form.setFields(
    [
      ["fullName", member.fullName],
      ["dateOfBirth", member.dateOfBirth ?? undefined],
      ["gender", member.gender ?? undefined],
      ["address", member.address ?? undefined],
      ["phone", member.phone ?? undefined],
      ["email", member.email ?? undefined],
      ["position", member.position ?? undefined],
      ["joinDate", member.joinDate ?? undefined],
      ["notes", member.notes ?? undefined],
    ].map(([name, value]) => ({
      name: name as keyof MemberFormValues,
      value,
      touched: false,
      errors: [],
    })),
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

function createListSearchParams({
  q,
  status,
  page,
  pageSize,
}: {
  q?: string;
  status?: MemberStatus;
  page: number;
  pageSize: number;
}) {
  const params = new URLSearchParams();
  const normalizedQuery = q?.trim() ?? "";

  if (normalizedQuery.length > 0) {
    params.set("q", normalizedQuery);
  }
  if (status !== undefined) {
    params.set("status", status);
  }
  if (page > 1) {
    params.set("page", String(page));
  }
  if (pageSize !== 20) {
    params.set("pageSize", String(pageSize));
  }

  return params;
}

function parseStatus(value: string | null): MemberStatus | undefined {
  return value === "Active" || value === "Inactive" ? value : undefined;
}

function parsePositiveInteger(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : fallback;
}

function parsePageSize(value: string | null): number {
  const parsed = parsePositiveInteger(value, 20);
  return parsed <= 100 ? parsed : 20;
}

function normalizeOptional(value: string | undefined): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0 ? null : normalized;
}

function formatDate(value: string | null): string {
  if (value === null) {
    return "—";
  }

  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}/${month}/${year}` : value;
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

function readReturnTo(state: unknown): string {
  if (
    typeof state === "object"
    && state !== null
    && "returnTo" in state
    && typeof state.returnTo === "string"
    && state.returnTo.startsWith("/members")
  ) {
    return state.returnTo;
  }

  return "/members";
}
