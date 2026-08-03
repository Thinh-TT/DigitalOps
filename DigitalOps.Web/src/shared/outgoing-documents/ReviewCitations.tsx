import { Alert, List, Space, Tag, Typography } from "antd";
import type { ReviewCitationResponse } from "./types";

export function ReviewCitations({ citations }: { citations: ReviewCitationResponse[] }) {
  if (citations.length === 0) {
    return <Typography.Text type="secondary">Không có nguồn pháp lý được trích dẫn.</Typography.Text>;
  }

  return <List
    className="review-citations"
    size="small"
    dataSource={citations}
    renderItem={citation => <List.Item>
      <Space className="review-citation" orientation="vertical" size={4}>
        <Space wrap>
          <Typography.Link href={citation.sourceUrl} target="_blank" rel="noreferrer">
            {citation.documentNumber ? `${citation.documentNumber} — ${citation.title}` : citation.title}
          </Typography.Link>
          <Tag color={citation.sourceTrustTier === "official" ? "green" : "blue"}>
            {citation.sourceTrustTier === "official" ? "Nguồn chính thức" : "Bản sao đã xác minh"}
          </Tag>
          <Tag>{legalStatusLabel(citation.legalStatus)}</Tag>
        </Space>
        <Typography.Text type="secondary">
          {citation.issuer ?? "Chưa rõ cơ quan ban hành"} · Phiên bản {citation.sourceVersion}
          {citation.effectiveFrom ? ` · Hiệu lực từ ${formatDate(citation.effectiveFrom)}` : ""}
          {citation.effectiveTo ? ` đến ${formatDate(citation.effectiveTo)}` : ""}
        </Typography.Text>
        {citation.isEffectivityUnknown && <Alert
          type="warning"
          showIcon
          title="Chưa xác định đủ thời gian hiệu lực; cần kiểm tra văn bản gốc trước khi phê duyệt."
        />}
      </Space>
    </List.Item>}
  />;
}

function legalStatusLabel(status: string) {
  const labels: Record<string, string> = {
    current: "Còn hiệu lực",
    expired: "Hết hiệu lực",
    repealed: "Bị bãi bỏ",
    superseded: "Đã được thay thế",
    status_unknown: "Chưa rõ hiệu lực",
  };
  return labels[status.toLowerCase()] ?? status;
}

function formatDate(value: string) {
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("vi-VN");
}
