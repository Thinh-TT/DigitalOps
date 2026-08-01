import {
  ArrowLeftOutlined,
  CheckCircleOutlined,
  DeleteOutlined,
  DownloadOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  SearchOutlined,
  UploadOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Form,
  Input,
  Popconfirm,
  Result,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  Upload,
  type FormInstance,
  type TableProps,
  type UploadProps,
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
import { useAuth } from "../shared/auth/auth-context";
import { getAllDocumentTypes } from "../shared/document-catalog/document-catalog-service";
import type { DocumentTypeResponse } from "../shared/document-catalog/types";
import {
  completeIncomingDocument,
  createIncomingDocument,
  deleteAttachment,
  downloadAttachment,
  getIncomingDocument,
  getIncomingDocuments,
  uploadIncomingAttachment,
  updateIncomingDocument,
} from "../shared/incoming-documents/incoming-document-service";
import type {
  AttachmentResponse,
  ExtractionStatus,
  IncomingDocumentResponse,
  IncomingDocumentStatus,
  IncomingDocumentUpdateRequest,
  IncomingStaffReference,
} from "../shared/incoming-documents/types";
import { getOutgoingDocuments } from "../shared/outgoing-documents/outgoing-document-service";
import type { OutgoingDocumentResponse } from "../shared/outgoing-documents/types";

interface IncomingDocumentFormValues {
  referenceNumber: string;
  senderOrg: string;
  summary: string;
  receivedDate: string;
  deadline: string;
  documentTypeId: string;
}

interface IncomingFilters {
  q: string;
  documentTypeId?: string;
  status?: IncomingDocumentStatus;
  deadlineFrom?: string;
  deadlineTo?: string;
}

const statusOptions: { value: IncomingDocumentStatus; label: string }[] = [
  { value: "New", label: "Mới tiếp nhận" },
  { value: "InProgress", label: "Đang xử lý" },
  { value: "Overdue", label: "Quá hạn" },
  { value: "Completed", label: "Hoàn tất" },
];

export function IncomingDocumentListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { currentUser } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = readFilters(searchParams);
  const {
    q,
    documentTypeId,
    status,
    deadlineFrom,
    deadlineTo,
  } = filters;
  const page = parsePositiveInteger(searchParams.get("page"), 1);
  const pageSize = parsePageSize(searchParams.get("pageSize"));
  const filterKey = JSON.stringify(filters);
  const [draft, setDraft] = useState<IncomingFilters>(filters);
  const [sourceFilterKey, setSourceFilterKey] = useState(filterKey);
  const [data, setData] =
    useState<PagedResponse<IncomingDocumentResponse> | null>(null);
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const isClerk = currentUser?.roles.includes("Clerk") ?? false;

  if (sourceFilterKey !== filterKey) {
    setSourceFilterKey(filterKey);
    setDraft(filters);
  }

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
        const [response, types] = await Promise.all([
          getIncomingDocuments({
            q,
            documentTypeId,
            status,
            deadlineFrom,
            deadlineTo,
            page,
            pageSize,
          }),
          getAllDocumentTypes(),
        ]);
        if (!ignored) {
          setData(response);
          setDocumentTypes(types);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(getErrorMessage(error, "Không thể tải danh sách văn bản đến."));
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
  }, [
    deadlineFrom,
    deadlineTo,
    documentTypeId,
    page,
    pageSize,
    q,
    reloadVersion,
    status,
  ]);

  const returnTo = `${location.pathname}${location.search}`;
  const columns: TableProps<IncomingDocumentResponse>["columns"] = [
    {
      title: "Số, ký hiệu",
      dataIndex: "referenceNumber",
      key: "referenceNumber",
      width: 150,
    },
    {
      title: "Trích yếu",
      dataIndex: "summary",
      key: "summary",
      ellipsis: true,
    },
    {
      title: "Cơ quan gửi",
      dataIndex: "senderOrg",
      key: "senderOrg",
      width: 190,
    },
    {
      title: "Loại",
      key: "documentType",
      width: 150,
      render: (_, item) => item.documentType.name,
    },
    {
      title: "Ngày nhận",
      dataIndex: "receivedDate",
      key: "receivedDate",
      width: 120,
      render: formatDate,
    },
    {
      title: "Hạn xử lý",
      dataIndex: "deadline",
      key: "deadline",
      width: 120,
      render: formatDate,
    },
    {
      title: "Trạng thái",
      dataIndex: "status",
      key: "status",
      width: 135,
      render: (status: IncomingDocumentStatus) => <IncomingStatusTag status={status} />,
    },
    {
      title: "Thao tác",
      key: "action",
      width: 80,
      render: (_, item) => (
        <Button
          type="link"
          onClick={() => navigate(`/incoming-documents/${item.id}`, { state: { returnTo } })}
        >
          Xem
        </Button>
      ),
    },
  ];

  const applyFilters = () => {
    setSearchParams(createSearchParams(draft, 1, pageSize));
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Văn bản đến</Typography.Title>
          <Typography.Text type="secondary">
            Tra cứu, tiếp nhận và theo dõi tiến độ xử lý văn bản.
          </Typography.Text>
        </div>
        {isClerk && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => navigate("/incoming-documents/new", { state: { returnTo } })}
          >
            Tiếp nhận văn bản
          </Button>
        )}
      </div>

      <Card>
        <Space wrap>
          <Input
            className="incoming-keyword-filter"
            aria-label="Từ khóa văn bản đến"
            allowClear
            maxLength={200}
            placeholder="Số hiệu, nơi gửi, trích yếu"
            value={draft.q}
            onChange={(event) => setDraft((current) => ({ ...current, q: event.target.value }))}
            onPressEnter={applyFilters}
          />
          <Select
            className="incoming-type-filter"
            aria-label="Loại văn bản đến"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Tất cả loại văn bản"
            value={draft.documentTypeId}
            options={documentTypes.map((type) => ({
              value: type.id,
              label: `${type.code} — ${type.name}${type.isActive ? "" : " (Ngừng)"}`,
            }))}
            onChange={(value) => setDraft((current) => ({ ...current, documentTypeId: value }))}
          />
          <Select
            className="incoming-status-filter"
            aria-label="Trạng thái văn bản đến"
            allowClear
            placeholder="Tất cả trạng thái"
            value={draft.status}
            options={statusOptions}
            onChange={(value) => setDraft((current) => ({ ...current, status: value }))}
          />
          <Input
            type="date"
            aria-label="Hạn xử lý từ"
            value={draft.deadlineFrom}
            onChange={(event) => setDraft((current) => ({ ...current, deadlineFrom: event.target.value || undefined }))}
          />
          <Input
            type="date"
            aria-label="Hạn xử lý đến"
            value={draft.deadlineTo}
            onChange={(event) => setDraft((current) => ({ ...current, deadlineTo: event.target.value || undefined }))}
          />
          <Button type="primary" icon={<SearchOutlined />} onClick={applyFilters}>
            Lọc
          </Button>
          <Button
            onClick={() => {
              setDraft({ q: "" });
              setSearchParams(createSearchParams({ q: "" }, 1, pageSize));
            }}
          >
            Xóa bộ lọc
          </Button>
        </Space>
      </Card>

      {errorMessage !== null && (
        <Alert
          type="error"
          showIcon
          title={errorMessage}
          action={<Button size="small" icon={<ReloadOutlined />} onClick={() => setReloadVersion((value) => value + 1)}>Thử lại</Button>}
        />
      )}

      <Card>
        <Table
          rowKey="id"
          loading={loading}
          columns={columns}
          dataSource={data?.items ?? []}
          locale={{ emptyText: <Empty description="Chưa có văn bản đến phù hợp." /> }}
          pagination={{
            current: data?.page ?? page,
            pageSize: data?.pageSize ?? pageSize,
            total: data?.totalCount ?? 0,
            showSizeChanger: true,
            pageSizeOptions: [10, 20, 50, 100],
            onChange: (nextPage, nextPageSize) =>
              setSearchParams(createSearchParams(filters, nextPageSize === pageSize ? nextPage : 1, nextPageSize)),
          }}
          scroll={{ x: 1180 }}
        />
      </Card>
    </Space>
  );
}

