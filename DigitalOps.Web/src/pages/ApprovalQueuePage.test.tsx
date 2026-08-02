import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { ApprovalQueuePage } from "./ApprovalQueuePage";
import * as service from "../shared/outgoing-documents/outgoing-document-service";
import type { OutgoingDocumentResponse, ReviewResponse } from "../shared/outgoing-documents/types";
import { ApiError } from "../shared/api/api-client";

vi.mock("../shared/outgoing-documents/outgoing-document-service");

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(service.getOutgoingDocuments).mockResolvedValue({
    items: [outgoingFixture()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
  });
  vi.mocked(service.getOutgoingReviews).mockResolvedValue({
    items: [reviewFixture()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
  });
  vi.mocked(service.getOutgoingDocument).mockResolvedValue({
    ...outgoingFixture(), status: "Editing",
  });
  vi.mocked(service.decideOutgoingDocumentApproval).mockResolvedValue({
    ...outgoingFixture(), status: "Approved", approvedByStaff: { id: "leader", fullName: "Lãnh đạo", position: null, department: null }, approvedAt: "2026-08-02T10:30:00Z",
  });
});

it("loads only PendingApproval documents and shows review evidence in the drawer", async () => {
  renderPage();

  expect(await screen.findByRole("heading", { name: "Hàng chờ duyệt" })).toBeInTheDocument();
  expect(service.getOutgoingDocuments).toHaveBeenCalledWith({ status: "PendingApproval", page: 1, pageSize: 20 });
  await waitUntilQueueReady();
  await userEvent.click(screen.getByRole("button", { name: /Xem xét/ }));

  expect(await screen.findByText("Nội dung cần lãnh đạo duyệt", { selector: "pre" })).toBeInTheDocument();
  expect(screen.getByText("Bản AI đầu tiên", { selector: "h5" })).toBeInTheDocument();
  expect(await screen.findByText("Snapshot ở lần review", { selector: "pre" })).toBeInTheDocument();
  expect(service.getOutgoingReviews).toHaveBeenCalledWith("outgoing", { page: 1, pageSize: 20 });
  expect(screen.queryByLabelText(/comment/i)).not.toBeInTheDocument();
});

it("returns the document after explicit confirmation and refreshes the queue", async () => {
  renderPage();
  await waitUntilQueueReady();
  await userEvent.click(screen.getByRole("button", { name: /Xem xét/ }));
  await userEvent.click(await screen.findByRole("button", { name: /Trả lại chỉnh sửa/ }));
  await userEvent.click(await screen.findByRole("button", { name: "Xác nhận trả lại" }));

  await waitFor(() => expect(service.decideOutgoingDocumentApproval).toHaveBeenCalledWith("outgoing", { decision: "Return" }));
  expect(await screen.findByText("Đã trả văn bản về trạng thái chỉnh sửa.")).toBeInTheDocument();
  await waitFor(() => expect(service.getOutgoingDocuments).toHaveBeenCalledTimes(2));
  expect(screen.queryByRole("button", { name: /Duyệt văn bản/ })).not.toBeInTheDocument();
});

it("keeps an error visible and refreshes queue and resource after a conflict", async () => {
  const conflict = Object.assign(new Error("conflict"), {
    status: 409,
    problem: { detail: "Văn bản đã được xử lý bởi lãnh đạo khác." },
  });
  Object.setPrototypeOf(conflict, ApiError.prototype);
  vi.mocked(service.decideOutgoingDocumentApproval).mockRejectedValue(conflict);
  vi.mocked(service.getOutgoingDocuments)
    .mockResolvedValueOnce({ items: [outgoingFixture()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
  renderPage();
  await waitUntilQueueReady();
  await userEvent.click(screen.getByRole("button", { name: /Xem xét/ }));
  await userEvent.click(await screen.findByRole("button", { name: /Duyệt văn bản/ }));
  await userEvent.click(await screen.findByRole("button", { name: "Xác nhận duyệt" }));

  expect(await screen.findByText("Văn bản đã được xử lý bởi lãnh đạo khác.")).toBeInTheDocument();
  await waitFor(() => expect(service.getOutgoingDocument).toHaveBeenCalledWith("outgoing"));
  await waitFor(() => expect(service.getOutgoingDocuments).toHaveBeenCalledWith({ status: "PendingApproval", page: 1, pageSize: 20 }));
  expect(screen.queryByRole("button", { name: /Duyệt văn bản/ })).not.toBeInTheDocument();
});

function renderPage() {
  return render(<MemoryRouter><ApprovalQueuePage /></MemoryRouter>);
}

async function waitUntilQueueReady() {
  await screen.findByRole("button", { name: /Xem xét/ });
  await waitFor(() => expect(screen.queryByRole("img", { name: "loading" })).not.toBeInTheDocument());
}

function outgoingFixture(): OutgoingDocumentResponse {
  return {
    id: "outgoing",
    template: { id: "template", name: "Mẫu quyết định", documentType: { id: "type", code: "QD", name: "Quyết định" } },
    relatedIncomingDocument: { id: "incoming", referenceNumber: "12/CV", summary: "Văn bản liên quan" },
    relatedMember: { id: "member", fullName: "Hội viên A", position: "Ủy viên" },
    title: "Quyết định chờ duyệt",
    content: "Nội dung cần lãnh đạo duyệt",
    aiDraftContent: "Bản AI đầu tiên",
    draftedByStaff: { id: "drafter", fullName: "Người soạn", position: null, department: null },
    status: "PendingApproval",
    reviewIssues: [{ ruleCode: "style", severity: "Warning", message: "Kiểm tra căn lề.", location: "Trang 1" }],
    approvedByStaff: null,
    approvedAt: null,
    referenceNumber: null,
    issuedDate: null,
    archivedAt: null,
    attachments: [],
    createdAt: "2026-08-02T08:00:00Z",
    updatedAt: "2026-08-02T09:00:00Z",
  };
}

function reviewFixture(): ReviewResponse {
  return {
    id: "review",
    outgoingDocumentId: "outgoing",
    attemptNo: 1,
    reviewSource: "Hybrid",
    reviewedByStaff: { id: "drafter", fullName: "Người soạn", position: null, department: null },
    contentSnapshot: "Snapshot ở lần review",
    reviewResult: "Passed",
    reviewIssues: [{ ruleCode: "style", severity: "Warning", message: "Kiểm tra căn lề.", location: "Trang 1" }],
    reviewedAt: "2026-08-02T09:00:00Z",
    documentStatus: "PendingApproval",
  };
}
