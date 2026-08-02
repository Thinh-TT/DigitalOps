import {
  CheckOutlined, CloseOutlined, EyeOutlined, ReloadOutlined,
} from "@ant-design/icons";
import {
  Alert, Button, Descriptions, Drawer, Empty, List, Modal, Pagination, Space,
  Table, Tag, Typography,
  type TableProps,
} from "antd";
import { useCallback, useEffect, useState } from "react";
import { ApiError } from "../shared/api/api-client";
import type { PagedResponse } from "../shared/api/types";
import {
  decideOutgoingDocumentApproval, getOutgoingDocument, getOutgoingDocuments,
  getOutgoingReviews,
} from "../shared/outgoing-documents/outgoing-document-service";
import type {
  ApprovalDecision, OutgoingDocumentResponse, ReviewIssueResponse, ReviewResponse,
} from "../shared/outgoing-documents/types";

const pageSize = 20;

export function ApprovalQueuePage() {
  const [page, setPage] = useState(1);
  const [queue, setQueue] = useState<PagedResponse<OutgoingDocumentResponse> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [selected, setSelected] = useState<OutgoingDocumentResponse | null>(null);
  const [history, setHistory] = useState<PagedResponse<ReviewResponse> | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [decisionToConfirm, setDecisionToConfirm] = useState<ApprovalDecision | null>(null);
  const [deciding, setDeciding] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const reloadQueue = useCallback(() => setReloadToken(value => value + 1), []);

  useEffect(() => {
    let ignored = false;
    void (async () => {
      setLoading(true);
      setError(null);
      try {
        const response = await getOutgoingDocuments({ status: "PendingApproval", page, pageSize });
        if (!ignored) setQueue(response);
      } catch (cause) {
        if (!ignored) setError(errorMessage(cause, "Không thể tải hàng chờ duyệt."));
      } finally {
        if (!ignored) setLoading(false);
      }
    })();
    return () => { ignored = true; };
  }, [page, reloadToken]);

  const loadHistory = useCallback(async (documentId: string, targetPage = 1) => {
    setHistoryLoading(true);
    setHistoryError(null);
    try {
      const response = await getOutgoingReviews(documentId, { page: targetPage, pageSize });
      setHistory(response);
      setHistoryPage(targetPage);
    } catch (cause) {
      setHistoryError(errorMessage(cause, "Không thể tải lịch sử thẩm định."));
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  const openDrawer = (document: OutgoingDocumentResponse) => {
    setSelected(document);
    setHistory(null);
    setHistoryPage(1);
    setHistoryError(null);
    setActionError(null);
    void loadHistory(document.id);
  };

  const closeDrawer = () => {
    if (deciding) return;
    setSelected(null);
    setHistory(null);
    setDecisionToConfirm(null);
  };

  const refreshAfterConflict = async (documentId: string) => {
    try {
      const [latestQueue, current] = await Promise.all([
        getOutgoingDocuments({ status: "PendingApproval", page, pageSize }),
        getOutgoingDocument(documentId),
      ]);
      setQueue(latestQueue);
      if (current.status !== "PendingApproval") {
        setSelected(null);
        setHistory(null);
      } else {
        setSelected(current);
        await loadHistory(current.id, historyPage);
      }
    } catch {
      reloadQueue();
    }
  };

  const decide = async () => {
    if (!selected || !decisionToConfirm) return;
    const documentId = selected.id;
    const decision = decisionToConfirm;
    setDeciding(true);
    setActionError(null);
    try {
      const updated = await decideOutgoingDocumentApproval(documentId, { decision });
      if (updated.status !== "PendingApproval") {
        setSelected(null);
        setHistory(null);
      }
      setDecisionToConfirm(null);
      setSuccess(decision === "Approve"
        ? "Đã phê duyệt văn bản."
        : "Đã trả văn bản về trạng thái chỉnh sửa.");
      reloadQueue();
    } catch (cause) {
      setActionError(errorMessage(cause, "Không thể xử lý quyết định phê duyệt."));
      setDecisionToConfirm(null);
      if (cause instanceof ApiError && cause.status === 409) {
        await refreshAfterConflict(documentId);
      }
    } finally {
      setDeciding(false);
    }
  };

  const columns: TableProps<OutgoingDocumentResponse>["columns"] = [
    { title: "Tiêu đề", dataIndex: "title", ellipsis: true },
    { title: "Mẫu", key: "template", width: 220, render: (_, item) => `${item.template.documentType.code} — ${item.template.name}` },
    { title: "Người soạn", key: "drafter", width: 180, render: (_, item) => item.draftedByStaff.fullName },
    { title: "Cập nhật", dataIndex: "updatedAt", width: 170, render: formatDateTime },
    { title: "Thao tác", key: "actions", width: 110, render: (_, item) => <Button type="link" icon={<EyeOutlined />} onClick={() => openDrawer(item)}>Xem xét</Button> },
  ];

  return <Space className="page-stack" orientation="vertical" size="large">
    <div className="page-heading-row">
      <div>
        <Typography.Title level={2}>Hàng chờ duyệt</Typography.Title>
        <Typography.Text type="secondary">Văn bản đã đạt thẩm định và đang chờ lãnh đạo quyết định.</Typography.Text>
      </div>
      <Button icon={<ReloadOutlined />} onClick={reloadQueue} loading={loading}>Tải lại</Button>
    </div>
    {success && <Alert type="success" showIcon closable title={success} onClose={() => setSuccess(null)} />}
    {actionError && <Alert type="error" showIcon closable title={actionError} onClose={() => setActionError(null)} />}
    {error
      ? <Alert type="error" showIcon title={error} action={<Button size="small" onClick={reloadQueue}>Thử lại</Button>} />
      : <Table<OutgoingDocumentResponse>
          rowKey="id"
          loading={loading}
          dataSource={queue?.items ?? []}
          columns={columns}
          locale={{ emptyText: <Empty description="Không có văn bản nào đang chờ duyệt." /> }}
          pagination={{
            current: queue?.page ?? page,
            pageSize: queue?.pageSize ?? pageSize,
            total: queue?.totalCount ?? 0,
            showSizeChanger: false,
            onChange: targetPage => setPage(targetPage),
          }}
        />}
    <Drawer
      title="Xem xét văn bản chờ duyệt"
      size="large"
      open={selected !== null}
      onClose={closeDrawer}
      closable={!deciding}
      mask={{ closable: !deciding }}
      extra={selected && <Space>
        <Button danger icon={<CloseOutlined />} disabled={deciding} onClick={() => setDecisionToConfirm("Return")}>Trả lại chỉnh sửa</Button>
        <Button type="primary" icon={<CheckOutlined />} disabled={deciding} loading={deciding} onClick={() => setDecisionToConfirm("Approve")}>Duyệt văn bản</Button>
      </Space>}
    >
      {selected && <ApprovalDrawerContent
        document={selected}
        history={history}
        historyLoading={historyLoading}
        historyError={historyError}
        historyPage={historyPage}
        onHistoryPageChange={targetPage => void loadHistory(selected.id, targetPage)}
        onRetryHistory={() => void loadHistory(selected.id, historyPage)}
      />}
    </Drawer>
    <Modal
      title={decisionToConfirm === "Approve" ? "Xác nhận phê duyệt" : "Xác nhận trả lại chỉnh sửa"}
      open={decisionToConfirm !== null}
      okText={decisionToConfirm === "Approve" ? "Xác nhận duyệt" : "Xác nhận trả lại"}
      okButtonProps={{ danger: decisionToConfirm === "Return" }}
      cancelText="Hủy"
      confirmLoading={deciding}
      closable={!deciding}
      mask={{ closable: !deciding }}
      onOk={() => void decide()}
      onCancel={() => { if (!deciding) setDecisionToConfirm(null); }}
    >
      <Typography.Paragraph>
        {decisionToConfirm === "Approve"
          ? "Văn bản sẽ chuyển sang trạng thái Đã duyệt để Văn thư phát hành ở bước tiếp theo."
          : "Văn bản sẽ trở về trạng thái Đang chỉnh sửa. Người soạn phải thẩm định lại trước khi trình duyệt."}
      </Typography.Paragraph>
    </Modal>
  </Space>;
}

function ApprovalDrawerContent({
  document, history, historyLoading, historyError, historyPage, onHistoryPageChange, onRetryHistory,
}: {
  document: OutgoingDocumentResponse;
  history: PagedResponse<ReviewResponse> | null;
  historyLoading: boolean;
  historyError: string | null;
  historyPage: number;
  onHistoryPageChange: (page: number) => void;
  onRetryHistory: () => void;
}) {
  return <Space className="page-stack" orientation="vertical" size="large">
    <Descriptions column={1} size="small" bordered>
      <Descriptions.Item label="Trạng thái"><Tag color="processing">Chờ duyệt</Tag></Descriptions.Item>
      <Descriptions.Item label="Mẫu">{document.template.documentType.code} — {document.template.name}</Descriptions.Item>
      <Descriptions.Item label="Người soạn">{document.draftedByStaff.fullName}</Descriptions.Item>
      <Descriptions.Item label="Hội viên liên quan">{document.relatedMember?.fullName ?? "—"}</Descriptions.Item>
      <Descriptions.Item label="Văn bản đến liên quan">{document.relatedIncomingDocument ? `${document.relatedIncomingDocument.referenceNumber} — ${document.relatedIncomingDocument.summary}` : "—"}</Descriptions.Item>
    </Descriptions>
    <ContentSection title="Nội dung hiện tại" content={document.content} />
    {document.aiDraftContent && <ContentSection title="Bản AI đầu tiên" content={document.aiDraftContent} />}
    <section>
      <Typography.Title level={5}>Kết quả thẩm định gần nhất</Typography.Title>
      <ReviewIssues issues={document.reviewIssues} empty="Lần review đạt không có issue thể thức." />
    </section>
    <section>
      <Typography.Title level={5}>Lịch sử thẩm định</Typography.Title>
      {historyError && <Alert type="error" showIcon title={historyError} action={<Button size="small" onClick={onRetryHistory}>Thử lại</Button>} />}
      <List<ReviewResponse>
        loading={historyLoading}
        dataSource={history?.items ?? []}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có lịch sử thẩm định." /> }}
        renderItem={review => <List.Item>
          <Space className="page-stack" orientation="vertical" size="small">
            <Space wrap>
              <Typography.Text strong>Lần {review.attemptNo}</Typography.Text>
              <Tag color={review.reviewResult === "Passed" ? "success" : "error"}>{review.reviewResult === "Passed" ? "Đạt" : "Chưa đạt"}</Tag>
              <Tag>{review.reviewSource}</Tag>
              <Typography.Text type="secondary">{formatDateTime(review.reviewedAt)}</Typography.Text>
            </Space>
            <ReviewIssues issues={review.reviewIssues} empty="Không có issue." />
            <ContentSection title="Snapshot nội dung" content={review.contentSnapshot} />
          </Space>
        </List.Item>}
      />
      {(history?.totalCount ?? 0) > pageSize && <Pagination
        current={historyPage}
        pageSize={pageSize}
        total={history?.totalCount}
        showSizeChanger={false}
        onChange={onHistoryPageChange}
      />}
    </section>
  </Space>;
}

function ContentSection({ title, content }: { title: string; content: string }) {
  return <section>
    <Typography.Title level={5}>{title}</Typography.Title>
    <pre className="document-content-preview">{content}</pre>
  </section>;
}

function ReviewIssues({ issues, empty }: { issues: ReviewIssueResponse[]; empty: string }) {
  return issues.length === 0
    ? <Typography.Text type="secondary">{empty}</Typography.Text>
    : <List
        size="small"
        dataSource={issues}
        renderItem={issue => <List.Item><Space wrap><Tag color={issue.severity === "Error" ? "error" : issue.severity === "Warning" ? "warning" : "blue"}>{issue.severity}</Tag><Typography.Text code>{issue.ruleCode}</Typography.Text><Typography.Text>{issue.message}</Typography.Text>{issue.location && <Typography.Text type="secondary">{issue.location}</Typography.Text>}</Space></List.Item>}
      />;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError && error.problem.detail
    ? error.problem.detail
    : error instanceof Error && error.message ? error.message : fallback;
}
