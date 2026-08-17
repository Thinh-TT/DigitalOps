import {
  ArrowLeftOutlined, DeleteOutlined, DownloadOutlined, PlusOutlined,
  ReloadOutlined, SearchOutlined, UploadOutlined,
} from "@ant-design/icons";
import {
  Alert, Button, Card, Descriptions, Empty, Form, Input, List, Modal, Popconfirm,
  Select, Space, Table, Tabs, Tag, Typography, Upload,
  type FormInstance, type TableProps, type UploadProps,
} from "antd";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError } from "../shared/api/api-client";
import type { PagedResponse } from "../shared/api/types";
import { useAuth } from "../shared/auth/auth-context";
import { getAllDocumentTemplates } from "../shared/document-catalog/document-catalog-service";
import type { DocumentTemplateResponse } from "../shared/document-catalog/types";
import { getIncomingDocuments } from "../shared/incoming-documents/incoming-document-service";
import type { AttachmentResponse } from "../shared/incoming-documents/types";
import { getMemberLookup } from "../shared/members/member-service";
import type { MemberLookupResponse } from "../shared/members/types";
import {
  createOutgoingDocument, deleteOutgoingAttachment, downloadOutgoingAttachment,
  createOutgoingReview, generateOutgoingAiDraft, getOutgoingDocument, getOutgoingDocuments,
  getOutgoingReviews,
  updateOutgoingDocument, uploadOutgoingAttachment,
} from "../shared/outgoing-documents/outgoing-document-service";
import { ReviewCitations } from "../shared/outgoing-documents/ReviewCitations";
import type {
  OutgoingDocumentCreateRequest, OutgoingDocumentListParameters,
  OutgoingDocumentResponse, OutgoingDocumentStatus, ReviewResponse,
} from "../shared/outgoing-documents/types";

type OutgoingFilters = Pick<OutgoingDocumentListParameters, "q" | "templateId" | "status" | "dateFrom" | "dateTo">;
interface CreateValues { templateId: string; title: string; relatedMemberId?: string; relatedIncomingDocumentId?: string; }
interface OutgoingEditValues { title: string; content: string; }
const statuses: { value: OutgoingDocumentStatus; label: string }[] = [
  { value: "Editing", label: "Đang soạn" }, { value: "AiDraft", label: "AI draft" },
  { value: "PendingReview", label: "Chờ rà soát" }, { value: "ReviewFailed", label: "Cần chỉnh sửa" },
  { value: "PendingApproval", label: "Chờ duyệt" }, { value: "Approved", label: "Đã duyệt" }, { value: "Archived", label: "Đã lưu trữ" },
];

