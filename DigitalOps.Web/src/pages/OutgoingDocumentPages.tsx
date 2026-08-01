import {
  ArrowLeftOutlined, DeleteOutlined, DownloadOutlined, PlusOutlined,
  ReloadOutlined, SearchOutlined, UploadOutlined,
} from "@ant-design/icons";
import {
  Alert, Button, Card, Descriptions, Empty, Form, Input, Popconfirm,
  Select, Space, Table, Tag, Typography, Upload,
  type FormInstance, type TableProps, type UploadProps,
} from "antd";
import { useEffect, useState } from "react";
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
  getOutgoingDocument, getOutgoingDocuments, uploadOutgoingAttachment,
} from "../shared/outgoing-documents/outgoing-document-service";
import type {
  OutgoingDocumentCreateRequest, OutgoingDocumentListParameters,
  OutgoingDocumentResponse, OutgoingDocumentStatus,
} from "../shared/outgoing-documents/types";

type OutgoingFilters = Pick<OutgoingDocumentListParameters, "q" | "templateId" | "status" | "dateFrom" | "dateTo">;
interface CreateValues { templateId: string; title: string; relatedMemberId?: string; relatedIncomingDocumentId?: string; }
const statuses: { value: OutgoingDocumentStatus; label: string }[] = [
  { value: "Editing", label: "Đang soạn" }, { value: "AiDraft", label: "AI draft" },
  { value: "PendingReview", label: "Chờ rà soát" }, { value: "ReviewFailed", label: "Cần chỉnh sửa" },
  { value: "PendingApproval", label: "Chờ duyệt" }, { value: "Approved", label: "Đã duyệt" }, { value: "Archived", label: "Đã lưu trữ" },
];

export function OutgoingDocumentListPage() {
  const { currentUser } = useAuth();
  const isDrafter = currentUser?.roles.includes("Drafter") ?? false;
  const navigate = useNavigate(); const location = useLocation(); const [params, setParams] = useSearchParams();
  const filters = readFilters(params); const [draft, setDraft] = useState(filters);
  const page = parseIntOr(params.get("page"), 1); const pageSize = parseIntOr(params.get("pageSize"), 20);
  const [data, setData] = useState<PagedResponse<OutgoingDocumentResponse> | null>(null);
  const [templates, setTemplates] = useState<DocumentTemplateResponse[]>([]);
  const [loading, setLoading] = useState(true); const [error, setError] = useState<string | null>(null); const [reload, setReload] = useState(0);
  useEffect(() => { setDraft(filters); }, [params.toString()]);
  useEffect(() => { let ignored = false; void (async () => {
    setLoading(true); setError(null);
    try { const [response, activeTemplates] = await Promise.all([
      getOutgoingDocuments({ ...filters, page, pageSize }), getAllDocumentTemplates(),
    ]); if (!ignored) { setData(response); setTemplates(activeTemplates); }
    } catch (e) { if (!ignored) setError(errorMessage(e, "Không thể tải danh sách văn bản đi.")); }
    finally { if (!ignored) setLoading(false); }
  })(); return () => { ignored = true; }; }, [filters.q, filters.templateId, filters.status, filters.dateFrom, filters.dateTo, page, pageSize, reload]);
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
  const { id } = useParams(); const navigate = useNavigate(); const location = useLocation(); const { currentUser } = useAuth(); const [outgoing, setOutgoing] = useState<OutgoingDocumentResponse | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState<string | null>(null); const [uploading, setUploading] = useState(false); const [busyId, setBusyId] = useState<string | null>(null); const [success, setSuccess] = useState<string | null>(readSuccess(location.state));
  const setDocument = setOutgoing;
  const canEditAttachments = !!outgoing && outgoing.status === "Editing" && outgoing.draftedByStaff.id === currentUser?.staff.id && currentUser.roles.includes("Drafter");
  const load = () => { if (!id) return; setLoading(true); void getOutgoingDocument(id).then(setDocument).catch(e => setError(errorMessage(e, "Không thể tải văn bản đi."))).finally(() => setLoading(false)); };
  useEffect(load, [id]);
  const uploadProps: UploadProps = { showUploadList: false, disabled: uploading || !canEditAttachments, customRequest: options => { if (!(options.file instanceof File)) return; setUploading(true); void uploadOutgoingAttachment(id!, options.file).then(() => { setSuccess("Đã tải file lên."); load(); options.onSuccess?.({}, options.file); }).catch(e => { setError(errorMessage(e, "Không thể tải file lên.")); options.onError?.(e instanceof Error ? e : new Error("Upload failed")); }).finally(() => setUploading(false)); } };
  const download = async (attachment: AttachmentResponse) => { setBusyId(attachment.id); try { const result = await downloadOutgoingAttachment(attachment.id); const url = URL.createObjectURL(result.blob); const a = globalThis.document.createElement("a"); a.href = url; a.download = result.fileName; a.click(); URL.revokeObjectURL(url); } catch (e) { setError(errorMessage(e, "Không thể tải file.")); } finally { setBusyId(null); } };
  const remove = async (attachment: AttachmentResponse) => { setBusyId(attachment.id); try { await deleteOutgoingAttachment(attachment.id); setSuccess("Đã xóa file."); load(); } catch (e) { setError(errorMessage(e, "Không thể xóa file.")); } finally { setBusyId(null); } };
  return <Space className="page-stack" orientation="vertical" size="large"><div className="page-heading-row"><div><Typography.Title level={2}>{outgoing?.title ?? "Chi tiết văn bản đi"}</Typography.Title><Typography.Text type="secondary">Nội dung render ban đầu và file đính kèm.</Typography.Text></div><Button icon={<ArrowLeftOutlined />} onClick={() => navigate(readReturnTo(location.state))}>Về danh sách</Button></div>{success && <Alert type="success" showIcon closable title={success} onClose={() => setSuccess(null)} />}{error && <Alert type="error" showIcon title={error} action={!outgoing && <Button size="small" onClick={load}>Thử lại</Button>} />}{outgoing && <><Card title="Thông tin văn bản" extra={<OutgoingStatusTag status={outgoing.status} />}><Descriptions column={1} size="small"><Descriptions.Item label="Tiêu đề">{outgoing.title}</Descriptions.Item><Descriptions.Item label="Mẫu">{outgoing.template.documentType.code} — {outgoing.template.name}</Descriptions.Item><Descriptions.Item label="Hội viên liên quan">{outgoing.relatedMember?.fullName ?? "—"}</Descriptions.Item><Descriptions.Item label="Văn bản đến liên quan">{outgoing.relatedIncomingDocument ? `${outgoing.relatedIncomingDocument.referenceNumber} — ${outgoing.relatedIncomingDocument.summary}` : "—"}</Descriptions.Item><Descriptions.Item label="Người soạn">{outgoing.draftedByStaff.fullName}</Descriptions.Item></Descriptions></Card><Card title="Nội dung đã render"><pre className="document-content-preview">{outgoing.content}</pre></Card><Card title="File đính kèm" extra={canEditAttachments && <Upload {...uploadProps}><Button icon={<UploadOutlined />} loading={uploading}>Thêm file</Button></Upload>}>{outgoing.attachments.length === 0 ? <Empty description="Chưa có file đính kèm." /> : <Table<AttachmentResponse> size="small" rowKey="id" pagination={false} dataSource={outgoing.attachments} columns={[{ title: "Tên file", dataIndex: "fileName", ellipsis: true }, { title: "Người tải", render: (_, a) => a.uploadedBy.fullName }, { title: "Thao tác", render: (_, a) => <Space><Button type="link" icon={<DownloadOutlined />} loading={busyId === a.id} onClick={() => void download(a)}>Tải</Button>{canEditAttachments && <Popconfirm title="Xóa file đính kèm?" okText="Xóa" okButtonProps={{ danger: true }} cancelText="Hủy" onConfirm={() => void remove(a)}><Button danger type="link" icon={<DeleteOutlined />} loading={busyId === a.id}>Xóa</Button></Popconfirm>}</Space> }]} />}</Card></>}</Space>;
}

