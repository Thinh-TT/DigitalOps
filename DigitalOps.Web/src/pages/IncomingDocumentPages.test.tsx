import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { ApiError } from "../shared/api/api-client";
import {
  AuthContext,
  type AuthContextValue,
} from "../shared/auth/auth-context";
import type { Role } from "../shared/auth/types";
import * as catalogService from "../shared/document-catalog/document-catalog-service";
import type { DocumentTypeResponse } from "../shared/document-catalog/types";
import * as incomingService from "../shared/incoming-documents/incoming-document-service";
import type { IncomingDocumentResponse } from "../shared/incoming-documents/types";
import * as staffService from "../shared/staff/staff-service";
import type { StaffResponse } from "../shared/staff/types";
import {
  IncomingDocumentCreatePage,
  IncomingDocumentDetailPage,
  IncomingDocumentListPage,
} from "./IncomingDocumentPages";

vi.mock("../shared/document-catalog/document-catalog-service");
vi.mock("../shared/incoming-documents/incoming-document-service");
vi.mock("../shared/staff/staff-service");

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([createType()]);
  vi.mocked(incomingService.getIncomingDocuments).mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0,
  });
  vi.mocked(incomingService.deleteAttachment).mockResolvedValue(undefined);
  vi.mocked(staffService.getAllActiveStaff).mockResolvedValue([createStaff()]);
});