export function OutgoingDocumentListPage() {
  const { currentUser } = useAuth();
  const isDrafter = currentUser?.roles.includes("Drafter") ?? false;
  const navigate = useNavigate(); const location = useLocation(); const [params, setParams] = useSearchParams();
  const paramsKey = params.toString();
  const filters = useMemo(() => readFilters(new URLSearchParams(paramsKey)), [paramsKey]);
  const [draft, setDraft] = useState(filters);
  const page = parseIntOr(params.get("page"), 1); const pageSize = parseIntOr(params.get("pageSize"), 20);
  const [data, setData] = useState<PagedResponse<OutgoingDocumentResponse> | null>(null);
  const [templates, setTemplates] = useState<DocumentTemplateResponse[]>([]);
  const [loading, setLoading] = useState(true); const [error, setError] = useState<string | null>(null); const [reload, setReload] = useState(0);
  useEffect(() => {
    const timeout = globalThis.setTimeout(() => setDraft(filters), 0);
    return () => globalThis.clearTimeout(timeout);
  }, [filters]);
  useEffect(() => { let ignored = false; void (async () => {
    setLoading(true); setError(null);
    try { const [response, activeTemplates] = await Promise.all([
      getOutgoingDocuments({ ...filters, page, pageSize }), getAllDocumentTemplates(),
    ]); if (!ignored) { setData(response); setTemplates(activeTemplates); }
    } catch (e) { if (!ignored) setError(errorMessage(e, "Không thể tải danh sách văn bản đi.")); }
    finally { if (!ignored) setLoading(false); }
  })(); return () => { ignored = true; }; }, [filters, page, pageSize, reload]);
  const returnTo = `${location.pathname}${location.search}`;
  const apply = () => setParams(toParams(draft, 1, pageSize));
  const columns: TableProps<OutgoingDocumentResponse>["columns"] = [
    { title: "Tiêu đề", dataIndex: "title", key: "title", ellipsis: true },
    { title: "Mẫu", key: "template", width: 180, render: (_, item) => item.template.name },
    { title: "Liên kết", key: "related", width: 210, render: (_, item) => item.relatedMember?.fullName ?? item.relatedIncomingDocument?.referenceNumber ?? "—" },
    { title: "Trạng thái", dataIndex: "status", key: "status", width: 140, render: (s: OutgoingDocumentStatus) => <OutgoingStatusTag status={s} /> },
    { title: "Cập nhật", dataIndex: "updatedAt", key: "updatedAt", width: 170, render: formatDateTime },
    { title: "Thao tác", key: "action", width: 70, render: (_, item) => <Button type="link" onClick={() => navigate(`/outgoing-documents/${item.id}`, { state: { returnTo } })}>Xem</Button> },
  ];
  return <Space className="page-stack" orientation="vertical" size="large">
    <div className="page-heading-row"><div><Typography.Title level={2}>Văn bản đi</Typography.Title><Typography.Text type="secondary">Tạo và theo dõi văn bản phát hành theo mẫu.</Typography.Text></div>{isDrafter && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate("/outgoing-documents/new", { state: { returnTo } })}>Tạo văn bản đi</Button>}</div>
    <Card><Space wrap>
      <Input aria-label="Từ khóa văn bản đi" allowClear placeholder="Tiêu đề, số văn bản" value={draft.q ?? ""} onChange={e => setDraft(v => ({ ...v, q: e.target.value }))} onPressEnter={apply} />
      <Select aria-label="Mẫu văn bản đi" allowClear showSearch optionFilterProp="label" placeholder="Tất cả mẫu" value={draft.templateId} options={templates.map(t => ({ value: t.id, label: `${t.documentType.code} — ${t.name}` }))} onChange={v => setDraft(x => ({ ...x, templateId: v }))} />
      <Select aria-label="Trạng thái văn bản đi" allowClear placeholder="Tất cả trạng thái" value={draft.status} options={statuses} onChange={v => setDraft(x => ({ ...x, status: v }))} />
      <Input type="date" aria-label="Tạo từ ngày" value={draft.dateFrom ?? ""} onChange={e => setDraft(x => ({ ...x, dateFrom: e.target.value || undefined }))} />
      <Input type="date" aria-label="Tạo đến ngày" value={draft.dateTo ?? ""} onChange={e => setDraft(x => ({ ...x, dateTo: e.target.value || undefined }))} />
      <Button type="primary" icon={<SearchOutlined />} onClick={apply}>Lọc</Button><Button onClick={() => { setDraft({}); setParams(toParams({}, 1, pageSize)); }}>Xóa bộ lọc</Button>
    </Space></Card>
    {error && <Alert type="error" showIcon title={error} action={<Button size="small" icon={<ReloadOutlined />} onClick={() => setReload(v => v + 1)}>Thử lại</Button>} />}
    <Card><Table rowKey="id" loading={loading} columns={columns} dataSource={data?.items ?? []} locale={{ emptyText: <Empty description="Chưa có văn bản đi phù hợp." /> }} pagination={{ current: data?.page ?? page, pageSize: data?.pageSize ?? pageSize, total: data?.totalCount ?? 0, showSizeChanger: true, pageSizeOptions: [10, 20, 50, 100], onChange: (p, ps) => setParams(toParams(filters, ps === pageSize ? p : 1, ps)) }} scroll={{ x: 1100 }} /></Card>
  </Space>;
}