export function IncomingDocumentCreatePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<IncomingDocumentFormValues>();
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const returnTo = readReturnTo(location.state);

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      try {
        const types = await getAllDocumentTypes(true);
        if (!ignored) {
          setDocumentTypes(types);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(getErrorMessage(error, "Không thể tải loại văn bản."));
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
  }, []);

  const handleSubmit = async (values: IncomingDocumentFormValues) => {
    if (!datesAreValid(values.receivedDate, values.deadline, form)) {
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    try {
      const created = await createIncomingDocument(normalizeFormValues(values));
      navigate(`/incoming-documents/${created.id}`, {
        state: { returnTo, success: "Đã tiếp nhận văn bản đến." },
      });
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(getErrorMessage(error, "Không thể tiếp nhận văn bản đến."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title="Tiếp nhận văn bản đến"
        description="Nhập thông tin hành chính và hạn xử lý ban đầu."
        returnTo={returnTo}
      />
      {errorMessage !== null && <Alert type="error" showIcon title={errorMessage} />}
      <Card loading={loading}>
        <IncomingDocumentForm
          form={form}
          documentTypes={documentTypes}
          submitting={submitting}
          submitLabel="Tiếp nhận văn bản"
          onFinish={handleSubmit}
        />
      </Card>
    </Space>
  );
}

export function IncomingDocumentDetailPage() {
  const { id = "" } = useParams();
  const { currentUser } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<IncomingDocumentFormValues>();
  const [document, setDocument] = useState<IncomingDocumentResponse | null>(null);
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [completing, setCompleting] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState(readSuccess(location.state));
  const [reloadVersion, setReloadVersion] = useState(0);
  const [relatedOutgoing, setRelatedOutgoing] = useState<OutgoingDocumentResponse[]>([]);
  const returnTo = readReturnTo(location.state);
  const isClerk = currentUser?.roles.includes("Clerk") ?? false;
  const editable = isClerk && document?.status !== "Completed";

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      setNotFound(false);
      setErrorMessage(null);
      try {
        const [response, types] = await Promise.all([
          getIncomingDocument(id),
          getAllDocumentTypes(),
        ]);
        if (!ignored) {
          setDocument(response);
          setDocumentTypes(types);
          setFormValues(response, form);
        }
      } catch (error) {
        if (!ignored) {
          if (error instanceof ApiError && error.status === 404) {
            setNotFound(true);
          } else {
            setErrorMessage(getErrorMessage(error, "Không thể tải văn bản đến."));
          }
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
    void getOutgoingDocuments({ relatedIncomingDocumentId: id, pageSize: 100 })
      .then(response => { if (!ignored) setRelatedOutgoing(response.items); })
      .catch(() => { if (!ignored) setRelatedOutgoing([]); });
    return () => { ignored = true; };
  }, [id, reloadVersion]);

  if (notFound) {
    return (
      <Result
        status="404"
        title="Không tìm thấy văn bản đến"
        subTitle="Văn bản có thể không tồn tại hoặc đã thay đổi."
        extra={<Button onClick={() => navigate(returnTo)}>Về danh sách</Button>}
      />
    );
  }

  const handleSubmit = async (values: IncomingDocumentFormValues) => {
    if (document === null || !datesAreValid(values.receivedDate, values.deadline, form)) {
      return;
    }

    const request = createPatch(form, values);
    if (Object.keys(request).length === 0) {
      setSuccessMessage("Không có thay đổi cần lưu.");
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      const updated = await updateIncomingDocument(document.id, request);
      setDocument(updated);
      setFormValues(updated, form);
      setSuccessMessage("Đã cập nhật văn bản đến.");
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(getErrorMessage(error, "Không thể cập nhật văn bản đến."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleComplete = async () => {
    if (document === null) {
      return;
    }
    setCompleting(true);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      const updated = await completeIncomingDocument(document.id);
      setDocument(updated);
      setFormValues(updated, form);
      setSuccessMessage("Đã hoàn tất văn bản đến.");
    } catch (error) {
      setErrorMessage(getErrorMessage(error, "Không thể hoàn tất văn bản đến."));
    } finally {
      setCompleting(false);
    }
  };

  const refreshAfterAttachmentConflict = async () => {
    try {
      const latest = await getIncomingDocument(id);
      setDocument(latest);
      setFormValues(latest, form);
    } catch {
      // Keep the attachment error visible; the user can use the page retry flow.
    }
  };

  const handleUpload = async (file: File) => {
    if (document === null) {
      return;
    }

    setUploading(true);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      const attachment = await uploadIncomingAttachment(document.id, file);
      setDocument((current) => current === null
        ? current
        : {
            ...current,
            attachments: sortAttachments([attachment, ...current.attachments]),
          });
      setSuccessMessage("Đã tải file đính kèm lên.");
    } catch (error) {
      setErrorMessage(getAttachmentErrorMessage(error, "Không thể tải file lên."));
      if (error instanceof ApiError && error.status === 409) {
        await refreshAfterAttachmentConflict();
      }
      throw error;
    } finally {
      setUploading(false);
    }
  };

  const handleDownload = async (attachment: AttachmentResponse) => {
    setDownloadingId(attachment.id);
    setErrorMessage(null);
    try {
      const downloaded = await downloadAttachment(attachment.id);
      triggerDownload(
        downloaded.blob,
        downloaded.fileName ?? attachment.fileName,
      );
    } catch (error) {
      setErrorMessage(getErrorMessage(error, "Không thể tải file đính kèm."));
    } finally {
      setDownloadingId(null);
    }
  };

  const handleDelete = async (attachment: AttachmentResponse) => {
    setDeletingId(attachment.id);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      await deleteAttachment(attachment.id);
      setDocument((current) => current === null
        ? current
        : {
            ...current,
            attachments: current.attachments.filter(
              (item) => item.id !== attachment.id,
            ),
          });
      setSuccessMessage("Đã xóa file đính kèm.");
    } catch (error) {
      setErrorMessage(getErrorMessage(error, "Không thể xóa file đính kèm."));
      if (error instanceof ApiError && error.status === 409) {
        await refreshAfterAttachmentConflict();
      }
    } finally {
      setDeletingId(null);
    }
  };

  const uploadProps: UploadProps = {
    accept: ".pdf,.docx,.xlsx,.jpg,.jpeg,.png",
    multiple: false,
    maxCount: 1,
    showUploadList: false,
    disabled: !editable || uploading,
    customRequest: (options) => {
      if (!(options.file instanceof File)) {
        options.onError?.(new Error("File tải lên không hợp lệ."));
        return;
      }

      void handleUpload(options.file).then(
        () => options.onSuccess?.({}, options.file),
        (error: unknown) => options.onError?.(
          error instanceof Error ? error : new Error("Upload failed."),
        ),
      );
    },
  };

  const canComplete = document !== null
    && document.assignedToStaff !== null
    && (document.status === "InProgress" || document.status === "Overdue")
    && (isClerk || document.assignedToStaff.id === currentUser?.staff.id);
  const currentType = documentTypes.find((type) => type.id === document?.documentType.id);

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title={document?.referenceNumber ?? "Chi tiết văn bản đến"}
        description="Thông tin tiếp nhận, điều phối và tiến độ xử lý."
        returnTo={returnTo}
      />
      {successMessage !== null && (
        <Alert type="success" showIcon closable title={successMessage} onClose={() => setSuccessMessage(null)} />
      )}
      {errorMessage !== null && (
        <Alert
          type="error"
          showIcon
          title={errorMessage}
          action={document === null ? <Button size="small" onClick={() => setReloadVersion((value) => value + 1)}>Thử lại</Button> : undefined}
        />
      )}
      {document !== null && currentType?.isActive === false && (
        <Alert
          type="warning"
          showIcon
          title="Loại văn bản hiện tại đã ngừng hoạt động."
          description="Bạn vẫn có thể sửa thông tin khác; nếu đổi loại, chỉ được chọn loại đang hoạt động."
        />
      )}

      <Card
        loading={loading}
        title="Thông tin hành chính"
        extra={document === null ? undefined : <IncomingStatusTag status={document.status} />}
      >
        {document !== null && (
          <IncomingDocumentForm
            form={form}
            documentTypes={documentTypes}
            currentTypeId={document.documentType.id}
            submitting={submitting}
            submitLabel="Lưu thay đổi"
            readOnly={!editable}
            onFinish={handleSubmit}
          />
        )}
      </Card>

      {document !== null && (
        <>
        <div className="incoming-detail-grid">
          <Card title="Điều phối xử lý">
            {document.assignedToStaff === null && document.suggestedStaff === null ? (
              <Empty description="Chưa có gợi ý hoặc phân công xử lý." />
            ) : (
              <Descriptions column={1} size="small">
                <Descriptions.Item label="Nhân sự được gợi ý">
                  {formatStaff(document.suggestedStaff)}
                </Descriptions.Item>
                <Descriptions.Item label="Lý do gợi ý">
                  {document.assignmentSuggestionReason ?? "—"}
                </Descriptions.Item>
                <Descriptions.Item label="Nhân sự xử lý chính">
                  {formatStaff(document.assignedToStaff)}
                </Descriptions.Item>
                <Descriptions.Item label="Người xác nhận">
                  {formatStaff(document.assignmentConfirmedBy)}
                </Descriptions.Item>
                <Descriptions.Item label="Thời điểm xác nhận">
                  {formatDateTime(document.assignmentConfirmedAt)}
                </Descriptions.Item>
              </Descriptions>
            )}
            {canComplete && (
              <Popconfirm
                title="Hoàn tất văn bản đến?"
                description="Trạng thái sẽ chuyển sang Hoàn tất."
                okText="Hoàn tất"
                cancelText="Hủy"
                onConfirm={() => void handleComplete()}
              >
                <Button
                  className="incoming-complete-button"
                  type="primary"
                  icon={<CheckCircleOutlined />}
                  loading={completing}
                >
                  Hoàn tất xử lý
                </Button>
              </Popconfirm>
            )}
          </Card>
          <Card
            title="File đính kèm"
            extra={editable ? (
              <Upload {...uploadProps}>
                <Button icon={<UploadOutlined />} loading={uploading}>
                  Thêm file
                </Button>
              </Upload>
            ) : undefined}
          >
            {document.attachments.length === 0 ? (
              <Empty description="Chưa có file đính kèm." />
            ) : (
              <Table<AttachmentResponse>
                className="attachment-table"
                rowKey="id"
                size="small"
                pagination={false}
                scroll={{ x: 760 }}
                dataSource={document.attachments}
                columns={[
                  {
                    title: "Tên file",
                    dataIndex: "fileName",
                    key: "fileName",
                    ellipsis: true,
                  },
                  {
                    title: "Người tải",
                    key: "uploadedBy",
                    width: 180,
                    render: (_, attachment) => attachment.uploadedBy.fullName,
                  },
                  {
                    title: "Tải lên lúc",
                    dataIndex: "uploadedAt",
                    key: "uploadedAt",
                    width: 170,
                    render: formatDateTime,
                  },
                  {
                    title: "Trích xuất",
                    dataIndex: "extractionStatus",
                    key: "extractionStatus",
                    width: 150,
                    render: (status: ExtractionStatus) => (
                      <ExtractionStatusTag status={status} />
                    ),
                  },
                  {
                    title: "Thao tác",
                    key: "actions",
                    fixed: "right",
                    width: editable ? 160 : 90,
                    render: (_, attachment) => (
                      <Space size="small">
                        <Button
                          type="link"
                          size="small"
                          icon={<DownloadOutlined />}
                          loading={downloadingId === attachment.id}
                          onClick={() => void handleDownload(attachment)}
                        >
                          Tải
                        </Button>
                        {editable && (
                          <Popconfirm
                            title="Xóa file đính kèm?"
                            description="File và metadata sẽ bị xóa khỏi văn bản."
                            okText="Xóa"
                            okButtonProps={{ danger: true }}
                            cancelText="Hủy"
                            onConfirm={() => handleDelete(attachment)}
                          >
                            <Button
                              danger
                              type="link"
                              size="small"
                              icon={<DeleteOutlined />}
                              loading={deletingId === attachment.id}
                            >
                              Xóa
                            </Button>
                          </Popconfirm>
                        )}
                      </Space>
                    ),
                  },
                ]}
              />
            )}
            <Typography.Text className="attachment-hint" type="secondary">
              Hỗ trợ PDF, DOCX, XLSX, JPG, JPEG và PNG; dung lượng do máy chủ kiểm soát.
            </Typography.Text>
          </Card>
        </div>
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
        </>
      )}
    </Space>
  );
}

function IncomingDocumentForm({
  form,
  documentTypes,
  currentTypeId,
  submitting,
  submitLabel,
  readOnly = false,
  onFinish,
}: {
  form: FormInstance<IncomingDocumentFormValues>;
  documentTypes: DocumentTypeResponse[];
  currentTypeId?: string;
  submitting: boolean;
  submitLabel: string;
  readOnly?: boolean;
  onFinish: (values: IncomingDocumentFormValues) => void | Promise<void>;
}) {
  return (
    <Form
      className="incoming-form"
      form={form}
      layout="vertical"
      requiredMark={!readOnly}
      disabled={readOnly}
      onFinish={(values) => void onFinish(values)}
    >
      <div className="incoming-form-grid">
        <Form.Item
          name="referenceNumber"
          label="Số, ký hiệu"
          rules={[
            { required: true, whitespace: true, message: "Vui lòng nhập số, ký hiệu văn bản." },
            { max: 100, message: "Số, ký hiệu không được vượt quá 100 ký tự." },
          ]}
        >
          <Input />
        </Form.Item>
        <Form.Item
          name="senderOrg"
          label="Cơ quan gửi"
          rules={[
            { required: true, whitespace: true, message: "Vui lòng nhập cơ quan gửi." },
            { max: 255, message: "Cơ quan gửi không được vượt quá 255 ký tự." },
          ]}
        >
          <Input />
        </Form.Item>
        <Form.Item
          name="documentTypeId"
          label="Loại văn bản"
          rules={[{ required: true, message: "Vui lòng chọn loại văn bản." }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Chọn loại văn bản"
            options={documentTypes.map((type) => ({
              value: type.id,
              label: `${type.code} — ${type.name}${type.isActive ? "" : " (Ngừng hoạt động)"}`,
              disabled: !type.isActive && type.id !== currentTypeId,
            }))}
          />
        </Form.Item>
        <Form.Item
          name="receivedDate"
          label="Ngày tiếp nhận"
          rules={[{ required: true, message: "Vui lòng nhập ngày tiếp nhận." }]}
        >
          <Input type="date" />
        </Form.Item>
        <Form.Item
          name="deadline"
          label="Hạn xử lý"
          rules={[{ required: true, message: "Vui lòng nhập hạn xử lý." }]}
        >
          <Input type="date" />
        </Form.Item>
      </div>
      <Form.Item
        name="summary"
        label="Trích yếu"
        rules={[{ required: true, whitespace: true, message: "Vui lòng nhập trích yếu văn bản." }]}
      >
        <Input.TextArea autoSize={{ minRows: 4, maxRows: 10 }} />
      </Form.Item>
      {!readOnly && (
        <Button type="primary" htmlType="submit" icon={<SaveOutlined />} loading={submitting}>
          {submitLabel}
        </Button>
      )}
    </Form>
  );
}

function IncomingStatusTag({ status }: { status: IncomingDocumentStatus }) {
  const config: Record<IncomingDocumentStatus, { color: string; label: string }> = {
    New: { color: "blue", label: "Mới tiếp nhận" },
    InProgress: { color: "processing", label: "Đang xử lý" },
    Overdue: { color: "error", label: "Quá hạn" },
    Completed: { color: "success", label: "Hoàn tất" },
  };
  return <Tag color={config[status].color}>{config[status].label}</Tag>;
}

function ExtractionStatusTag({ status }: { status: ExtractionStatus }) {
  const config: Record<ExtractionStatus, { color: string; label: string }> = {
    Pending: { color: "blue", label: "Chờ trích xuất" },
    Processing: { color: "processing", label: "Đang trích xuất" },
    Succeeded: { color: "success", label: "Đã trích xuất" },
    Failed: { color: "error", label: "Trích xuất lỗi" },
    Unsupported: { color: "default", label: "Không hỗ trợ" },
  };
  return <Tag color={config[status].color}>{config[status].label}</Tag>;
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
      <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(returnTo)}>
        Về danh sách
      </Button>
    </div>
  );
}

function normalizeFormValues(values: IncomingDocumentFormValues) {
  return {
    referenceNumber: values.referenceNumber.trim(),
    senderOrg: values.senderOrg.trim(),
    summary: values.summary.trim(),
    receivedDate: values.receivedDate,
    deadline: values.deadline,
    documentTypeId: values.documentTypeId,
  };
}

function createPatch(
  form: FormInstance<IncomingDocumentFormValues>,
  values: IncomingDocumentFormValues,
): IncomingDocumentUpdateRequest {
  const normalized = normalizeFormValues(values);
  const request: IncomingDocumentUpdateRequest = {};
  for (const field of [
    "referenceNumber",
    "senderOrg",
    "summary",
    "receivedDate",
    "deadline",
    "documentTypeId",
  ] as const) {
    if (form.isFieldTouched(field)) {
      request[field] = normalized[field];
    }
  }
  return request;
}

function setFormValues(
  document: IncomingDocumentResponse,
  form: FormInstance<IncomingDocumentFormValues>,
) {
  form.setFields(
    ([
      ["referenceNumber", document.referenceNumber],
      ["senderOrg", document.senderOrg],
      ["summary", document.summary],
      ["receivedDate", document.receivedDate],
      ["deadline", document.deadline],
      ["documentTypeId", document.documentType.id],
    ] as const).map(([name, value]) => ({ name, value, touched: false, errors: [] })),
  );
}

function datesAreValid(
  receivedDate: string,
  deadline: string,
  form: FormInstance<IncomingDocumentFormValues>,
): boolean {
  if (receivedDate !== "" && deadline !== "" && receivedDate > deadline) {
    form.setFields([{ name: "deadline", errors: ["Hạn xử lý không được trước ngày tiếp nhận."] }]);
    return false;
  }
  return true;
}

function applyValidationErrors(error: unknown, form: FormInstance): boolean {
  if (!(error instanceof ApiError) || error.status !== 400) {
    return false;
  }
  const entries = Object.entries(error.problem.errors ?? {});
  if (entries.length === 0) {
    return false;
  }
  form.setFields(entries.map(([name, errors]) => ({ name, errors })));
  return true;
}

function readFilters(searchParams: URLSearchParams): IncomingFilters {
  return {
    q: searchParams.get("q")?.trim() ?? "",
    documentTypeId: searchParams.get("documentTypeId") ?? undefined,
    status: parseStatus(searchParams.get("status")),
    deadlineFrom: searchParams.get("deadlineFrom") ?? undefined,
    deadlineTo: searchParams.get("deadlineTo") ?? undefined,
  };
}

function createSearchParams(
  filters: IncomingFilters,
  page: number,
  pageSize: number,
): URLSearchParams {
  const params = new URLSearchParams();
  const q = filters.q.trim();
  if (q !== "") params.set("q", q);
  if (filters.documentTypeId !== undefined) params.set("documentTypeId", filters.documentTypeId);
  if (filters.status !== undefined) params.set("status", filters.status);
  if (filters.deadlineFrom !== undefined) params.set("deadlineFrom", filters.deadlineFrom);
  if (filters.deadlineTo !== undefined) params.set("deadlineTo", filters.deadlineTo);
  if (page > 1) params.set("page", String(page));
  if (pageSize !== 20) params.set("pageSize", String(pageSize));
  return params;
}

function parseStatus(value: string | null): IncomingDocumentStatus | undefined {
  return statusOptions.some((option) => option.value === value)
    ? value as IncomingDocumentStatus
    : undefined;
}

function parsePositiveInteger(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : fallback;
}

function parsePageSize(value: string | null): number {
  const parsed = parsePositiveInteger(value, 20);
  return parsed <= 100 ? parsed : 20;
}

function formatDate(value: string): string {
  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}/${month}/${year}` : value;
}

function formatDateTime(value: string | null): string {
  if (value === null) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
}

function formatStaff(staff: IncomingStaffReference | null): string {
  if (staff === null) return "—";
  const details = [staff.position, staff.department].filter(Boolean).join(" — ");
  return details === "" ? staff.fullName : `${staff.fullName} (${details})`;
}

function sortAttachments(attachments: AttachmentResponse[]): AttachmentResponse[] {
  return [...attachments].sort((left, right) => {
    const byDate = right.uploadedAt.localeCompare(left.uploadedAt);
    return byDate === 0 ? left.id.localeCompare(right.id) : byDate;
  });
}

function getAttachmentErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    if (error.status === 413) {
      return "File vượt giới hạn dung lượng cho phép.";
    }
    if (error.status === 415) {
      return "File không đúng định dạng PDF, DOCX, XLSX, JPG, JPEG hoặc PNG.";
    }
  }

  return getErrorMessage(error, fallback);
}

function triggerDownload(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim() !== "" ? error.message : fallback;
}

function readReturnTo(state: unknown): string {
  if (
    typeof state === "object"
    && state !== null
    && "returnTo" in state
    && typeof state.returnTo === "string"
    && state.returnTo.startsWith("/incoming-documents")
  ) {
    return state.returnTo;
  }
  return "/incoming-documents";
}

function readSuccess(state: unknown): string | null {
  return typeof state === "object"
    && state !== null
    && "success" in state
    && typeof state.success === "string"
    ? state.success
    : null;
}
