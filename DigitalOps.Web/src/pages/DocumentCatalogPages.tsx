import {
  ArrowLeftOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
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
  useSearchParams,
} from "react-router";
import { ApiError } from "../shared/api/api-client";
import type { PagedResponse } from "../shared/api/types";
import {
  createDocumentTemplate,
  createDocumentType,
  getAllDocumentTypes,
  getDocumentTemplate,
  getDocumentTemplates,
  getDocumentType,
  getDocumentTypes,
  updateDocumentTemplate,
  updateDocumentType,
} from "../shared/document-catalog/document-catalog-service";
import type {
  DocumentTemplateResponse,
  DocumentTemplateUpdateRequest,
  DocumentTypeResponse,
  DocumentTypeUpdateRequest,
  FormatRules,
} from "../shared/document-catalog/types";

interface DocumentTypeFormValues {
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
}

interface DocumentTemplateFormValues {
  documentTypeId: string;
  name: string;
  templateContent: string;
  formatRulesText: string;
  isActive: boolean;
}

const defaultFormatRules = JSON.stringify(
  { version: 1, rules: [] },
  null,
  2,
);

export function DocumentTypeListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeOnly = searchParams.get("activeOnly") === "true";
  const page = parsePositiveInteger(searchParams.get("page"), 1);
  const pageSize = parsePageSize(searchParams.get("pageSize"));
  const [draftFilter, setDraftFilter] = useState(() => ({
    sourceActiveOnly: activeOnly,
    activeOnly,
  }));
  const [data, setData] =
    useState<PagedResponse<DocumentTypeResponse> | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createForm] = Form.useForm<DocumentTypeFormValues>();
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  if (draftFilter.sourceActiveOnly !== activeOnly) {
    setDraftFilter({ sourceActiveOnly: activeOnly, activeOnly });
  }

  const draftActiveOnly = draftFilter.activeOnly;

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
        const response = await getDocumentTypes({
          activeOnly: activeOnly || undefined,
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
              "Không thể tải danh sách loại văn bản. Vui lòng thử lại.",
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

  const columns: TableProps<DocumentTypeResponse>["columns"] = [
    { title: "Mã", dataIndex: "code", key: "code", width: 160 },
    { title: "Tên loại văn bản", dataIndex: "name", key: "name" },
    {
      title: "Mô tả",
      dataIndex: "description",
      key: "description",
      render: (description: string | null) => description ?? "—",
    },
    {
      title: "Trạng thái",
      dataIndex: "isActive",
      key: "isActive",
      width: 150,
      render: (isActive: boolean) => <ActiveTag isActive={isActive} />,
    },
    {
      title: "Thao tác",
      key: "action",
      width: 100,
      render: (_, documentType) => (
        <Button
          type="link"
          onClick={() =>
            navigate(`/document-types/${documentType.id}`, {
              state: { returnTo: `${location.pathname}${location.search}` },
            })
          }
        >
          Xem
        </Button>
      ),
    },
  ];

  const openCreate = () => {
    setCreateError(null);
    createForm.setFieldsValue({
      code: "",
      name: "",
      description: undefined,
      isActive: true,
    });
    setCreateOpen(true);
  };

  const handleCreate = async (values: DocumentTypeFormValues) => {
    setCreating(true);
    setCreateError(null);
    try {
      const created = await createDocumentType({
        code: values.code.trim(),
        name: values.name.trim(),
        description: normalizeOptional(values.description),
        isActive: values.isActive,
      });
      setCreateOpen(false);
      navigate(`/document-types/${created.id}`, {
        state: {
          success: "Đã tạo loại văn bản.",
          returnTo: `${location.pathname}${location.search}`,
        },
      });
    } catch (error) {
      if (!applyValidationErrors(error, createForm)) {
        setCreateError(
          getErrorMessage(
            error,
            "Không thể tạo loại văn bản. Vui lòng thử lại.",
          ),
        );
      }
    } finally {
      setCreating(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Loại văn bản</Typography.Title>
          <Typography.Text type="secondary">
            Quản lý danh mục loại dùng cho văn bản đến và mẫu soạn thảo.
          </Typography.Text>
        </div>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          Tạo loại văn bản
        </Button>
      </div>

      <Card>
        <Space wrap>
          <Select<string>
            className="catalog-status-filter"
            aria-label="Trạng thái loại văn bản"
            value={draftActiveOnly ? "active" : undefined}
            allowClear
            placeholder="Tất cả trạng thái"
            options={[{ value: "active", label: "Đang hoạt động" }]}
            onChange={(value) =>
              setDraftFilter((current) => ({
                ...current,
                activeOnly: value === "active",
              }))
            }
          />
          <Button
            type="primary"
            onClick={() =>
              setSearchParams(
                createCatalogSearchParams({
                  activeOnly: draftActiveOnly,
                  page: 1,
                  pageSize,
                }),
              )
            }
          >
            Áp dụng
          </Button>
          <Button
            onClick={() => {
              setDraftFilter({ sourceActiveOnly: false, activeOnly: false });
              setSearchParams(
                createCatalogSearchParams({ page: 1, pageSize }),
              );
            }}
          >
            Xóa bộ lọc
          </Button>
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
              <Empty description="Không có loại văn bản phù hợp.">
                {activeOnly && (
                  <Button
                    onClick={() =>
                      setSearchParams(
                        createCatalogSearchParams({ page: 1, pageSize }),
                      )
                    }
                  >
                    Xóa bộ lọc
                  </Button>
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
          onChange={(pagination) =>
            setSearchParams(
              createCatalogSearchParams({
                activeOnly,
                page: pagination.current ?? 1,
                pageSize: pagination.pageSize ?? 20,
              }),
            )
          }
        />
      </Card>

      <Modal
        title="Tạo loại văn bản"
        open={createOpen}
        okText="Tạo loại văn bản"
        cancelText="Hủy"
        confirmLoading={creating}
        onCancel={() => setCreateOpen(false)}
        onOk={() => createForm.submit()}
        forceRender
        afterClose={() => {
          createForm.resetFields();
          setCreateError(null);
        }}
      >
        {createError !== null && (
          <Alert className="catalog-modal-alert" type="error" showIcon title={createError} />
        )}
        <DocumentTypeForm
          form={createForm}
          onFinish={handleCreate}
          showSubmit={false}
        />
      </Modal>
    </Space>
  );
}

export function DocumentTypeDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<DocumentTypeFormValues>();
  const [documentType, setDocumentType] =
    useState<DocumentTypeResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(
    readNavigationSuccess(location.state),
  );
  const [reloadVersion, setReloadVersion] = useState(0);
  const returnTo = readReturnTo(location.state, "/document-types");

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      setNotFound(false);
      setErrorMessage(null);
      try {
        const response = await getDocumentType(id);
        if (!ignored) {
          setDocumentType(response);
          setDocumentTypeForm(response, form);
        }
      } catch (error) {
        if (!ignored) {
          if (error instanceof ApiError && error.status === 404) {
            setNotFound(true);
          } else {
            setErrorMessage(
              getErrorMessage(error, "Không thể tải loại văn bản."),
            );
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

  if (notFound) {
    return (
      <Result
        status="404"
        title="Không tìm thấy loại văn bản"
        subTitle="Danh mục có thể không tồn tại hoặc đã thay đổi."
        extra={<Button onClick={() => navigate(returnTo)}>Về danh sách</Button>}
      />
    );
  }

  const handleSubmit = async (values: DocumentTypeFormValues) => {
    if (documentType === null) {
      return;
    }

    const request = createDocumentTypePatch(form, values);
    if (Object.keys(request).length === 0) {
      setSuccessMessage("Không có thay đổi cần lưu.");
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      const updated = await updateDocumentType(documentType.id, request);
      setDocumentType(updated);
      setDocumentTypeForm(updated, form);
      setSuccessMessage("Đã cập nhật loại văn bản.");
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(error, "Không thể cập nhật loại văn bản."),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title={documentType?.name ?? "Chi tiết loại văn bản"}
        description="Cập nhật mã, tên, mô tả và trạng thái sử dụng."
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
            documentType === null ? (
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
        title="Thông tin loại văn bản"
        extra={
          documentType === null ? undefined : (
            <ActiveTag isActive={documentType.isActive} />
          )
        }
      >
        {documentType !== null && (
          <DocumentTypeForm
            form={form}
            submitting={submitting}
            onFinish={handleSubmit}
          />
        )}
      </Card>
    </Space>
  );
}

export function DocumentTemplateListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const documentTypeId = searchParams.get("documentTypeId") ?? undefined;
  const activeOnly = searchParams.get("activeOnly") === "true";
  const page = parsePositiveInteger(searchParams.get("page"), 1);
  const pageSize = parsePageSize(searchParams.get("pageSize"));
  const [draftFilter, setDraftFilter] = useState(() => ({
    sourceTypeId: documentTypeId,
    sourceActiveOnly: activeOnly,
    typeId: documentTypeId,
    activeOnly,
  }));
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [data, setData] =
    useState<PagedResponse<DocumentTemplateResponse> | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (
    draftFilter.sourceTypeId !== documentTypeId
    || draftFilter.sourceActiveOnly !== activeOnly
  ) {
    setDraftFilter({
      sourceTypeId: documentTypeId,
      sourceActiveOnly: activeOnly,
      typeId: documentTypeId,
      activeOnly,
    });
  }

  const draftTypeId = draftFilter.typeId;
  const draftActiveOnly = draftFilter.activeOnly;

  useEffect(() => {
    let ignored = false;
    void (async () => {
      try {
        const response = await getAllDocumentTypes();
        if (!ignored) {
          setDocumentTypes(response);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(
            getErrorMessage(error, "Không thể tải danh mục loại văn bản."),
          );
        }
      }
    })();
    return () => {
      ignored = true;
    };
  }, [reloadVersion]);

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
        const response = await getDocumentTemplates({
          documentTypeId,
          activeOnly: activeOnly || undefined,
          page,
          pageSize,
        });
        if (!ignored) {
          setData(response);
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(
            getErrorMessage(error, "Không thể tải danh sách mẫu văn bản."),
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
  }, [activeOnly, documentTypeId, page, pageSize, reloadVersion]);

  const columns: TableProps<DocumentTemplateResponse>["columns"] = [
    {
      title: "Loại văn bản",
      key: "documentType",
      render: (_, template) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text>{template.documentType.name}</Typography.Text>
          <Typography.Text type="secondary">
            {template.documentType.code}
          </Typography.Text>
        </Space>
      ),
    },
    { title: "Tên mẫu", dataIndex: "name", key: "name" },
    {
      title: "Cập nhật",
      dataIndex: "updatedAt",
      key: "updatedAt",
      render: (value: string) => formatDateTime(value),
    },
    {
      title: "Trạng thái",
      dataIndex: "isActive",
      key: "isActive",
      render: (isActive: boolean) => <ActiveTag isActive={isActive} />,
    },
    {
      title: "Thao tác",
      key: "action",
      render: (_, template) => (
        <Button
          type="link"
          onClick={() =>
            navigate(`/document-templates/${template.id}`, {
              state: { returnTo: `${location.pathname}${location.search}` },
            })
          }
        >
          Xem
        </Button>
      ),
    },
  ];

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Mẫu văn bản</Typography.Title>
          <Typography.Text type="secondary">
            Quản lý nội dung mẫu và quy tắc thể thức dùng khi soạn thảo.
          </Typography.Text>
        </div>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() =>
            navigate("/document-templates/new", {
              state: { returnTo: `${location.pathname}${location.search}` },
            })
          }
        >
          Tạo mẫu văn bản
        </Button>
      </div>
      <Card>
        <Space wrap>
          <Select<string>
            showSearch
            optionFilterProp="label"
            className="catalog-type-filter"
            aria-label="Lọc theo loại văn bản"
            allowClear
            placeholder="Tất cả loại văn bản"
            value={draftTypeId}
            options={documentTypes.map((item) => ({
              value: item.id,
              label: `${item.code} — ${item.name}`,
            }))}
            onChange={(value) =>
              setDraftFilter((current) => ({ ...current, typeId: value }))
            }
          />
          <Select<string>
            className="catalog-status-filter"
            aria-label="Trạng thái mẫu văn bản"
            value={draftActiveOnly ? "active" : undefined}
            allowClear
            placeholder="Tất cả trạng thái"
            options={[{ value: "active", label: "Đang hoạt động" }]}
            onChange={(value) =>
              setDraftFilter((current) => ({
                ...current,
                activeOnly: value === "active",
              }))
            }
          />
          <Button
            type="primary"
            onClick={() =>
              setSearchParams(
                createCatalogSearchParams({
                  documentTypeId: draftTypeId,
                  activeOnly: draftActiveOnly,
                  page: 1,
                  pageSize,
                }),
              )
            }
          >
            Áp dụng
          </Button>
          <Button
            onClick={() => {
              setDraftFilter({
                sourceTypeId: undefined,
                sourceActiveOnly: false,
                typeId: undefined,
                activeOnly: false,
              });
              setSearchParams(
                createCatalogSearchParams({ page: 1, pageSize }),
              );
            }}
          >
            Xóa bộ lọc
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => setReloadVersion((version) => version + 1)}
          >
            Tải lại
          </Button>
        </Space>
      </Card>
      {errorMessage !== null && (
        <Alert type="error" showIcon title={errorMessage} />
      )}
      <Card>
        <Table
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={loading}
          locale={{
            emptyText: (
              <Empty description="Không có mẫu văn bản phù hợp." />
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
          onChange={(pagination) =>
            setSearchParams(
              createCatalogSearchParams({
                documentTypeId,
                activeOnly,
                page: pagination.current ?? 1,
                pageSize: pagination.pageSize ?? 20,
              }),
            )
          }
        />
      </Card>
    </Space>
  );
}

export function DocumentTemplateCreatePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<DocumentTemplateFormValues>();
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const returnTo = readReturnTo(location.state, "/document-templates");

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      try {
        const response = await getAllDocumentTypes(true);
        if (!ignored) {
          setDocumentTypes(response);
          form.setFieldsValue({
            formatRulesText: defaultFormatRules,
            isActive: true,
          });
        }
      } catch (error) {
        if (!ignored) {
          setErrorMessage(
            getErrorMessage(error, "Không thể tải danh mục loại văn bản."),
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
  }, [form]);

  const handleSubmit = async (values: DocumentTemplateFormValues) => {
    const parsed = parseFormatRules(values.formatRulesText);
    if (parsed.error !== undefined) {
      form.setFields([{ name: "formatRulesText", errors: [parsed.error] }]);
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    try {
      const created = await createDocumentTemplate({
        documentTypeId: values.documentTypeId,
        name: values.name.trim(),
        templateContent: values.templateContent.trim(),
        formatRules: parsed.value!,
        isActive: values.isActive,
      });
      navigate(`/document-templates/${created.id}`, {
        replace: true,
        state: { success: "Đã tạo mẫu văn bản.", returnTo },
      });
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(error, "Không thể tạo mẫu văn bản."),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title="Tạo mẫu văn bản"
        description="Nhập nội dung mẫu và FormatRules JSON dùng cho thẩm định."
        returnTo={returnTo}
      />
      {errorMessage !== null && (
        <Alert type="error" showIcon title={errorMessage} />
      )}
      {!loading && documentTypes.length === 0 && (
        <Alert
          type="warning"
          showIcon
          title="Chưa có loại văn bản đang hoạt động."
          description="Hãy kích hoạt hoặc tạo loại văn bản trước khi tạo mẫu."
        />
      )}
      <Card loading={loading}>
        {!loading && (
          <DocumentTemplateForm
            form={form}
            documentTypes={documentTypes}
            submitting={submitting}
            submitDisabled={documentTypes.length === 0}
            submitLabel="Tạo mẫu văn bản"
            onFinish={handleSubmit}
          />
        )}
      </Card>
    </Space>
  );
}

export function DocumentTemplateDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [form] = Form.useForm<DocumentTemplateFormValues>();
  const [template, setTemplate] = useState<DocumentTemplateResponse | null>(null);
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(
    readNavigationSuccess(location.state),
  );
  const [reloadVersion, setReloadVersion] = useState(0);
  const returnTo = readReturnTo(location.state, "/document-templates");

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      setNotFound(false);
      setErrorMessage(null);
      try {
        const [templateResponse, typeResponse] = await Promise.all([
          getDocumentTemplate(id),
          getAllDocumentTypes(),
        ]);
        if (!ignored) {
          setTemplate(templateResponse);
          setDocumentTypes(typeResponse);
          setDocumentTemplateForm(templateResponse, form);
        }
      } catch (error) {
        if (!ignored) {
          if (error instanceof ApiError && error.status === 404) {
            setNotFound(true);
          } else {
            setErrorMessage(
              getErrorMessage(error, "Không thể tải mẫu văn bản."),
            );
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

  if (notFound) {
    return (
      <Result
        status="404"
        title="Không tìm thấy mẫu văn bản"
        subTitle="Mẫu có thể không tồn tại hoặc đã thay đổi."
        extra={<Button onClick={() => navigate(returnTo)}>Về danh sách</Button>}
      />
    );
  }

  const handleSubmit = async (values: DocumentTemplateFormValues) => {
    if (template === null) {
      return;
    }

    const parsed = parseFormatRules(values.formatRulesText);
    if (parsed.error !== undefined) {
      form.setFields([{ name: "formatRulesText", errors: [parsed.error] }]);
      return;
    }

    const request = createDocumentTemplatePatch(form, values, parsed.value!);
    if (Object.keys(request).length === 0) {
      setSuccessMessage("Không có thay đổi cần lưu.");
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      const updated = await updateDocumentTemplate(template.id, request);
      setTemplate(updated);
      setDocumentTemplateForm(updated, form);
      setSuccessMessage("Đã cập nhật mẫu văn bản.");
    } catch (error) {
      if (!applyValidationErrors(error, form)) {
        setErrorMessage(
          getErrorMessage(error, "Không thể cập nhật mẫu văn bản."),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const currentType = documentTypes.find(
    (item) => item.id === template?.documentType.id,
  );

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <PageBackHeading
        title={template?.name ?? "Chi tiết mẫu văn bản"}
        description="Cập nhật nội dung, FormatRules và trạng thái sử dụng."
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
            template === null ? (
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
      {template !== null && currentType?.isActive === false && (
        <Alert
          type="warning"
          showIcon
          title="Loại văn bản của mẫu đã ngừng hoạt động."
          description="Mẫu không xuất hiện trong danh sách khả dụng cho văn bản mới. Bạn vẫn có thể sửa hoặc vô hiệu hóa mẫu."
        />
      )}
      <Card
        loading={loading}
        extra={
          template === null ? undefined : (
            <ActiveTag isActive={template.isActive} />
          )
        }
      >
        {template !== null && (
          <DocumentTemplateForm
            form={form}
            documentTypes={documentTypes}
            currentTypeId={template.documentType.id}
            submitting={submitting}
            submitLabel="Lưu mẫu văn bản"
            onFinish={handleSubmit}
          />
        )}
      </Card>
    </Space>
  );
}

function DocumentTypeForm({
  form,
  submitting = false,
  showSubmit = true,
  onFinish,
}: {
  form: FormInstance<DocumentTypeFormValues>;
  submitting?: boolean;
  showSubmit?: boolean;
  onFinish: (values: DocumentTypeFormValues) => void | Promise<void>;
}) {
  return (
    <Form
      className="catalog-form"
      form={form}
      layout="vertical"
      requiredMark
      onFinish={(values) => void onFinish(values)}
    >
      <div className="catalog-form-grid">
        <Form.Item
          name="code"
          label="Mã loại văn bản"
          rules={[
            { required: true, whitespace: true, message: "Vui lòng nhập mã loại văn bản." },
            { max: 50, message: "Mã không được vượt quá 50 ký tự." },
          ]}
        >
          <Input autoComplete="off" />
        </Form.Item>
        <Form.Item
          name="name"
          label="Tên loại văn bản"
          rules={[
            { required: true, whitespace: true, message: "Vui lòng nhập tên loại văn bản." },
            { max: 150, message: "Tên không được vượt quá 150 ký tự." },
          ]}
        >
          <Input />
        </Form.Item>
      </div>
      <Form.Item name="description" label="Mô tả">
        <Input.TextArea autoSize={{ minRows: 3, maxRows: 8 }} />
      </Form.Item>
      <Form.Item name="isActive" label="Cho phép sử dụng" valuePropName="checked">
        <Switch checkedChildren="Hoạt động" unCheckedChildren="Ngừng" />
      </Form.Item>
      {showSubmit && (
        <Button
          type="primary"
          htmlType="submit"
          icon={<SaveOutlined />}
          loading={submitting}
        >
          Lưu loại văn bản
        </Button>
      )}
    </Form>
  );
}

function DocumentTemplateForm({
  form,
  documentTypes,
  currentTypeId,
  submitting,
  submitDisabled = false,
  submitLabel,
  onFinish,
}: {
  form: FormInstance<DocumentTemplateFormValues>;
  documentTypes: DocumentTypeResponse[];
  currentTypeId?: string;
  submitting: boolean;
  submitDisabled?: boolean;
  submitLabel: string;
  onFinish: (values: DocumentTemplateFormValues) => void | Promise<void>;
}) {
  return (
    <Form
      className="catalog-form"
      form={form}
      layout="vertical"
      requiredMark
      onFinish={(values) => void onFinish(values)}
    >
      <div className="catalog-form-grid">
        <Form.Item
          name="documentTypeId"
          label="Loại văn bản"
          rules={[{ required: true, message: "Vui lòng chọn loại văn bản." }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Chọn loại văn bản"
            options={documentTypes.map((item) => ({
              value: item.id,
              label: `${item.code} — ${item.name}${item.isActive ? "" : " (Ngừng hoạt động)"}`,
              disabled: !item.isActive && item.id !== currentTypeId,
            }))}
          />
        </Form.Item>
        <Form.Item
          name="name"
          label="Tên mẫu văn bản"
          rules={[
            { required: true, whitespace: true, message: "Vui lòng nhập tên mẫu văn bản." },
            { max: 200, message: "Tên mẫu không được vượt quá 200 ký tự." },
          ]}
        >
          <Input />
        </Form.Item>
      </div>
      <Form.Item
        name="templateContent"
        label="Nội dung mẫu"
        extra="Token phân biệt hoa/thường. Hội viên: {{member.fullName}}, dateOfBirth, gender, address, phone, email, position, joinDate. Văn bản đến: {{incoming.referenceNumber}}, senderOrg, summary, receivedDate, deadline. Token thiếu dữ liệu được giữ nguyên."
        rules={[
          { required: true, whitespace: true, message: "Vui lòng nhập nội dung mẫu văn bản." },
        ]}
      >
        <Input.TextArea autoSize={{ minRows: 10, maxRows: 24 }} />
      </Form.Item>
      <Form.Item
        className="format-rules-editor"
        name="formatRulesText"
        label="FormatRules (JSON)"
        extra="Yêu cầu version là số nguyên dương; rules là mảng các object có code và required."
        rules={[
          { required: true, whitespace: true, message: "Vui lòng nhập FormatRules." },
          {
            validator: async (_, value: string | undefined) => {
              if (value === undefined || value.trim().length === 0) {
                return;
              }
              const parsed = parseFormatRules(value);
              if (parsed.error !== undefined) {
                throw new Error(parsed.error);
              }
            },
          },
        ]}
      >
        <Input.TextArea
          spellCheck={false}
          autoSize={{ minRows: 12, maxRows: 28 }}
        />
      </Form.Item>
      <Form.Item name="isActive" label="Cho phép sử dụng" valuePropName="checked">
        <Switch checkedChildren="Hoạt động" unCheckedChildren="Ngừng" />
      </Form.Item>
      <Button
        type="primary"
        htmlType="submit"
        icon={<SaveOutlined />}
        loading={submitting}
        disabled={submitDisabled}
      >
        {submitLabel}
      </Button>
    </Form>
  );
}

function ActiveTag({ isActive }: { isActive: boolean }) {
  return (
    <Tag color={isActive ? "success" : "default"}>
      {isActive ? "Đang hoạt động" : "Ngừng hoạt động"}
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
      <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(returnTo)}>
        Về danh sách
      </Button>
    </div>
  );
}

function createDocumentTypePatch(
  form: FormInstance<DocumentTypeFormValues>,
  values: DocumentTypeFormValues,
): DocumentTypeUpdateRequest {
  const request: DocumentTypeUpdateRequest = {};
  if (form.isFieldTouched("code")) {
    request.code = values.code.trim();
  }
  if (form.isFieldTouched("name")) {
    request.name = values.name.trim();
  }
  if (form.isFieldTouched("description")) {
    request.description = normalizeOptional(values.description);
  }
  if (form.isFieldTouched("isActive")) {
    request.isActive = values.isActive;
  }
  return request;
}

function createDocumentTemplatePatch(
  form: FormInstance<DocumentTemplateFormValues>,
  values: DocumentTemplateFormValues,
  formatRules: FormatRules,
): DocumentTemplateUpdateRequest {
  const request: DocumentTemplateUpdateRequest = {};
  if (form.isFieldTouched("documentTypeId")) {
    request.documentTypeId = values.documentTypeId;
  }
  if (form.isFieldTouched("name")) {
    request.name = values.name.trim();
  }
  if (form.isFieldTouched("templateContent")) {
    request.templateContent = values.templateContent.trim();
  }
  if (form.isFieldTouched("formatRulesText")) {
    request.formatRules = formatRules;
  }
  if (form.isFieldTouched("isActive")) {
    request.isActive = values.isActive;
  }
  return request;
}

function setDocumentTypeForm(
  documentType: DocumentTypeResponse,
  form: FormInstance<DocumentTypeFormValues>,
) {
  form.setFields(
    [
      ["code", documentType.code],
      ["name", documentType.name],
      ["description", documentType.description ?? undefined],
      ["isActive", documentType.isActive],
    ].map(([name, value]) => ({
      name: name as keyof DocumentTypeFormValues,
      value,
      touched: false,
      errors: [],
    })),
  );
}

function setDocumentTemplateForm(
  template: DocumentTemplateResponse,
  form: FormInstance<DocumentTemplateFormValues>,
) {
  form.setFields(
    [
      ["documentTypeId", template.documentType.id],
      ["name", template.name],
      ["templateContent", template.templateContent],
      ["formatRulesText", JSON.stringify(template.formatRules, null, 2)],
      ["isActive", template.isActive],
    ].map(([name, value]) => ({
      name: name as keyof DocumentTemplateFormValues,
      value,
      touched: false,
      errors: [],
    })),
  );
}

function parseFormatRules(text: string): {
  value?: FormatRules;
  error?: string;
} {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch (error) {
    return {
      error: error instanceof Error
        ? `JSON không hợp lệ: ${error.message}`
        : "JSON không hợp lệ.",
    };
  }

  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return { error: "FormatRules phải là một JSON object." };
  }

  const record = value as Record<string, unknown>;
  if (!Number.isInteger(record.version) || (record.version as number) <= 0) {
    return { error: "FormatRules.version phải là số nguyên dương." };
  }
  if (!Array.isArray(record.rules)) {
    return { error: "FormatRules.rules phải là một mảng." };
  }

  const codes = new Set<string>();
  for (const [index, rule] of record.rules.entries()) {
    if (typeof rule !== "object" || rule === null || Array.isArray(rule)) {
      return { error: `FormatRules.rules[${index}] phải là một JSON object.` };
    }
    const ruleRecord = rule as Record<string, unknown>;
    if (typeof ruleRecord.code !== "string" || ruleRecord.code.trim().length === 0) {
      return { error: `FormatRules.rules[${index}].code phải là chuỗi không rỗng.` };
    }
    const code = ruleRecord.code.trim();
    if (codes.has(code)) {
      return { error: `FormatRules.rules[${index}].code bị trùng: ${code}.` };
    }
    codes.add(code);
    if (typeof ruleRecord.required !== "boolean") {
      return { error: `FormatRules.rules[${index}].required phải là boolean.` };
    }
  }

  return { value: value as FormatRules };
}

function applyValidationErrors(
  error: unknown,
  form: FormInstance,
): boolean {
  if (
    !(error instanceof ApiError)
    || (error.status !== 400 && error.status !== 422)
  ) {
    return false;
  }

  const entries = Object.entries(error.problem.errors ?? {});
  if (entries.length === 0) {
    return false;
  }

  form.setFields(
    entries.map(([name, errors]) => ({
      name: name === "formatRules" ? "formatRulesText" : name,
      errors,
    })),
  );
  return true;
}

function createCatalogSearchParams({
  documentTypeId,
  activeOnly,
  page,
  pageSize,
}: {
  documentTypeId?: string;
  activeOnly?: boolean;
  page: number;
  pageSize: number;
}) {
  const params = new URLSearchParams();
  if (documentTypeId !== undefined) {
    params.set("documentTypeId", documentTypeId);
  }
  if (activeOnly === true) {
    params.set("activeOnly", "true");
  }
  if (page > 1) {
    params.set("page", String(page));
  }
  if (pageSize !== 20) {
    params.set("pageSize", String(pageSize));
  }
  return params;
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

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
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

function readReturnTo(state: unknown, fallback: string): string {
  if (
    typeof state === "object"
    && state !== null
    && "returnTo" in state
    && typeof state.returnTo === "string"
    && state.returnTo.startsWith(fallback)
  ) {
    return state.returnTo;
  }
  return fallback;
}