export function OutgoingDocumentCreatePage() {
  const [form] = Form.useForm<CreateValues>(); const navigate = useNavigate(); const location = useLocation(); const returnTo = readReturnTo(location.state);
  const [templates, setTemplates] = useState<DocumentTemplateResponse[]>([]); const [members, setMembers] = useState<MemberLookupResponse[]>([]); const [incoming, setIncoming] = useState<{ id: string; referenceNumber: string; summary: string }[]>([]);
  const [loading, setLoading] = useState(true); const [submitting, setSubmitting] = useState(false); const [error, setError] = useState<string | null>(null);
  useEffect(() => { let ignored = false; void (async () => { try { const [t, m, i] = await Promise.all([getAllDocumentTemplates(true), getMemberLookup({ pageSize: 100 }), getIncomingDocuments({ pageSize: 100 })]); if (!ignored) { setTemplates(t); setMembers(m.items); setIncoming(i.items); } } catch (e) { if (!ignored) setError(errorMessage(e, "Không thể tải dữ liệu lựa chọn.")); } finally { if (!ignored) setLoading(false); } })(); return () => { ignored = true; }; }, []);
  const submit = async (values: CreateValues) => { setSubmitting(true); setError(null); try { const request: OutgoingDocumentCreateRequest = { templateId: values.templateId, title: values.title.trim(), ...(values.relatedMemberId ? { relatedMemberId: values.relatedMemberId } : {}), ...(values.relatedIncomingDocumentId ? { relatedIncomingDocumentId: values.relatedIncomingDocumentId } : {}) }; const created = await createOutgoingDocument(request); navigate(`/outgoing-documents/${created.id}`, { state: { returnTo, success: "Đã tạo văn bản đi." } }); } catch (e) { if (!applyValidationErrors(e, form)) setError(errorMessage(e, "Không thể tạo văn bản đi.")); } finally { setSubmitting(false); } };
  return <Space className="page-stack" orientation="vertical" size="large"><div className="page-heading-row"><div><Typography.Title level={2}>Tạo văn bản đi</Typography.Title><Typography.Text type="secondary">Chọn mẫu và liên kết dữ liệu để render nội dung ban đầu.</Typography.Text></div><Button icon={<ArrowLeftOutlined />} onClick={() => navigate(returnTo)}>Về danh sách</Button></div><Card loading={loading}><Form form={form} layout="vertical" onFinish={values => void submit(values)}><Form.Item name="templateId" label="Mẫu văn bản" rules={[{ required: true, message: "Vui lòng chọn mẫu văn bản." }]}><Select showSearch optionFilterProp="label" options={templates.map(t => ({ value: t.id, label: `${t.documentType.code} — ${t.name}` }))} /></Form.Item><Form.Item name="title" label="Tiêu đề" rules={[{ required: true, whitespace: true, message: "Vui lòng nhập tiêu đề." }, { max: 500, message: "Tiêu đề không quá 500 ký tự." }]}><Input maxLength={500} /></Form.Item><Form.Item name="relatedMemberId" label="Hội viên liên quan"><Select allowClear showSearch optionFilterProp="label" placeholder="Không liên kết" options={members.map(m => ({ value: m.id, label: `${m.fullName}${m.position ? ` — ${m.position}` : ""}` }))} /></Form.Item><Form.Item name="relatedIncomingDocumentId" label="Văn bản đến liên quan"><Select allowClear showSearch optionFilterProp="label" placeholder="Không liên kết" options={incoming.map(i => ({ value: i.id, label: `${i.referenceNumber} — ${i.summary}` }))} /></Form.Item><Alert type="info" showIcon message="Token được render tự động" description="Các token không biết hoặc thiếu dữ liệu sẽ được giữ nguyên để người soạn hoàn thiện." /><Typography.Text type="secondary">Token hội viên: member.fullName, dateOfBirth, gender, address, phone, email, position, joinDate. Token văn bản đến: incoming.referenceNumber, senderOrg, summary, receivedDate, deadline.</Typography.Text>{error && <Alert type="error" showIcon title={error} />}<Button type="primary" htmlType="submit" loading={submitting}>Tạo văn bản</Button></Form></Card></Space>;
}