describe("Incoming document pages", () => {
  it("loads filters and paging from the list URL and shows create only for Clerk", async () => {
    vi.mocked(incomingService.getIncomingDocuments).mockResolvedValue({
      items: [createIncoming()],
      page: 2,
      pageSize: 50,
      totalCount: 51,
      totalPages: 2,
    });
    renderIncomingRoute(
      "/incoming-documents?q=b%C3%A1o+c%C3%A1o&documentTypeId=type-id&status=New&deadlineFrom=2026-08-01&deadlineTo=2026-08-31&page=2&pageSize=50",
      "/incoming-documents",
      <IncomingDocumentListPage />,
      ["Clerk"],
    );

    expect(await screen.findByText("01/BC-MTTQ")).toBeInTheDocument();
    expect(incomingService.getIncomingDocuments).toHaveBeenCalledWith({
      q: "báo cáo",
      documentTypeId: "type-id",
      status: "New",
      deadlineFrom: "2026-08-01",
      deadlineTo: "2026-08-31",
      page: 2,
      pageSize: 50,
    });
    expect(screen.getByRole("button", { name: /Tiếp nhận văn bản$/ })).toBeInTheDocument();
  });

  it("validates dates before create and loads only active types", async () => {
    const user = userEvent.setup();
    renderIncomingRoute(
      "/incoming-documents/new",
      "/incoming-documents/new",
      <IncomingDocumentCreatePage />,
      ["Clerk"],
    );

    await screen.findByRole("heading", { name: "Tiếp nhận văn bản đến" });
    expect(catalogService.getAllDocumentTypes).toHaveBeenCalledWith(true);
    await user.type(screen.getByLabelText("Số, ký hiệu"), "01/BC");
    await user.type(screen.getByLabelText("Cơ quan gửi"), "UBND phường");
    await user.type(screen.getByLabelText("Trích yếu"), "Báo cáo tháng");
    await selectType(user);
    await user.type(screen.getByLabelText("Ngày tiếp nhận"), "2026-08-10");
    await user.type(screen.getByLabelText("Hạn xử lý"), "2026-08-01");
    await user.click(screen.getByRole("button", { name: /Tiếp nhận văn bản$/ }));

    expect(await screen.findByText("Hạn xử lý không được trước ngày tiếp nhận.")).toBeInTheDocument();
    expect(incomingService.createIncomingDocument).not.toHaveBeenCalled();
  });

  it("patches only touched fields and keeps values on conflict", async () => {
    const user = userEvent.setup();
    const original = createIncoming();
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.updateIncomingDocument).mockRejectedValue(
      new ApiError(409, { status: 409, detail: "Văn bản đến đã hoàn tất." }),
    );
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    const summary = await screen.findByLabelText("Trích yếu");
    await user.clear(summary);
    await user.type(summary, "Nội dung mới");
    await user.click(screen.getByRole("button", { name: /Lưu thay đổi$/ }));

    await waitFor(() => expect(incomingService.updateIncomingDocument).toHaveBeenCalledWith(
      original.id,
      { summary: "Nội dung mới" },
    ));
    expect(await screen.findByText("Văn bản đến đã hoàn tất.")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Nội dung mới")).toBeInTheDocument();
  });

  it("shows inactive current type but keeps other inactive types unavailable", async () => {
    const original = createIncoming();
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([
      createType({ isActive: false }),
      createType({ id: "other-id", code: "OLD", name: "Loại cũ", isActive: false }),
      createType({ id: "active-id", code: "PLAN", name: "Kế hoạch", isActive: true }),
    ]);
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    expect(await screen.findByText("Loại văn bản hiện tại đã ngừng hoạt động.")).toBeInTheDocument();
    expect(screen.getByText("REPORT — Báo cáo (Ngừng hoạt động)")).toBeInTheDocument();
  });

  it("renders read-only detail for BusinessAccess without Clerk role", async () => {
    const original = createIncoming();
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Leader"],
    );

    expect(await screen.findByDisplayValue(original.summary)).toBeDisabled();
    expect(screen.queryByRole("button", { name: /Lưu thay đổi$/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Chưa có gợi ý hoặc phân công/)).toBeInTheDocument();
    expect(screen.getByText(/Chưa có file đính kèm/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Thêm file$/ })).not.toBeInTheDocument();
    expect(staffService.getAllActiveStaff).not.toHaveBeenCalled();
  });

  it("lets Clerk request an AI suggestion and explicitly confirm assignment", async () => {
    const user = userEvent.setup();
    const original = createIncoming();
    const suggestedStaff = {
      id: "suggested-staff",
      fullName: "Trần Thị Tuyên",
      position: "Cán bộ tuyên truyền",
      department: "Ban Tuyên giáo",
    };
    vi.mocked(staffService.getAllActiveStaff).mockResolvedValue([
      createStaff({
        id: suggestedStaff.id,
        fullName: suggestedStaff.fullName,
        position: suggestedStaff.position,
        department: suggestedStaff.department,
      }),
    ]);
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.suggestIncomingDocumentAssignment).mockResolvedValue({
      incomingDocumentId: original.id,
      suggestedStaff,
      reason: "Phù hợp lĩnh vực tuyên truyền.",
      confidence: null,
      suggestedAt: "2026-08-02T08:00:00Z",
    });
    vi.mocked(incomingService.confirmIncomingDocumentAssignment).mockResolvedValue({
      ...original,
      suggestedStaff,
      assignmentSuggestionReason: "Phù hợp lĩnh vực tuyên truyền.",
      assignmentSuggestedAt: "2026-08-02T08:00:00Z",
      assignedToStaff: suggestedStaff,
      assignmentConfirmedBy: {
        id: "current-staff",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
      assignmentConfirmedAt: "2026-08-02T08:01:00Z",
      status: "InProgress",
    });
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    await user.click(await screen.findByRole("button", { name: /AI gợi ý$/ }));
    await waitFor(() => expect(
      incomingService.suggestIncomingDocumentAssignment,
    ).toHaveBeenCalledWith(original.id));
    expect(await screen.findByText("Phù hợp lĩnh vực tuyên truyền.")).toBeInTheDocument();
    expect(screen.getAllByText(/Trần Thị Tuyên/).length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: /^Xác nhận$/ }));
    await waitFor(() => expect(
      incomingService.confirmIncomingDocumentAssignment,
    ).toHaveBeenCalledWith(original.id, {
      assignedToStaffId: suggestedStaff.id,
    }));
    expect(await screen.findByText("Đã xác nhận điều phối.")).toBeInTheDocument();
    expect(screen.getByText("Đang xử lý")).toBeInTheDocument();
  });

  it("keeps manual assignment available when AI returns 503", async () => {
    const user = userEvent.setup();
    const original = createIncoming();
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.suggestIncomingDocumentAssignment).mockRejectedValue(
      new ApiError(503, {
        status: 503,
        detail: "Dịch vụ AI hiện không khả dụng. Bạn vẫn có thể chọn thủ công.",
      }),
    );
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    await user.click(await screen.findByRole("button", { name: /AI gợi ý$/ }));

    expect(await screen.findByText(/Dịch vụ AI hiện không khả dụng/)).toBeInTheDocument();
    expect(screen.getByLabelText("Cán bộ xử lý")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Xác nhận$/ })).toBeDisabled();
  });

  it("shows insufficient evidence without blocking manual assignment", async () => {
    const user = userEvent.setup();
    const original = createIncoming();
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.suggestIncomingDocumentAssignment).mockResolvedValue({
      incomingDocumentId: original.id,
      suggestedStaff: null,
      reason: "Không có cán bộ active đạt ngưỡng bằng chứng.",
      confidence: null,
      suggestedAt: null,
    });
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    await user.click(await screen.findByRole("button", { name: /AI gợi ý$/ }));

    expect(await screen.findByText("Chưa đủ bằng chứng để gợi ý")).toBeInTheDocument();
    expect(screen.getByText("Không có cán bộ active đạt ngưỡng bằng chứng.")).toBeInTheDocument();
    expect(screen.getByLabelText("Cán bộ xử lý")).toBeInTheDocument();
  });

  it("lets Clerk upload and download attachments while updating metadata", async () => {
    const original = createIncoming({ attachments: [createAttachment()] });
    const uploaded = createAttachment({
      id: "new-attachment",
      fileName: "ảnh.png",
      extractionStatus: "Unsupported",
      uploadedAt: "2026-07-31T10:00:00Z",
    });
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.uploadIncomingAttachment).mockResolvedValue(uploaded);
    vi.mocked(incomingService.downloadAttachment).mockResolvedValue({
      blob: new Blob(["pdf"], { type: "application/pdf" }),
      fileName: "report.pdf",
    });
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: vi.fn(() => "blob:attachment"),
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    expect(await screen.findByText("report.pdf")).toBeInTheDocument();
    expect(screen.getByText("Chờ trích xuất")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Tải$/ }));
    await waitFor(() => expect(incomingService.downloadAttachment)
      .toHaveBeenCalledWith("attachment-id"));

    const input = document.querySelector<HTMLInputElement>('input[type="file"]');
    expect(input).not.toBeNull();
    const png = new File(["png"], "ảnh.png", { type: "image/png" });
    fireEvent.change(input!, { target: { files: [png] } });
    await waitFor(() => expect(incomingService.uploadIncomingAttachment)
      .toHaveBeenCalledWith(original.id, png));
    expect(await screen.findByText("ảnh.png")).toBeInTheDocument();
    expect(screen.getByText("Không hỗ trợ")).toBeInTheDocument();

  });

  it("confirms attachment deletion and removes its metadata", async () => {
    const original = createIncoming({ attachments: [createAttachment()] });
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    await screen.findByText("report.pdf");
    fireEvent.click(screen.getByRole("button", { name: /Xóa$/ }));
    const buttons = await screen.findAllByRole("button", { name: /Xóa$/ });
    fireEvent.click(buttons[buttons.length - 1]);

    await waitFor(() => expect(incomingService.deleteAttachment)
      .toHaveBeenCalledWith("attachment-id"));
    await waitFor(() => expect(screen.queryByText("report.pdf")).not.toBeInTheDocument());
  });

  it("shows a clear unsupported-file error and keeps existing attachments", async () => {
    const user = userEvent.setup();
    const original = createIncoming({ attachments: [createAttachment()] });
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(original);
    vi.mocked(incomingService.uploadIncomingAttachment).mockRejectedValue(
      new ApiError(415, { status: 415, detail: "Sai định dạng." }),
    );
    renderIncomingRoute(
      `/incoming-documents/${original.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    await screen.findByText("report.pdf");
    const input = document.querySelector<HTMLInputElement>('input[type="file"]');
    await user.upload(
      input!,
      new File(["fake"], "fake.pdf", { type: "application/pdf" }),
    );

    expect(await screen.findByText(/File không đúng định dạng PDF/)).toBeInTheDocument();
    expect(screen.getByText("report.pdf")).toBeInTheDocument();
  });

  it("allows assigned staff to complete and refreshes the response", async () => {
    const user = userEvent.setup();
    const assigned = createIncoming({
      status: "InProgress",
      assignedToStaff: {
        id: "current-staff",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
    });
    vi.mocked(incomingService.getIncomingDocument).mockResolvedValue(assigned);
    vi.mocked(incomingService.completeIncomingDocument).mockResolvedValue({
      ...assigned,
      status: "Completed",
      completedAt: "2026-07-31T09:00:00Z",
    });
    renderIncomingRoute(
      `/incoming-documents/${assigned.id}`,
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Leader"],
    );

    await user.click(await screen.findByRole("button", { name: /Hoàn tất xử lý$/ }));
    await user.click(await screen.findByRole("button", { name: /^Hoàn tất$/ }));

    await waitFor(() => expect(incomingService.completeIncomingDocument).toHaveBeenCalledWith(assigned.id));
    expect(await screen.findByText("Đã hoàn tất văn bản đến.")).toBeInTheDocument();
    expect(screen.getByText("Hoàn tất")).toBeInTheDocument();
  });

  it("renders a not-found state for 404", async () => {
    vi.mocked(incomingService.getIncomingDocument).mockRejectedValue(
      new ApiError(404, { status: 404, detail: "Không tìm thấy." }),
    );
    renderIncomingRoute(
      "/incoming-documents/missing",
      "/incoming-documents/:id",
      <IncomingDocumentDetailPage />,
      ["Clerk"],
    );

    expect(await screen.findByText("Không tìm thấy văn bản đến")).toBeInTheDocument();
  });
});

async function selectType(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByLabelText("Loại văn bản"));
  await user.click(await screen.findByText("REPORT — Báo cáo"));
}

function renderIncomingRoute(
  initialEntry: string,
  path: string,
  element: React.ReactNode,
  roles: Role[],
) {
  const router = createMemoryRouter([{ path, element }], { initialEntries: [initialEntry] });
  return render(
    <AuthContext.Provider value={createAuthValue(roles)}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

function createAuthValue(roles: Role[]): AuthContextValue {
  return {
    status: "authenticated",
    currentUser: {
      staff: {
        id: "current-staff",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
      roles,
      mustChangePassword: false,
    },
    errorMessage: null,
    establishSession: vi.fn(),
    refreshCurrentUser: vi.fn(),
    logout: vi.fn(),
  };
}

function createType(overrides: Partial<DocumentTypeResponse> = {}): DocumentTypeResponse {
  return {
    id: "type-id",
    code: "REPORT",
    name: "Báo cáo",
    description: null,
    isActive: true,
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}

function createIncoming(
  overrides: Partial<IncomingDocumentResponse> = {},
): IncomingDocumentResponse {
  return {
    id: "incoming-id",
    referenceNumber: "01/BC-MTTQ",
    senderOrg: "UBND phường",
    summary: "Báo cáo tháng",
    receivedDate: "2026-07-30",
    deadline: "2026-08-05",
    documentType: { id: "type-id", code: "REPORT", name: "Báo cáo" },
    suggestedStaff: null,
    assignmentSuggestionReason: null,
    assignmentConfidence: null,
    assignmentSuggestedAt: null,
    assignedToStaff: null,
    assignmentConfirmedBy: null,
    assignmentConfirmedAt: null,
    status: "New",
    completedAt: null,
    attachments: [],
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}

function createAttachment(
  overrides: Partial<IncomingDocumentResponse["attachments"][number]> = {},
): IncomingDocumentResponse["attachments"][number] {
  return {
    id: "attachment-id",
    fileName: "report.pdf",
    uploadedBy: {
      id: "current-staff",
      fullName: "Nguyễn Văn A",
      position: "Chuyên viên",
      department: "Văn phòng",
    },
    uploadedAt: "2026-07-31T09:00:00Z",
    extractionStatus: "Pending",
    extractedAt: null,
    ...overrides,
  };
}

function createStaff(overrides: Partial<StaffResponse> = {}): StaffResponse {
  return {
    id: "active-staff",
    identityUserId: "active-user",
    userName: "active.staff",
    fullName: "Cán bộ active",
    position: "Chuyên viên",
    department: "Văn phòng",
    email: "active@example.local",
    phone: null,
    isActive: true,
    roles: ["Drafter"],
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    ...overrides,
  };
}