function OutgoingStatusTag({ status }: { status: OutgoingDocumentStatus }) { const option = statuses.find(x => x.value === status); return <Tag color={status === "Editing" ? "processing" : status === "Approved" ? "success" : "default"}>{option?.label ?? status}</Tag>; }
function readFilters(params: URLSearchParams): OutgoingFilters { const status = statuses.some(x => x.value === params.get("status")) ? params.get("status") as OutgoingDocumentStatus : undefined; return { q: params.get("q") ?? "", templateId: params.get("templateId") ?? undefined, status, dateFrom: params.get("dateFrom") ?? undefined, dateTo: params.get("dateTo") ?? undefined }; }
function toParams(filters: OutgoingFilters, page: number, pageSize: number) { const p = new URLSearchParams(); for (const [k, v] of Object.entries(filters)) if (v) p.set(k, v); if (page > 1) p.set("page", String(page)); if (pageSize !== 20) p.set("pageSize", String(pageSize)); return p; }
function parseIntOr(v: string | null, fallback: number) { const n = Number(v); return Number.isInteger(n) && n > 0 ? n : fallback; }
function formatDateTime(v: string) { const date = new Date(v); return Number.isNaN(date.getTime()) ? v : date.toLocaleString("vi-VN"); }
function errorMessage(error: unknown, fallback: string) { return error instanceof ApiError && error.problem.detail ? error.problem.detail : error instanceof Error && error.message ? error.message : fallback; }
function applyValidationErrors(error: unknown, form: FormInstance) { if (!(error instanceof ApiError) || error.status !== 400) return false; const entries = Object.entries(error.problem.errors ?? {}); if (!entries.length) return false; form.setFields(entries.map(([name, errors]) => ({ name, errors }))); return true; }
function readReturnTo(state: unknown) { return typeof state === "object" && state !== null && "returnTo" in state && typeof state.returnTo === "string" && state.returnTo.startsWith("/outgoing-documents") ? state.returnTo : "/outgoing-documents"; }
function readSuccess(state: unknown): string | null { return typeof state === "object" && state !== null && "success" in state && typeof state.success === "string" ? state.success : null; }