export function OutgoingDocumentDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { currentUser } = useAuth();
  const [form] = Form.useForm<OutgoingEditValues>();
  const [outgoing, setOutgoing] = useState<OutgoingDocumentResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(readSuccess(location.state));
  const [saving, setSaving] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [aiModalOpen, setAiModalOpen] = useState(false);
  const [instruction, setInstruction] = useState("");
  const [uploading, setUploading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [reviews, setReviews] = useState<PagedResponse<ReviewResponse> | null>(null);
  const [reviewLoading, setReviewLoading] = useState(true);
  const [reviewing, setReviewing] = useState(false);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const canEdit = !!outgoing
    && isOutgoingEditable(outgoing.status)
    && outgoing.draftedByStaff.id === currentUser?.staff.id
    && (currentUser?.roles.includes("Drafter") ?? false);
  const canEditAttachments = canEdit && !reviewing;
  const canReview = !!outgoing
    && (outgoing.status === "Editing" || outgoing.status === "ReviewFailed")
    && outgoing.draftedByStaff.id === currentUser?.staff.id
    && (currentUser?.roles.includes("Drafter") ?? false);

  const applyDocument = useCallback((document: OutgoingDocumentResponse, preserveEditor = false) => {
    setOutgoing(document);
    if (!preserveEditor) {
      form.setFields([
        { name: "title", value: document.title, touched: false, errors: [] },
        { name: "content", value: document.content, touched: false, errors: [] },
      ]);
    }
  }, [form]);

  const load = useCallback((preserveEditor = false) => {
    if (!id) return;
    setLoading(true);
    setError(null);
    void getOutgoingDocument(id)
      .then(document => applyDocument(document, preserveEditor))
      .catch(e => setError(errorMessage(e, "Không thể tải văn bản đi.")))
      .finally(() => setLoading(false));
  }, [applyDocument, id]);

  const loadReviews = useCallback(async (page = 1) => {
    if (!id) return;
    setReviewLoading(true);
    setReviewError(null);
    try {
      setReviews(await getOutgoingReviews(id, { page, pageSize: 20 }));
    } catch (e) {
      setReviewError(errorMessage(e, "Không thể tải lịch sử thẩm định."));
    } finally {
      setReviewLoading(false);
    }
  }, [id]);

  useEffect(() => {
    if (!id) return;
    let ignored = false;
    void (async () => {
      setLoading(true);
      setError(null);
      try {
        const document = await getOutgoingDocument(id);
        if (!ignored) applyDocument(document);
      } catch (e) {
        if (!ignored) setError(errorMessage(e, "Không thể tải văn bản đi."));
      } finally {
        if (!ignored) setLoading(false);
      }
    })();
    return () => { ignored = true; };
  }, [applyDocument, id]);

  useEffect(() => {
    void (async () => {
      await loadReviews();
    })();
  }, [loadReviews]);

  const save = async (values: OutgoingEditValues) => {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await updateOutgoingDocument(id, {
        title: values.title.trim(),
        content: values.content.trim(),
      });
      applyDocument(updated);
      setSuccess("Đã lưu nội dung văn bản.");
    } catch (e) {
      if (!applyValidationErrors(e, form)) {
        setError(errorMessage(e, "Không thể lưu nội dung văn bản."));
      }
    } finally {
      setSaving(false);
    }
  };

  const openAiModal = () => {
    if (form.isFieldsTouched(["title", "content"])) {
      setError("Vui lòng lưu thay đổi tiêu đề và nội dung trước khi sinh nháp AI.");
      return;
    }

    setError(null);
    setAiModalOpen(true);
  };

  const generateDraft = async () => {
    if (!id) return;
    if (form.isFieldsTouched(["title", "content"])) {
      setError("Vui lòng lưu thay đổi tiêu đề và nội dung trước khi sinh nháp AI.");
      setAiModalOpen(false);
      return;
    }

    setGenerating(true);
    setError(null);
    try {
      const generated = await generateOutgoingAiDraft(id, {
        ...(instruction.trim() ? { instruction: instruction.trim() } : {}),
      });
      applyDocument(generated);
      setInstruction("");
      setAiModalOpen(false);
      setSuccess("Đã sinh và lưu bản nháp AI.");
    } catch (e) {
      setError(errorMessage(e, "Không thể sinh nháp AI."));
    } finally {
      setGenerating(false);
    }
  };

  const submitReview = async () => {
    if (!id) return;
    if (form.isFieldsTouched(["title", "content"])) {
      setError("Vui lòng lưu thay đổi tiêu đề và nội dung trước khi gửi thẩm định.");
      return;
    }

    setReviewing(true);
    setError(null);
    try {
      const review = await createOutgoingReview(id);
      setOutgoing(current => current && {
        ...current,
        status: review.documentStatus,
        reviewIssues: review.reviewIssues,
      });
      await loadReviews(1);
      setSuccess(review.reviewResult === "Passed"
        ? "Văn bản đã đạt thẩm định và được chuyển chờ duyệt."
        : "Thẩm định chưa đạt. Vui lòng chỉnh sửa các lỗi thể thức.");
    } catch (e) {
      setError(errorMessage(e, "Không thể gửi thẩm định."));
    } finally {
      setReviewing(false);
    }
  };

  const uploadProps: UploadProps = {
    showUploadList: false,
    disabled: uploading || !canEditAttachments,
    customRequest: options => {
      if (!(options.file instanceof File)) return;
      setUploading(true);
      setError(null);
      void uploadOutgoingAttachment(id!, options.file)
        .then(() => {
          setSuccess("Đã tải file lên.");
          load(true);
          options.onSuccess?.({}, options.file);
        })
        .catch(e => {
          setError(errorMessage(e, "Không thể tải file lên."));
          options.onError?.(e instanceof Error ? e : new Error("Upload failed"));
        })
        .finally(() => setUploading(false));
    },
  };
  const download = async (attachment: AttachmentResponse) => {
    setBusyId(attachment.id);
    try {
      const result = await downloadOutgoingAttachment(attachment.id);
      const url = URL.createObjectURL(result.blob);
      const anchor = globalThis.document.createElement("a");
      anchor.href = url;
      anchor.download = result.fileName;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(errorMessage(e, "Không thể tải file."));
    } finally {
      setBusyId(null);
    }
  };
  const remove = async (attachment: AttachmentResponse) => {
    setBusyId(attachment.id);
    try {
      await deleteOutgoingAttachment(attachment.id);
      setSuccess("Đã xóa file.");
      load(true);
    } catch (e) {
      setError(errorMessage(e, "Không thể xóa file."));
    } finally {
      setBusyId(null);
    }
  };

  return <Space className="page-stack" orientation="vertical" size="large">
    <div className="page-heading-row">
      <div>
        <Typography.Title level={2}>{outgoing?.title ?? "Chi tiết văn bản đi"}</Typography.Title>
        <Typography.Text type="secondary">Chỉnh sửa nội dung, sinh nháp AI và quản lý file đính kèm.</Typography.Text>
      </div>
      <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(readReturnTo(location.state))}>Về danh sách</Button>
    </div>
    {success && <Alert type="success" showIcon closable title={success} onClose={() => setSuccess(null)} />}
    {error && <Alert type="error" showIcon title={error} action={!outgoing && <Button size="small" onClick={() => load()}>Thử lại</Button>} />}
    {outgoing && <>
      <Card title="Thông tin văn bản" extra={<OutgoingStatusTag status={outgoing.status} />} loading={loading}>
        <Descriptions column={1} size="small">
          <Descriptions.Item label="Mẫu">{outgoing.template.documentType.code} — {outgoing.template.name}</Descriptions.Item>
          <Descriptions.Item label="Hội viên liên quan">{outgoing.relatedMember?.fullName ?? "—"}</Descriptions.Item>
          <Descriptions.Item label="Văn bản đến liên quan">{outgoing.relatedIncomingDocument ? `${outgoing.relatedIncomingDocument.referenceNumber} — ${outgoing.relatedIncomingDocument.summary}` : "—"}</Descriptions.Item>
          <Descriptions.Item label="Người soạn">{outgoing.draftedByStaff.fullName}</Descriptions.Item>
        </Descriptions>
      </Card>
      <Card title="Soạn thảo">
        <Form<OutgoingEditValues> form={form} layout="vertical" onFinish={values => void save(values)}>
          <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, whitespace: true, message: "Vui lòng nhập tiêu đề." }, { max: 500, message: "Tiêu đề không quá 500 ký tự." }]}>
            <Input readOnly={!canEdit || reviewing} maxLength={500} />
          </Form.Item>
          <Tabs items={[
            {
              key: "content",
              label: "Nội dung hiện tại",
              children: <Form.Item name="content" label="Nội dung" rules={[{ required: true, whitespace: true, message: "Vui lòng nhập nội dung văn bản." }]}>
                <Input.TextArea aria-label="Nội dung hiện tại" readOnly={!canEdit || reviewing} autoSize={{ minRows: 14, maxRows: 28 }} />
              </Form.Item>,
            },
            {
              key: "ai-draft",
              label: "Bản AI đầu tiên",
              children: outgoing.aiDraftContent
                ? <pre className="document-content-preview">{outgoing.aiDraftContent}</pre>
                : <Empty description="Chưa có bản nháp AI đầu tiên." />,
            },
          ]} />
          {canEdit && <Space>
            <Button type="primary" htmlType="submit" loading={saving} disabled={generating || reviewing}>Lưu</Button>
            <Button onClick={openAiModal} loading={generating} disabled={saving || reviewing}>Sinh nháp AI</Button>
          </Space>}
        </Form>
      </Card>
      <Card
        title="Thẩm định và lịch sử review"
        extra={canReview && <Button type="primary" loading={reviewing} disabled={saving || generating} onClick={() => void submitReview()}>Gửi thẩm định</Button>}
      >
        <Typography.Text strong>Kết quả gần nhất</Typography.Text>
        {outgoing.reviewIssues.length === 0
          ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có lỗi thể thức ở lần review gần nhất." />
          : <List size="small" dataSource={outgoing.reviewIssues} renderItem={issue => <List.Item><Space wrap><ReviewSeverityTag severity={issue.severity} /><Typography.Text code>{issue.ruleCode}</Typography.Text><Typography.Text>{issue.message}</Typography.Text>{issue.location && <Typography.Text type="secondary">{issue.location}</Typography.Text>}</Space></List.Item>} />}
        {reviewError && <Alert className="review-history-error" type="error" showIcon title={reviewError} action={<Button size="small" onClick={() => void loadReviews(reviews?.page ?? 1)}>Thử lại</Button>} />}
        <Table<ReviewResponse>
          className="review-history-table"
          size="small"
          rowKey="id"
          loading={reviewLoading}
          dataSource={reviews?.items ?? []}
          locale={{ emptyText: <Empty description="Chưa có lượt thẩm định nào." /> }}
          columns={[
            { title: "Lần", dataIndex: "attemptNo", width: 70 },
            { title: "Kết quả", dataIndex: "reviewResult", width: 120, render: (result: ReviewResponse["reviewResult"]) => <ReviewResultTag result={result} /> },
            { title: "Nguồn", dataIndex: "reviewSource", width: 110, render: (source: ReviewResponse["reviewSource"]) => <ReviewSourceTag source={source} /> },
            { title: "Người gửi", key: "reviewedByStaff", render: (_, review) => review.reviewedByStaff?.fullName ?? "Hệ thống" },
            { title: "Thời điểm", dataIndex: "reviewedAt", width: 170, render: formatDateTime },
          ]}
          expandable={{ expandedRowRender: review => <Space className="review-history-detail" orientation="vertical" size="middle"><div><Typography.Text strong>Nội dung tại thời điểm thẩm định</Typography.Text><pre className="document-content-preview">{review.contentSnapshot}</pre></div><div><Typography.Text strong>Issues</Typography.Text>{review.reviewIssues.length === 0 ? <Typography.Text type="secondary">Không có issue.</Typography.Text> : <List size="small" dataSource={review.reviewIssues} renderItem={issue => <List.Item><Space wrap><ReviewSeverityTag severity={issue.severity} /><Typography.Text code>{issue.ruleCode}</Typography.Text><Typography.Text>{issue.message}</Typography.Text>{issue.location && <Typography.Text type="secondary">{issue.location}</Typography.Text>}</Space></List.Item>} />}</div><div><Typography.Text strong>Nguồn pháp lý đã dùng</Typography.Text><ReviewCitations citations={review.citations} /></div></Space> }}
          pagination={{ current: reviews?.page ?? 1, pageSize: reviews?.pageSize ?? 20, total: reviews?.totalCount ?? 0, showSizeChanger: false, onChange: page => void loadReviews(page) }}
        />
      </Card>
      <Card title="File đính kèm" extra={canEditAttachments && <Upload {...uploadProps}><Button icon={<UploadOutlined />} loading={uploading}>Thêm file</Button></Upload>}>
        {outgoing.attachments.length === 0 ? <Empty description="Chưa có file đính kèm." /> : <Table<AttachmentResponse> size="small" rowKey="id" pagination={false} dataSource={outgoing.attachments} columns={[
          { title: "Tên file", dataIndex: "fileName", ellipsis: true },
          { title: "Người tải", render: (_, attachment) => attachment.uploadedBy.fullName },
          { title: "Thao tác", render: (_, attachment) => <Space><Button type="link" icon={<DownloadOutlined />} loading={busyId === attachment.id} onClick={() => void download(attachment)}>Tải</Button>{canEditAttachments && <Popconfirm title="Xóa file đính kèm?" okText="Xóa" okButtonProps={{ danger: true }} cancelText="Hủy" onConfirm={() => void remove(attachment)}><Button danger type="link" icon={<DeleteOutlined />} loading={busyId === attachment.id}>Xóa</Button></Popconfirm>}</Space> },
        ]} />}
      </Card>
    </>}
    <Modal
      title="Sinh nháp AI"
      open={aiModalOpen}
      okText="Sinh và lưu nháp"
      cancelText="Hủy"
      confirmLoading={generating}
      closable={!generating}
      mask={{ closable: !generating }}
      onOk={() => void generateDraft()}
      onCancel={() => setAiModalOpen(false)}
    >
      <Typography.Paragraph type="secondary">Kết quả thành công sẽ thay nội dung hiện tại. Bản AI đầu tiên luôn được giữ để so sánh.</Typography.Paragraph>
      <Input.TextArea aria-label="Hướng dẫn bổ sung cho AI" value={instruction} onChange={event => setInstruction(event.target.value)} placeholder="Ví dụ: nhấn mạnh tiến độ thực hiện; không tự đặt số liệu." autoSize={{ minRows: 4, maxRows: 8 }} disabled={generating} />
    </Modal>
  </Space>;
}

