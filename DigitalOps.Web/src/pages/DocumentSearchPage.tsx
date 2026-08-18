import {
  ArrowRightOutlined,
  ClearOutlined,
  FileSearchOutlined,
  FileTextOutlined,
  SearchOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Badge,
  Button,
  Card,
  DatePicker,
  Divider,
  Empty,
  Input,
  Pagination,
  Select,
  Space,
  Spin,
  Tag,
  Typography,
} from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";
import { ApiError } from "../shared/api/api-client";
import { getAllDocumentTypes } from "../shared/document-catalog/document-catalog-service";
import type { DocumentTypeResponse } from "../shared/document-catalog/types";
import type { IncomingDocumentStatus } from "../shared/incoming-documents/types";
import type { OutgoingDocumentStatus } from "../shared/outgoing-documents/types";
import { searchDocuments } from "../shared/search/document-search-service";
import type {
  DocumentKind,
  DocumentSearchMatchSource,
  DocumentSearchQuery,
  DocumentSearchResult,
} from "../shared/search/types";

const { Title, Text, Paragraph } = Typography;
const { RangePicker } = DatePicker;

export function DocumentSearchPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();

  // Search input & filter states
  const [keyword, setKeyword] = useState(searchParams.get("q") ?? "");
  const [documentKind, setDocumentKind] = useState<DocumentKind | undefined>(
    (searchParams.get("documentKind") as DocumentKind) || undefined,
  );
  const [documentTypeId, setDocumentTypeId] = useState<string | undefined>(
    searchParams.get("documentTypeId") || undefined,
  );
  const [incomingStatus, setIncomingStatus] = useState<IncomingDocumentStatus | undefined>(
    (searchParams.get("incomingStatus") as IncomingDocumentStatus) || undefined,
  );
  const [outgoingStatus, setOutgoingStatus] = useState<OutgoingDocumentStatus | undefined>(
    (searchParams.get("outgoingStatus") as OutgoingDocumentStatus) || undefined,
  );
  const [matchSource, setMatchSource] = useState<DocumentSearchMatchSource | undefined>(
    (searchParams.get("matchSource") as DocumentSearchMatchSource) || undefined,
  );

  const initialDateFrom = searchParams.get("dateFrom");
  const initialDateTo = searchParams.get("dateTo");
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null] | null>(
    initialDateFrom && initialDateTo
      ? [dayjs(initialDateFrom), dayjs(initialDateTo)]
      : null,
  );

  const [page, setPage] = useState(Number(searchParams.get("page")) || 1);
  const [pageSize, setPageSize] = useState(Number(searchParams.get("pageSize")) || 20);

  // UI state
  const [validationError, setValidationError] = useState<string | null>(null);
  const [documentTypes, setDocumentTypes] = useState<DocumentTypeResponse[]>([]);
  const [results, setResults] = useState<DocumentSearchResult[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasSearched, setHasSearched] = useState(false);

  // Load document types on mount
  useEffect(() => {
    let ignored = false;
    void (async () => {
      await Promise.resolve();
      if (ignored) return;
      try {
        const types = await getAllDocumentTypes();
        if (!ignored) {
          setDocumentTypes(types);
        }
      } catch {
        // Non-blocking error for filter options
      }
    })();
    return () => {
      ignored = true;
    };
  }, []);

  // Execute search function
  const executeSearch = useCallback(
    async (
      q: string,
      queryOverrides: Partial<DocumentSearchQuery> = {},
    ) => {
      const trimmed = q.trim();
      if (trimmed.length < 2) {
        setValidationError("Từ khóa tìm kiếm phải có tối thiểu 2 ký tự.");
        return;
      }

      setValidationError(null);
      setError(null);
      setLoading(true);

      const targetKind =
        queryOverrides.documentKind !== undefined ? queryOverrides.documentKind : documentKind;
      const targetTypeId =
        queryOverrides.documentTypeId !== undefined ? queryOverrides.documentTypeId : documentTypeId;
      const targetIncomingStatus =
        queryOverrides.incomingStatus !== undefined ? queryOverrides.incomingStatus : incomingStatus;
      const targetOutgoingStatus =
        queryOverrides.outgoingStatus !== undefined ? queryOverrides.outgoingStatus : outgoingStatus;
      const targetMatchSource =
        queryOverrides.matchSource !== undefined ? queryOverrides.matchSource : matchSource;
      const targetPage = queryOverrides.page !== undefined ? queryOverrides.page : page;
      const targetPageSize = queryOverrides.pageSize !== undefined ? queryOverrides.pageSize : pageSize;

      const queryPayload: DocumentSearchQuery = {
        q: trimmed,
        documentKind: targetKind,
        documentTypeId: targetTypeId,
        incomingStatus: targetIncomingStatus,
        outgoingStatus: targetOutgoingStatus,
        matchSource: targetMatchSource,
        dateFrom: queryOverrides.dateFrom ?? (dateRange?.[0] ? dateRange[0].format("YYYY-MM-DD") : undefined),
        dateTo: queryOverrides.dateTo ?? (dateRange?.[1] ? dateRange[1].format("YYYY-MM-DD") : undefined),
        page: targetPage,
        pageSize: targetPageSize,
      };

      // Sync with URL params
      const newParams = new URLSearchParams();
      newParams.set("q", trimmed);
      if (targetKind) newParams.set("documentKind", targetKind);
      if (targetTypeId) newParams.set("documentTypeId", targetTypeId);
      if (targetIncomingStatus) newParams.set("incomingStatus", targetIncomingStatus);
      if (targetOutgoingStatus) newParams.set("outgoingStatus", targetOutgoingStatus);
      if (targetMatchSource) newParams.set("matchSource", targetMatchSource);
      if (queryPayload.dateFrom) newParams.set("dateFrom", queryPayload.dateFrom);
      if (queryPayload.dateTo) newParams.set("dateTo", queryPayload.dateTo);
      if (targetPage > 1) newParams.set("page", String(targetPage));
      if (targetPageSize !== 20) newParams.set("pageSize", String(targetPageSize));
      setSearchParams(newParams, { replace: true });

      try {
        const response = await searchDocuments(queryPayload);
        setResults(response.items);
        setTotalCount(response.totalCount);
        setTotalPages(response.totalPages);
        setHasSearched(true);
      } catch (err) {
        if (err instanceof ApiError) {
          setError(err.problem?.detail || err.message || "Lỗi khi thực hiện tìm kiếm.");
        } else {
          setError("Không thể kết nối đến máy chủ.");
        }
      } finally {
        setLoading(false);
      }
    },
    [
      page,
      pageSize,
      documentKind,
      documentTypeId,
      incomingStatus,
      outgoingStatus,
      matchSource,
      dateRange,
      setSearchParams,
    ],
  );

  // Auto-search if q param present in URL on mount
  const initialSearchDoneRef = useRef(false);
  useEffect(() => {
    let ignored = false;
    void (async () => {
      await Promise.resolve();
      if (ignored || initialSearchDoneRef.current) return;
      const qFromUrl = searchParams.get("q");
      if (qFromUrl && qFromUrl.trim().length >= 2) {
        initialSearchDoneRef.current = true;
        void executeSearch(qFromUrl);
      }
    })();
    return () => {
      ignored = true;
    };
  }, [searchParams, executeSearch]);

  const handleSearchSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    initialSearchDoneRef.current = true;
    setPage(1);
    executeSearch(keyword, { page: 1 });
  };

  const handleResetFilters = () => {
    initialSearchDoneRef.current = true;
    setKeyword("");
    setDocumentKind(undefined);
    setDocumentTypeId(undefined);
    setIncomingStatus(undefined);
    setOutgoingStatus(undefined);
    setMatchSource(undefined);
    setDateRange(null);
    setPage(1);
    setPageSize(20);
    setResults([]);
    setTotalCount(0);
    setTotalPages(0);
    setHasSearched(false);
    setValidationError(null);
    setError(null);
    setSearchParams({}, { replace: true });
  };

  const handlePageChange = (newPage: number, newPageSize: number) => {
    setPage(newPage);
    setPageSize(newPageSize);
    executeSearch(keyword, { page: newPage, pageSize: newPageSize });
  };

  const handleOpenDetail = (item: DocumentSearchResult) => {
    const path =
      item.documentKind === "Incoming"
        ? `/incoming-documents/${item.documentId}`
        : `/outgoing-documents/${item.documentId}`;

    navigate(path, {
      state: { returnTo: location.pathname + location.search },
    });
  };

  const renderMatchSourceTag = (source: DocumentSearchMatchSource) => {
    switch (source) {
      case "Summary":
        return <Tag color="cyan">Khớp trích yếu</Tag>;
      case "Title":
        return <Tag color="purple">Khớp tiêu đề</Tag>;
      case "Content":
        return <Tag color="geekblue">Khớp nội dung</Tag>;
      case "AiDraftContent":
        return <Tag color="magenta">Khớp bản thảo AI</Tag>;
      case "Attachment":
        return <Tag color="orange">Khớp tệp đính kèm</Tag>;
      default:
        return <Tag>{source}</Tag>;
    }
  };

  return (
    <Space direction="vertical" size="large" className="page-stack" style={{ width: "100%" }}>
      {/* Page Header */}
      <div className="page-heading-row">
        <div>
          <Title level={3} style={{ marginBottom: 4 }}>
            <FileSearchOutlined style={{ marginRight: 8, color: "#1677ff" }} />
            Tìm kiếm toàn văn
          </Title>
          <Text type="secondary">
            Tra cứu toàn bộ văn bản đến, văn bản đi và nội dung trích xuất từ tệp đính kèm (DOCX, XLSX, PDF).
          </Text>
        </div>
      </div>

      {/* Search & Filter Card */}
      <Card className="search-card" style={{ boxShadow: "0 1px 2px rgba(0,0,0,0.03)" }}>
        <form onSubmit={handleSearchSubmit}>
          <Space direction="vertical" size="middle" style={{ width: "100%" }}>
            {/* Primary Search Bar */}
            <div style={{ display: "flex", gap: 12, alignItems: "flex-start" }}>
              <div style={{ flex: 1 }}>
                <Input
                  size="large"
                  placeholder="Nhập từ khóa tìm kiếm văn bản hoặc nội dung tệp (tối thiểu 2 ký tự)..."
                  prefix={<SearchOutlined style={{ color: "#bfbfbf" }} />}
                  value={keyword}
                  onChange={(e) => {
                    setKeyword(e.target.value);
                    if (validationError && e.target.value.trim().length >= 2) {
                      setValidationError(null);
                    }
                  }}
                  allowClear
                  status={validationError ? "error" : ""}
                />
                {validationError && (
                  <Text type="danger" style={{ fontSize: 12, marginTop: 4, display: "block" }}>
                    {validationError}
                  </Text>
                )}
              </div>
              <Button
                type="primary"
                size="large"
                icon={<SearchOutlined />}
                onClick={() => handleSearchSubmit()}
                loading={loading}
              >
                Tìm kiếm
              </Button>
              <Button
                size="large"
                icon={<ClearOutlined />}
                onClick={handleResetFilters}
              >
                Xóa bộ lọc
              </Button>
            </div>

            <Divider style={{ margin: "12px 0 8px 0" }} />

            {/* Filter Row */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: 12,
              }}
            >
              {/* Document Kind */}
              <div>
                <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                  Chiều văn bản
                </Text>
                <Select
                  style={{ width: "100%" }}
                  placeholder="Tất cả chiều văn bản"
                  allowClear
                  value={documentKind}
                  onChange={(val) => setDocumentKind(val)}
                  options={[
                    { label: "Tất cả văn bản", value: "" },
                    { label: "Văn bản đến", value: "Incoming" },
                    { label: "Văn bản đi", value: "Outgoing" },
                  ]}
                />
              </div>

              {/* Document Type */}
              <div>
                <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                  Loại văn bản
                </Text>
                <Select
                  style={{ width: "100%" }}
                  placeholder="Tất cả loại văn bản"
                  allowClear
                  value={documentTypeId}
                  onChange={(val) => setDocumentTypeId(val)}
                  options={documentTypes.map((t) => ({
                    label: `${t.name} (${t.code})`,
                    value: t.id,
                  }))}
                />
              </div>

              {/* Match Source */}
              <div>
                <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                  Nguồn khớp nội dung
                </Text>
                <Select
                  style={{ width: "100%" }}
                  placeholder="Tất cả nguồn khớp"
                  allowClear
                  value={matchSource}
                  onChange={(val) => setMatchSource(val)}
                  options={[
                    { label: "Tất cả nguồn khớp", value: "" },
                    { label: "Trích yếu (Văn bản đến)", value: "Summary" },
                    { label: "Tiêu đề (Văn bản đi)", value: "Title" },
                    { label: "Nội dung (Văn bản đi)", value: "Content" },
                    { label: "Bản thảo AI (Văn bản đi)", value: "AiDraftContent" },
                    { label: "Tệp đính kèm", value: "Attachment" },
                  ]}
                />
              </div>

              {/* Incoming Status (Visible if not Outgoing only) */}
              {documentKind !== "Outgoing" && (
                <div>
                  <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                    Trạng thái VB đến
                  </Text>
                  <Select
                    style={{ width: "100%" }}
                    placeholder="Tất cả trạng thái VB đến"
                    allowClear
                    value={incomingStatus}
                    onChange={(val) => setIncomingStatus(val)}
                    options={[
                      { label: "Mới tiếp nhận (New)", value: "New" },
                      { label: "Đang xử lý (InProgress)", value: "InProgress" },
                      { label: "Quá hạn (Overdue)", value: "Overdue" },
                      { label: "Hoàn tất (Completed)", value: "Completed" },
                    ]}
                  />
                </div>
              )}

              {/* Outgoing Status (Visible if not Incoming only) */}
              {documentKind !== "Incoming" && (
                <div>
                  <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                    Trạng thái VB đi
                  </Text>
                  <Select
                    style={{ width: "100%" }}
                    placeholder="Tất cả trạng thái VB đi"
                    allowClear
                    value={outgoingStatus}
                    onChange={(val) => setOutgoingStatus(val)}
                    options={[
                      { label: "Bản thảo AI (AiDraft)", value: "AiDraft" },
                      { label: "Đang soạn (Editing)", value: "Editing" },
                      { label: "Chờ thẩm tra (PendingReview)", value: "PendingReview" },
                      { label: "Thẩm tra không đạt (ReviewFailed)", value: "ReviewFailed" },
                      { label: "Chờ phê duyệt (PendingApproval)", value: "PendingApproval" },
                      { label: "Đã phê duyệt (Approved)", value: "Approved" },
                      { label: "Đã ban hành / Lưu trữ (Archived)", value: "Archived" },
                    ]}
                  />
                </div>
              )}

              {/* Date Range */}
              <div>
                <Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
                  Khoảng ngày
                </Text>
                <RangePicker
                  style={{ width: "100%" }}
                  format="YYYY-MM-DD"
                  value={dateRange}
                  onChange={(dates) => setDateRange(dates)}
                />
              </div>
            </div>
          </Space>
        </form>
      </Card>

      {/* Error alert */}
      {error && <Alert type="error" message={error} showIcon closable onClose={() => setError(null)} />}

      {/* Loading state */}
      {loading && (
        <div style={{ textAlign: "center", padding: "48px 0" }}>
          <Spin size="large" />
          <div style={{ marginTop: 12, color: "#8c8c8c" }}>Đang tìm kiếm toàn văn...</div>
        </div>
      )}

      {/* Search results */}
      {!loading && hasSearched && (
        <Space direction="vertical" size="middle" style={{ width: "100%" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <Text type="secondary">
              Tìm thấy <Text strong style={{ color: "#1677ff" }}>{totalCount}</Text> kết quả
              {keyword ? ` cho từ khóa "${keyword.trim()}"` : ""}.
            </Text>
            {totalPages > 1 && (
              <Text type="secondary" style={{ fontSize: 12 }}>
                Trang {page} / {totalPages}
              </Text>
            )}
          </div>

          {results.length === 0 ? (
            <Card>
              <Empty
                description="Không tìm thấy văn bản phù hợp với từ khóa và bộ lọc đã chọn."
                style={{ padding: "32px 0" }}
              />
            </Card>
          ) : (
            results.map((item) => (
              <Card
                key={`${item.documentKind}-${item.documentId}`}
                hoverable
                style={{ borderRadius: 8, transition: "all 0.2s" }}
                styles={{ body: { padding: "16px 20px" } }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16 }}>
                  <div style={{ flex: 1 }}>
                    {/* Tags row */}
                    <Space size={8} wrap style={{ marginBottom: 6 }}>
                      {item.documentKind === "Incoming" ? (
                        <Tag color="blue" icon={<FileTextOutlined />}>
                          Văn bản đến
                        </Tag>
                      ) : (
                        <Tag color="green" icon={<FileTextOutlined />}>
                          Văn bản đi
                        </Tag>
                      )}
                      <Tag style={{ fontWeight: 600 }}>{item.referenceNumber || "(Chưa cấp số)"}</Tag>
                      <Tag>{item.documentType}</Tag>
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {item.documentDate}
                      </Text>
                      {renderMatchSourceTag(item.matchSource)}
                      <Badge
                        count={`Độ khớp ${(item.score * 100).toFixed(0)}%`}
                        style={{
                          backgroundColor: item.score >= 0.8 ? "#52c41a" : item.score >= 0.6 ? "#faad14" : "#8c8c8c",
                          fontSize: 11,
                        }}
                      />
                    </Space>

                    {/* Title */}
                    <Title level={5} style={{ margin: "4px 0 8px 0", color: "#1f1f1f" }}>
                      {item.title}
                    </Title>

                    {/* Snippet */}
                    <div
                      className="search-snippet"
                      style={{
                        background: "#fafafa",
                        borderLeft: "3px solid #1677ff",
                        padding: "8px 12px",
                        borderRadius: "0 4px 4px 0",
                        fontSize: 13.5,
                        color: "#595959",
                        marginTop: 8,
                      }}
                    >
                      {renderSafeSnippet(item.snippet)}
                    </div>
                  </div>

                  {/* Detail Link Button */}
                  <div style={{ alignSelf: "center" }}>
                    <Button
                      type="default"
                      icon={<ArrowRightOutlined />}
                      onClick={() => handleOpenDetail(item)}
                      style={{ minWidth: 110 }}
                    >
                      Mở chi tiết
                    </Button>
                  </div>
                </div>
              </Card>
            ))
          )}

          {/* Pagination */}
          {totalCount > 0 && (
            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: 12 }}>
              <Pagination
                current={page}
                pageSize={pageSize}
                total={totalCount}
                showSizeChanger
                pageSizeOptions={["10", "20", "50", "100"]}
                onChange={handlePageChange}
                showTotal={(total, range) => `${range[0]}-${range[1]} / ${total} kết quả`}
              />
            </div>
          )}
        </Space>
      )}

      {/* Initial state placeholder when no search performed yet */}
      {!hasSearched && !loading && (
        <Card style={{ textAlign: "center", padding: "48px 0", background: "#fff" }}>
          <FileSearchOutlined style={{ fontSize: 48, color: "#d9d9d9", marginBottom: 16 }} />
          <Title level={4} style={{ color: "#595959", marginBottom: 8 }}>
            Tra cứu văn bản và tệp đính kèm
          </Title>
          <Paragraph type="secondary" style={{ maxWidth: 520, margin: "0 auto" }}>
            Nhập từ khóa và tùy chọn bộ lọc ở phía trên để tìm kiếm toàn văn trên tiêu đề, trích yếu, nội dung văn bản đi và nội dung trích xuất từ tệp đính kèm.
          </Paragraph>
        </Card>
      )}
    </Space>
  );
}

function renderSafeSnippet(snippet: string): ReactNode {
  return snippet.split(/(<b>[\s\S]*?<\/b>)/gi).map((part, index) => {
    const match = /^<b>([\s\S]*?)<\/b>$/i.exec(part);
    return match
      ? <strong key={`highlight-${index}`}>{match[1]}</strong>
      : <span key={`text-${index}`}>{part}</span>;
  });
}
