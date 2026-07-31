import {
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { ApiError } from "../shared/api/api-client";
import * as catalogService from "../shared/document-catalog/document-catalog-service";
import type {
  DocumentTemplateResponse,
  DocumentTypeResponse,
} from "../shared/document-catalog/types";
import {
  DocumentTemplateCreatePage,
  DocumentTemplateDetailPage,
  DocumentTemplateListPage,
  DocumentTypeDetailPage,
  DocumentTypeListPage,
} from "./DocumentCatalogPages";

vi.mock("../shared/document-catalog/document-catalog-service");

describe("DocumentType pages", () => {
  it("loads URL filters and keeps modal values when create conflicts", async () => {
    const user = userEvent.setup();
    vi.mocked(catalogService.getDocumentTypes).mockResolvedValue({
      items: [createType()],
      page: 2,
      pageSize: 50,
      totalCount: 51,
      totalPages: 2,
    });
    vi.mocked(catalogService.createDocumentType).mockRejectedValue(
      new ApiError(409, { status: 409, detail: "Mã loại văn bản đã tồn tại." }),
    );
    renderCatalogRoute(
      "/document-types?activeOnly=true&page=2&pageSize=50",
      "/document-types",
      <DocumentTypeListPage />,
    );

    expect(await screen.findByText("REPORT")).toBeInTheDocument();
    expect(catalogService.getDocumentTypes).toHaveBeenCalledWith({
      activeOnly: true,
      page: 2,
      pageSize: 50,
    });

    await user.click(screen.getByRole("button", { name: /Tạo loại văn bản$/ }));
    const dialog = screen.getByRole("dialog");
    await user.type(within(dialog).getByLabelText("Mã loại văn bản"), "REPORT");
    await user.type(within(dialog).getByLabelText("Tên loại văn bản"), "Báo cáo");
    await user.click(within(dialog).getByRole("button", { name: "Tạo loại văn bản" }));

    expect(await within(dialog).findByText("Mã loại văn bản đã tồn tại.")).toBeInTheDocument();
    expect(within(dialog).getByDisplayValue("REPORT")).toBeInTheDocument();
  });

  it("patches only touched document type fields", async () => {
    const user = userEvent.setup();
    const original = createType();
    vi.mocked(catalogService.getDocumentType).mockResolvedValue(original);
    vi.mocked(catalogService.updateDocumentType).mockResolvedValue({
      ...original,
      description: null,
    });
    renderCatalogRoute(
      `/document-types/${original.id}`,
      "/document-types/:id",
      <DocumentTypeDetailPage />,
    );

    await screen.findByDisplayValue(original.name);
    await user.clear(screen.getByLabelText("Mô tả"));
    await user.click(screen.getByRole("button", { name: /Lưu loại văn bản$/ }));

    await waitFor(() =>
      expect(catalogService.updateDocumentType).toHaveBeenCalledWith(
        original.id,
        { description: null },
      ),
    );
    expect(await screen.findByText("Đã cập nhật loại văn bản.")).toBeInTheDocument();
  });
});

describe("DocumentTemplate pages", () => {
  it("loads document type and active filters from the URL", async () => {
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([createType()]);
    vi.mocked(catalogService.getDocumentTemplates).mockResolvedValue({
      items: [createTemplate()],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });
    renderCatalogRoute(
      "/document-templates?documentTypeId=type-id&activeOnly=true",
      "/document-templates",
      <DocumentTemplateListPage />,
    );

    expect(await screen.findByText("Mẫu báo cáo")).toBeInTheDocument();
    expect(catalogService.getDocumentTemplates).toHaveBeenCalledWith({
      documentTypeId: "type-id",
      activeOnly: true,
      page: 1,
      pageSize: 20,
    });
  });

  it("rejects invalid JSON before creating a template", async () => {
    const user = userEvent.setup();
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([createType()]);
    renderCatalogRoute(
      "/document-templates/new",
      "/document-templates/new",
      <DocumentTemplateCreatePage />,
    );

    await screen.findByRole("heading", { name: "Tạo mẫu văn bản" });
    await chooseDocumentType(user);
    await user.type(screen.getByLabelText("Tên mẫu văn bản"), "Mẫu mới");
    await user.type(screen.getByLabelText("Nội dung mẫu"), "Nội dung");
    const rules = screen.getByLabelText("FormatRules (JSON)");
    await user.clear(rules);
    await user.type(rules, "invalid");
    await user.click(screen.getByRole("button", { name: /Tạo mẫu văn bản$/ }));

    expect(await screen.findByText(/JSON không hợp lệ:/)).toBeInTheDocument();
    expect(catalogService.createDocumentTemplate).not.toHaveBeenCalled();
  });

  it("patches only touched template content", async () => {
    const user = userEvent.setup();
    const original = createTemplate();
    vi.mocked(catalogService.getDocumentTemplate).mockResolvedValue(original);
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([createType()]);
    vi.mocked(catalogService.updateDocumentTemplate).mockResolvedValue({
      ...original,
      templateContent: "Nội dung mới",
    });
    renderCatalogRoute(
      `/document-templates/${original.id}`,
      "/document-templates/:id",
      <DocumentTemplateDetailPage />,
    );

    const content = await screen.findByLabelText("Nội dung mẫu");
    await user.clear(content);
    await user.type(content, "Nội dung mới");
    await user.click(screen.getByRole("button", { name: /Lưu mẫu văn bản$/ }));

    await waitFor(() =>
      expect(catalogService.updateDocumentTemplate).toHaveBeenCalledWith(
        original.id,
        { templateContent: "Nội dung mới" },
      ),
    );
    expect(await screen.findByText("Đã cập nhật mẫu văn bản.")).toBeInTheDocument();
  });

  it("maps server FormatRules errors and preserves the editor", async () => {
    const user = userEvent.setup();
    const original = createTemplate();
    vi.mocked(catalogService.getDocumentTemplate).mockResolvedValue(original);
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([createType()]);
    vi.mocked(catalogService.updateDocumentTemplate).mockRejectedValue(
      new ApiError(422, {
        status: 422,
        errors: { formatRules: ["FormatRules không được chấp nhận."] },
      }),
    );
    renderCatalogRoute(
      `/document-templates/${original.id}`,
      "/document-templates/:id",
      <DocumentTemplateDetailPage />,
    );

    const rules = await screen.findByLabelText("FormatRules (JSON)");
    await user.type(rules, " ");
    await user.click(screen.getByRole("button", { name: /Lưu mẫu văn bản$/ }));

    expect(await screen.findByText("FormatRules không được chấp nhận.")).toBeInTheDocument();
    expect((rules as HTMLTextAreaElement).value).toContain('"version": 1');
  });
});

async function chooseDocumentType(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByLabelText("Loại văn bản"));
  await user.click(await screen.findByText("REPORT — Báo cáo"));
}

function renderCatalogRoute(
  initialEntry: string,
  path: string,
  element: React.ReactNode,
) {
  const router = createMemoryRouter([{ path, element }], {
    initialEntries: [initialEntry],
  });
  return render(<RouterProvider router={router} />);
}

function createType(overrides: Partial<DocumentTypeResponse> = {}): DocumentTypeResponse {
  return {
    id: "type-id",
    code: "REPORT",
    name: "Báo cáo",
    description: "Mô tả cũ",
    isActive: true,
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}

function createTemplate(
  overrides: Partial<DocumentTemplateResponse> = {},
): DocumentTemplateResponse {
  return {
    id: "template-id",
    documentType: { id: "type-id", code: "REPORT", name: "Báo cáo" },
    name: "Mẫu báo cáo",
    templateContent: "Nội dung cũ",
    formatRules: {
      version: 1,
      rules: [{ code: "header", required: true }],
    },
    isActive: true,
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}