function OutgoingStatusTag({ status }: { status: OutgoingDocumentStatus }) { const option = statuses.find(x => x.value === status); return <Tag color={status === "Editing" ? "processing" : status === "Approved" ? "success" : "default"}>{option?.label ?? status}</Tag>; }
function ReviewSeverityTag({ severity }: { severity: string }) { return <Tag color={severity === "Error" ? "error" : severity === "Warning" ? "warning" : "blue"}>{severity}</Tag>; }
function ReviewResultTag({ result }: { result: ReviewResponse["reviewResult"] }) { return <Tag color={result === "Passed" ? "success" : "error"}>{result === "Passed" ? "Đạt" : "Chưa đạt"}</Tag>; }
function ReviewSourceTag({ source }: { source: ReviewResponse["reviewSource"] }) { const labels = { Rule: "Rule", AI: "AI", Hybrid: "Hybrid" }; return <Tag>{labels[source]}</Tag>; }
function isOutgoingEditable(status: OutgoingDocumentStatus) { return status === "AiDraft" || status === "Editing" || status === "ReviewFailed"; }
function readFilters(params: URLSearchParams): OutgoingFilters { const status = statuses.some(x => x.value === params.get("status")) ? params.get("status") as OutgoingDocumentStatus : undefined; return { q: params.get("q") ?? "", templateId: params.get("templateId") ?? undefined, status, dateFrom: params.get("dateFrom") ?? undefined, dateTo: params.get("dateTo") ?? undefined }; }
function toParams(filters: OutgoingFilters, page: number, pageSize: number) { const p = new URLSearchParams(); for (const [k, v] of Object.entries(filters)) if (v) p.set(k, v); if (page > 1) p.set("page", String(page)); if (pageSize !== 20) p.set("pageSize", String(pageSize)); return p; }
function parseIntOr(v: string | null, fallback: number) { const n = Number(v); return Number.isInteger(n) && n > 0 ? n : fallback; }
function formatDateTime(v: string) { const date = new Date(v); return Number.isNaN(date.getTime()) ? v : date.toLocaleString("vi-VN"); }
function errorMessage(error: unknown, fallback: string) { return error instanceof ApiError && error.problem.detail ? error.problem.detail : error instanceof Error && error.message ? error.message : fallback; }
function applyValidationErrors(error: unknown, form: FormInstance) { if (!(error instanceof ApiError) || error.status !== 400) return false; const entries = Object.entries(error.problem.errors ?? {}); if (!entries.length) return false; form.setFields(entries.map(([name, errors]) => ({ name, errors }))); return true; }
function readReturnTo(state: unknown) { return typeof state === "object" && state !== null && "returnTo" in state && typeof state.returnTo === "string" && state.returnTo.startsWith("/outgoing-documents") ? state.returnTo : "/outgoing-documents"; }
function readSuccess(state: unknown): string | null { return typeof state === "object" && state !== null && "success" in state && typeof state.success === "string" ? state.success : null; }
