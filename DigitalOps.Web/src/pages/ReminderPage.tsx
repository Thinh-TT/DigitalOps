import {
  Alert,
  Button,
  Card,
  Empty,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  type TableProps,
} from "antd";
import { CheckOutlined, ReloadOutlined } from "@ant-design/icons";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useAuth } from "../shared/auth/auth-context";
import type { PagedResponse } from "../shared/api/types";
import { getStaffList } from "../shared/staff/staff-service";
import type { StaffResponse } from "../shared/staff/types";
import { useReminderBadge } from "../shared/reminders/reminder-badge-context";
import {
  getReminders,
  markReminderRead,
} from "../shared/reminders/reminder-service";
import type {
  ReminderDeliveryStatus,
  ReminderKind,
  ReminderResponse,
} from "../shared/reminders/types";

const deliveryStatusOptions = [
  { value: "", label: "Tất cả trạng thái" },
  { value: "Unread", label: "Chưa đọc" },
  { value: "Read", label: "Đã đọc" },
];

export function ReminderPage() {
  const { currentUser } = useAuth();
  const { refreshUnreadCount } = useReminderBadge();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const isAdministrator = currentUser?.roles.includes("Administrator") ?? false;
  const [reminders, setReminders] = useState<PagedResponse<ReminderResponse> | null>(
    null,
  );
  const [staff, setStaff] = useState<StaffResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [staffLoading, setStaffLoading] = useState(isAdministrator);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [staffErrorMessage, setStaffErrorMessage] = useState<string | null>(null);
  const [markingId, setMarkingId] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);

  const deliveryStatus = parseDeliveryStatus(searchParams.get("deliveryStatus"));
  const recipientStaffId = isAdministrator
    ? searchParams.get("recipientStaffId") ?? undefined
    : undefined;
  const page = parsePositiveInteger(searchParams.get("page"), 1);
  const pageSize = parsePageSize(searchParams.get("pageSize"));

  const refreshList = useCallback(() => setRevision((value) => value + 1), []);

  useEffect(() => {
    let active = true;

    void Promise.resolve()
      .then(() => {
        if (active) {
          setLoading(true);
          setErrorMessage(null);
        }

        return getReminders({
          deliveryStatus,
          recipientStaffId,
          page,
          pageSize,
        });
      })
      .then(async (result) => {
        if (!active) {
          return;
        }

        setReminders(result);
        await refreshUnreadCount();
      })
      .catch((error: unknown) => {
        if (active) {
          setErrorMessage(getErrorMessage(error, "Không thể tải danh sách thông báo."));
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [deliveryStatus, page, pageSize, recipientStaffId, refreshUnreadCount, revision]);

  useEffect(() => {
    if (!isAdministrator) {
      return;
    }

    let active = true;

    void Promise.resolve()
      .then(() => {
        if (active) {
          setStaffLoading(true);
          setStaffErrorMessage(null);
        }

        return getAllStaff();
      })
      .then((result) => {
        if (active) {
          setStaff(result);
        }
      })
      .catch((error: unknown) => {
        if (active) {
          setStaffErrorMessage(getErrorMessage(error, "Không thể tải danh sách Staff."));
        }
      })
      .finally(() => {
        if (active) {
          setStaffLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [isAdministrator]);

  const updateQuery = useCallback(
    (changes: Record<string, string | undefined>, resetPage = true) => {
      const next = new URLSearchParams(searchParams);

      for (const [name, value] of Object.entries(changes)) {
        if (value === undefined || value === "") {
          next.delete(name);
        } else {
          next.set(name, value);
        }
      }

      if (resetPage) {
        next.delete("page");
      }

      setSearchParams(next);
    },
    [searchParams, setSearchParams],
  );

  const handleMarkRead = useCallback(async (id: string) => {
    setMarkingId(id);
    setErrorMessage(null);

    try {
      await markReminderRead(id);
      await refreshUnreadCount();
      refreshList();
    } catch (error) {
      setErrorMessage(getErrorMessage(error, "Không thể đánh dấu thông báo đã đọc."));
    } finally {
      setMarkingId(null);
    }
  }, [refreshList, refreshUnreadCount]);

  const columns = useMemo<TableProps<ReminderResponse>["columns"]>(
    () => [
      {
        title: "Văn bản đến",
        dataIndex: "referenceNumber",
        key: "referenceNumber",
        width: 180,
        render: (referenceNumber: string, reminder: ReminderResponse) => (
          <Button
            type="link"
            className="reminder-document-link"
            onClick={() => navigate(`/incoming-documents/${reminder.incomingDocumentId}`)}
          >
            {referenceNumber}
          </Button>
        ),
      },
      {
        title: "Trích yếu",
        dataIndex: "summary",
        key: "summary",
      },
      {
        title: "Loại nhắc",
        dataIndex: "reminderKind",
        key: "reminderKind",
        width: 140,
        render: (kind: ReminderKind) => <ReminderKindTag kind={kind} />,
      },
      {
        title: "Ngày nhắc",
        dataIndex: "reminderDate",
        key: "reminderDate",
        width: 130,
        render: formatDate,
      },
      {
        title: "Trạng thái",
        dataIndex: "deliveryStatus",
        key: "deliveryStatus",
        width: 125,
        render: (status: ReminderDeliveryStatus) => (
          <ReminderDeliveryTag status={status} />
        ),
      },
      {
        title: "Thời điểm tạo",
        dataIndex: "createdAt",
        key: "createdAt",
        width: 180,
        render: formatDateTime,
      },
      {
        title: "Thao tác",
        key: "actions",
        width: 160,
        render: (_, reminder: ReminderResponse) =>
          reminder.deliveryStatus === "Unread" ? (
            <Button
              icon={<CheckOutlined />}
              loading={markingId === reminder.id}
              onClick={() => void handleMarkRead(reminder.id)}
            >
              Đánh dấu đã đọc
            </Button>
          ) : null,
      },
    ],
    [handleMarkRead, markingId, navigate],
  );

  const recipientOptions = [
    { value: "", label: "Thông báo của tôi" },
    ...staff.map((item) => ({
      value: item.id,
      label: `${item.fullName}${item.isActive ? "" : " (Ngừng hoạt động)"}`,
    })),
  ];

  return (
    <section aria-labelledby="page-title">
      <Space orientation="vertical" size="large" className="page-stack">
        <div className="page-heading-row">
          <div>
            <Typography.Text type="secondary">SCR-010</Typography.Text>
            <Typography.Title id="page-title" level={2}>
              Thông báo
            </Typography.Title>
            <Typography.Paragraph type="secondary">
              Nhắc hạn xử lý văn bản đến của tài khoản.
            </Typography.Paragraph>
          </div>
          <Button icon={<ReloadOutlined />} onClick={refreshList} loading={loading}>
            Tải lại
          </Button>
        </div>

        {errorMessage === null ? null : (
          <Alert type="error" showIcon message={errorMessage} />
        )}
        {staffErrorMessage === null ? null : (
          <Alert type="warning" showIcon message={staffErrorMessage} />
        )}

        <Card>
          <Space wrap className="reminder-filters">
            <Select
              aria-label="Trạng thái thông báo"
              className="reminder-status-filter"
              value={deliveryStatus ?? ""}
              options={deliveryStatusOptions}
              onChange={(value: string) =>
                updateQuery({ deliveryStatus: value || undefined })
              }
            />
            {!isAdministrator ? null : (
              <Select
                aria-label="Người nhận thông báo"
                className="reminder-recipient-filter"
                value={recipientStaffId ?? ""}
                loading={staffLoading}
                options={recipientOptions}
                onChange={(value: string) =>
                  updateQuery({ recipientStaffId: value || undefined })
                }
              />
            )}
          </Space>
        </Card>

        <Card className="reminder-table-card">
          <Table<ReminderResponse>
            rowKey="id"
            columns={columns}
            dataSource={reminders?.items ?? []}
            loading={loading}
            scroll={{ x: 1080 }}
            locale={{ emptyText: <Empty description="Chưa có thông báo phù hợp." /> }}
            pagination={{
              current: reminders?.page ?? page,
              pageSize: reminders?.pageSize ?? pageSize,
              total: reminders?.totalCount ?? 0,
              showSizeChanger: true,
              pageSizeOptions: ["20", "50", "100"],
              showTotal: (total, range) => `${range[0]}-${range[1]} / ${total}`,
              onChange: (nextPage, nextPageSize) => {
                updateQuery(
                  {
                    page: String(nextPage),
                    pageSize: String(nextPageSize),
                  },
                  false,
                );
              },
            }}
          />
        </Card>
      </Space>
    </section>
  );
}

function ReminderKindTag({ kind }: { kind: ReminderKind }) {
  const config: Record<ReminderKind, { color: string; label: string }> = {
    BeforeDeadline: { color: "gold", label: "Sắp đến hạn" },
    DueDate: { color: "orange", label: "Đến hạn" },
    Overdue: { color: "red", label: "Quá hạn" },
  };

  return <Tag color={config[kind].color}>{config[kind].label}</Tag>;
}

function ReminderDeliveryTag({ status }: { status: ReminderDeliveryStatus }) {
  return status === "Unread" ? (
    <Tag color="blue">Chưa đọc</Tag>
  ) : (
    <Tag color="default">Đã đọc</Tag>
  );
}

async function getAllStaff(): Promise<StaffResponse[]> {
  const firstPage = await getStaffList({ page: 1, pageSize: 100 });
  const remainingPages = await Promise.all(
    Array.from({ length: Math.max(0, firstPage.totalPages - 1) }, (_, index) =>
      getStaffList({ page: index + 2, pageSize: 100 }),
    ),
  );

  return [
    ...firstPage.items,
    ...remainingPages.flatMap((result) => result.items),
  ];
}

function parseDeliveryStatus(value: string | null): ReminderDeliveryStatus | undefined {
  return value === "Unread" || value === "Read" ? value : undefined;
}

function parsePositiveInteger(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function parsePageSize(value: string | null): number {
  const parsed = parsePositiveInteger(value, 20);
  return parsed <= 100 ? parsed : 20;
}

function formatDate(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("vi-VN");
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : fallback;
}
