import {
  ArrowLeftOutlined,
  DeleteOutlined,
  DownloadOutlined,
  FileExcelOutlined,
  UploadOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Space,
  Table,
  Typography,
  Upload,
  type TableProps,
} from "antd";
import { useState } from "react";
import { useNavigate } from "react-router";
import { ApiError } from "../shared/api/api-client";
import {
  downloadMemberImportTemplate,
  importMembers,
} from "../shared/members/member-service";
import type {
  MemberImportProblemDetails,
  MemberImportResult,
  MemberImportRowError,
} from "../shared/members/types";

const defaultMaxFileSizeBytes = 10 * 1024 * 1024;
const defaultTemplateFileName = "DigitalOps-Member-Import-Template.xlsx";

const fieldLabels: Record<string, string> = {
  file: "Tệp",
  fullName: "Họ và tên",
  dateOfBirth: "Ngày sinh",
  gender: "Giới tính",
  address: "Địa chỉ",
  phone: "Số điện thoại",
  email: "Email",
  position: "Chức vụ",
  joinDate: "Ngày gia nhập",
  status: "Trạng thái",
  notes: "Ghi chú",
  duplicateKey: "Họ tên + Ngày sinh + Điện thoại",
};

export function MemberImportPage() {
  const navigate = useNavigate();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<MemberImportRowError[]>([]);
  const [result, setResult] = useState<MemberImportResult | null>(null);

  const columns: TableProps<MemberImportRowError>["columns"] = [
    {
      title: "Dòng",
      dataIndex: "rowNumber",
      key: "rowNumber",
      width: 90,
      render: (rowNumber: number) => rowNumber === 0 ? "Tệp" : rowNumber,
    },
    {
      title: "Cột",
      dataIndex: "field",
      key: "field",
      width: 240,
      render: (field: string) => fieldLabels[field] ?? field,
    },
    {
      title: "Nguyên nhân",
      dataIndex: "message",
      key: "message",
    },
  ];

  const handleDownload = async () => {
    setDownloading(true);
    setErrorMessage(null);
    try {
      const downloaded = await downloadMemberImportTemplate();
      triggerDownload(
        downloaded.blob,
        downloaded.fileName ?? defaultTemplateFileName,
      );
    } catch (error) {
      setErrorMessage(getErrorMessage(
        error,
        "Không thể tải template. Vui lòng thử lại.",
      ));
    } finally {
      setDownloading(false);
    }
  };

  const handleImport = async () => {
    if (selectedFile === null) {
      setErrorMessage("Vui lòng chọn một file XLSX để import.");
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    setRowErrors([]);
    setResult(null);
    try {
      const response = await importMembers(selectedFile);
      setResult(response);
      setSelectedFile(null);
    } catch (error) {
      const importErrors = readImportErrors(error);
      if (importErrors !== null) {
        setRowErrors(importErrors);
        setErrorMessage(
          "File có lỗi; hệ thống không import bất kỳ hội viên nào.",
        );
      } else {
        setErrorMessage(getImportErrorMessage(error));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space className="page-stack" orientation="vertical" size="large">
      <div className="page-heading-row">
        <div>
          <Typography.Title level={2}>Import hội viên</Typography.Title>
          <Typography.Text type="secondary">
            Nhập toàn bộ hồ sơ hợp lệ từ template XLSX theo cơ chế all-or-nothing.
          </Typography.Text>
        </div>
        <Button
          icon={<ArrowLeftOutlined />}
          onClick={() => navigate("/members")}
        >
          Về danh sách
        </Button>
      </div>

      {result !== null && (
        <Alert
          type="success"
          showIcon
          title={`Đã import ${result.importedCount}/${result.totalRows} hội viên.`}
          description="Toàn bộ dữ liệu đã được lưu thành công."
          action={(
            <Space wrap>
              <Button size="small" onClick={() => setResult(null)}>
                Import tệp khác
              </Button>
              <Button size="small" type="primary" onClick={() => navigate("/members")}>
                Xem danh sách
              </Button>
            </Space>
          )}
        />
      )}

      {errorMessage !== null && (
        <Alert
          type="error"
          showIcon
          title={errorMessage}
          action={selectedFile !== null && rowErrors.length === 0 ? (
            <Button
              size="small"
              loading={submitting}
              onClick={() => void handleImport()}
            >
              Thử lại
            </Button>
          ) : undefined}
        />
      )}

      <Card title="1. Tải và điền template">
        <Space orientation="vertical" size="middle">
          <Typography.Paragraph>
            Không đổi tên sheet hoặc header. Ngày dùng định dạng YYYY-MM-DD,
            số điện thoại nhập dạng Text; Status để trống sẽ là Active.
          </Typography.Paragraph>
          <Button
            icon={<DownloadOutlined />}
            loading={downloading}
            onClick={() => void handleDownload()}
          >
            Tải template XLSX
          </Button>
        </Space>
      </Card>

      <Card title="2. Chọn file và import">
        <Space className="member-import-stack" orientation="vertical" size="middle">
          <Upload.Dragger
            accept=".xlsx"
            maxCount={1}
            multiple={false}
            showUploadList={false}
            disabled={submitting}
            beforeUpload={(file) => {
              if (!file.name.toLowerCase().endsWith(".xlsx")) {
                setErrorMessage("Chỉ chấp nhận file có phần mở rộng .xlsx.");
                return Upload.LIST_IGNORE;
              }
              if (file.size > defaultMaxFileSizeBytes) {
                setErrorMessage("File vượt giới hạn mặc định 10 MiB.");
                return Upload.LIST_IGNORE;
              }

              setSelectedFile(file);
              setErrorMessage(null);
              setRowErrors([]);
              setResult(null);
              return Upload.LIST_IGNORE;
            }}
          >
            <p className="ant-upload-drag-icon"><FileExcelOutlined /></p>
            <p className="ant-upload-text">Kéo thả hoặc bấm để chọn file XLSX</p>
            <p className="ant-upload-hint">
              Một file mỗi lần, tối đa mặc định 10 MiB và 10.000 dòng dữ liệu.
            </p>
          </Upload.Dragger>

          {selectedFile !== null && (
            <Alert
              type="info"
              showIcon
              title={selectedFile.name}
              description={`Kích thước: ${formatFileSize(selectedFile.size)}`}
              action={(
                <Button
                  size="small"
                  icon={<DeleteOutlined />}
                  disabled={submitting}
                  onClick={() => {
                    setSelectedFile(null);
                    setErrorMessage(null);
                    setRowErrors([]);
                  }}
                >
                  Xóa tệp
                </Button>
              )}
            />
          )}

          <Button
            type="primary"
            icon={<UploadOutlined />}
            disabled={selectedFile === null}
            loading={submitting}
            onClick={() => void handleImport()}
          >
            Import hội viên
          </Button>
        </Space>
      </Card>

      {rowErrors.length > 0 && (
        <Card title={`Báo cáo lỗi (${rowErrors.length})`}>
          <Table
            rowKey={(error) =>
              `${error.rowNumber}-${error.field}-${error.message}`
            }
            columns={columns}
            dataSource={rowErrors}
            pagination={{ pageSize: 20, showSizeChanger: false }}
            scroll={{ x: 760 }}
          />
        </Card>
      )}
    </Space>
  );
}

function readImportErrors(error: unknown): MemberImportRowError[] | null {
  if (!(error instanceof ApiError) || error.status !== 422) {
    return null;
  }

  const problem = error.problem as unknown as MemberImportProblemDetails;
  return Array.isArray(problem.errors) ? problem.errors : null;
}

function getImportErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 413) {
      return "File vượt giới hạn dung lượng cho phép.";
    }
    if (error.status === 415) {
      return "File không phải workbook XLSX hợp lệ.";
    }
  }

  return getErrorMessage(
    error,
    "Không thể import hội viên. Vui lòng thử lại.",
  );
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

function formatFileSize(size: number): string {
  if (size < 1024) {
    return `${size} B`;
  }
  if (size < 1024 * 1024) {
    return `${(size / 1024).toFixed(1)} KiB`;
  }
  return `${(size / (1024 * 1024)).toFixed(1)} MiB`;
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : fallback;
}
