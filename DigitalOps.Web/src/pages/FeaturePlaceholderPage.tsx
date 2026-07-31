import { Alert, Card, Space, Typography } from "antd";

interface FeaturePlaceholderPageProps {
  screen: string;
  title: string;
  description: string;
}

export function FeaturePlaceholderPage({
  screen,
  title,
  description,
}: FeaturePlaceholderPageProps) {
  return (
    <section aria-labelledby="page-title">
      <Space direction="vertical" size="large" className="page-stack">
        <div>
          <Typography.Text type="secondary">{screen}</Typography.Text>
          <Typography.Title id="page-title" level={2}>
            {title}
          </Typography.Title>
          <Typography.Paragraph type="secondary">
            {description}
          </Typography.Paragraph>
        </div>
        <Card>
          <Alert
            type="info"
            showIcon
            message="Route và phân quyền đã sẵn sàng"
            description="Nội dung nghiệp vụ của màn hình sẽ được bổ sung trong task tương ứng."
          />
        </Card>
      </Space>
    </section>
  );
}
