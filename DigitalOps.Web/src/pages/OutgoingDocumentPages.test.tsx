import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { AuthContext, type AuthContextValue } from "../shared/auth/auth-context";
import * as catalogService from "../shared/document-catalog/document-catalog-service";
import * as incomingService from "../shared/incoming-documents/incoming-document-service";
import * as memberService from "../shared/members/member-service";
import * as service from "../shared/outgoing-documents/outgoing-document-service";
import type { OutgoingDocumentResponse } from "../shared/outgoing-documents/types";
import { OutgoingDocumentCreatePage, OutgoingDocumentDetailPage, OutgoingDocumentListPage } from "./OutgoingDocumentPages";

vi.mock("../shared/document-catalog/document-catalog-service");
vi.mock("../shared/incoming-documents/incoming-document-service");
vi.mock("../shared/members/member-service");
vi.mock("../shared/outgoing-documents/outgoing-document-service");

beforeEach(() => {
  vi.mocked(catalogService.getAllDocumentTemplates).mockResolvedValue([]);
  vi.mocked(incomingService.getIncomingDocuments).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  vi.mocked(memberService.getMemberLookup).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  vi.mocked(service.getOutgoingDocuments).mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
  vi.mocked(service.updateOutgoingDocument).mockImplementation(async (_, request) => ({
    ...outgoingFixture(),
    title: request.title ?? outgoingFixture().title,
    content: request.content ?? outgoingFixture().content,
  }));
  vi.mocked(service.generateOutgoingAiDraft).mockResolvedValue({
    ...outgoingFixture(),
    content: "Bản AI đã sinh",
    aiDraftContent: "Bản AI đã sinh",
    status: "AiDraft",
  });
});

it("keeps list filters in the URL and shows create only to Drafter", async () => {
  renderPage(<OutgoingDocumentListPage />, ["/outgoing-documents"], auth("Drafter"));
  const keyword = await screen.findByLabelText("Từ khóa văn bản đi");
  await userEvent.type(keyword, "quyết định");
  await userEvent.click(screen.getByRole("button", { name: /Lọc/ }));
  await waitFor(() => expect(service.getOutgoingDocuments).toHaveBeenLastCalledWith(expect.objectContaining({ q: "quyết định" })));
  expect(screen.getByRole("button", { name: /Tạo văn bản đi/ })).toBeInTheDocument();
});

it("renders editable content and attachment actions for the owner", async () => {
  vi.mocked(service.getOutgoingDocument).mockResolvedValue(outgoingFixture());
  renderPage(<Routes><Route path="/outgoing-documents/:id" element={<OutgoingDocumentDetailPage />} /></Routes>, ["/outgoing-documents/outgoing"], auth("Drafter"));
  expect(await screen.findByLabelText("Nội dung hiện tại", { selector: "textarea" })).toHaveValue("Kính gửi {{member.email}}");
  expect(screen.getByRole("button", { name: "Lưu" })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Sinh nháp AI" })).toBeInTheDocument();
  expect(screen.getByText("Thêm file")).toBeInTheDocument();
});

it("requires saving dirty editor content before opening AI draft", async () => {
  vi.mocked(service.getOutgoingDocument).mockResolvedValue(outgoingFixture());
  renderPage(<Routes><Route path="/outgoing-documents/:id" element={<OutgoingDocumentDetailPage />} /></Routes>, ["/outgoing-documents/outgoing"], auth("Drafter"));
  const content = await screen.findByLabelText("Nội dung hiện tại", { selector: "textarea" });
  await userEvent.clear(content);
  await userEvent.type(content, "Nội dung chưa lưu");
  await userEvent.click(screen.getByRole("button", { name: "Sinh nháp AI" }));

  expect(await screen.findByText("Vui lòng lưu thay đổi tiêu đề và nội dung trước khi sinh nháp AI.")).toBeInTheDocument();
  expect(service.generateOutgoingAiDraft).not.toHaveBeenCalled();
  expect(content).toHaveValue("Nội dung chưa lưu");
});

it("submits optional instruction and shows the immutable first AI draft", async () => {
  vi.mocked(service.getOutgoingDocument).mockResolvedValue(outgoingFixture());
  renderPage(<Routes><Route path="/outgoing-documents/:id" element={<OutgoingDocumentDetailPage />} /></Routes>, ["/outgoing-documents/outgoing"], auth("Drafter"));
  await screen.findByLabelText("Nội dung hiện tại", { selector: "textarea" });
  await userEvent.click(screen.getByRole("button", { name: "Sinh nháp AI" }));
  await userEvent.type(screen.getByLabelText("Hướng dẫn bổ sung cho AI"), "Nhấn mạnh tiến độ");
  await userEvent.click(screen.getByRole("button", { name: "Sinh và lưu nháp" }));

  await waitFor(() => expect(service.generateOutgoingAiDraft).toHaveBeenCalledWith("outgoing", { instruction: "Nhấn mạnh tiến độ" }));
  expect(screen.getByLabelText("Nội dung hiện tại", { selector: "textarea" })).toHaveValue("Bản AI đã sinh");
  await userEvent.click(screen.getByRole("tab", { name: "Bản AI đầu tiên" }));
  expect(await screen.findByText("Bản AI đã sinh", { selector: "pre" })).toBeInTheDocument();
});

it("keeps editor and instruction when AI returns 503", async () => {
  const error = Object.assign(new Error("unavailable"), { status: 503, problem: { detail: "Dịch vụ AI hiện không khả dụng." } });
  Object.setPrototypeOf(error, (await import("../shared/api/api-client")).ApiError.prototype);
  vi.mocked(service.getOutgoingDocument).mockResolvedValue(outgoingFixture());
  vi.mocked(service.generateOutgoingAiDraft).mockRejectedValue(error);
  renderPage(<Routes><Route path="/outgoing-documents/:id" element={<OutgoingDocumentDetailPage />} /></Routes>, ["/outgoing-documents/outgoing"], auth("Drafter"));
  await screen.findByLabelText("Nội dung hiện tại", { selector: "textarea" });
  await userEvent.click(screen.getByRole("button", { name: "Sinh nháp AI" }));
  const instruction = screen.getByLabelText("Hướng dẫn bổ sung cho AI");
  await userEvent.type(instruction, "Giữ dữ liệu này");
  await userEvent.click(screen.getByRole("button", { name: "Sinh và lưu nháp" }));

  expect(await screen.findByText("Dịch vụ AI hiện không khả dụng.")).toBeInTheDocument();
  expect(screen.getByLabelText("Nội dung hiện tại", { selector: "textarea" })).toHaveValue("Kính gửi {{member.email}}");
  expect(instruction).toHaveValue("Giữ dữ liệu này");
});

it("keeps the editor read-only for non-Drafter viewers", async () => {
  vi.mocked(service.getOutgoingDocument).mockResolvedValue(outgoingFixture());
  renderPage(<Routes><Route path="/outgoing-documents/:id" element={<OutgoingDocumentDetailPage />} /></Routes>, ["/outgoing-documents/outgoing"], auth("Leader"));
  expect(await screen.findByLabelText("Nội dung hiện tại", { selector: "textarea" })).toHaveAttribute("readonly");
  expect(screen.queryByRole("button", { name: "Lưu" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Sinh nháp AI" })).not.toBeInTheDocument();
  expect(screen.queryByText("Thêm file")).not.toBeInTheDocument();
});

it("maps server validation errors to create fields and keeps the form", async () => {
  const error = Object.assign(new Error("validation"), { status: 400, problem: { errors: { title: ["Tiêu đề chưa hợp lệ."] } } });
  Object.setPrototypeOf(error, (await import("../shared/api/api-client")).ApiError.prototype);
  vi.mocked(service.createOutgoingDocument).mockRejectedValue(error);
  renderPage(<OutgoingDocumentCreatePage />, ["/outgoing-documents/new"], auth("Drafter"));
  await userEvent.type(await screen.findByLabelText("Tiêu đề"), "Bản nháp");
  await userEvent.click(screen.getByRole("button", { name: "Tạo văn bản" }));
  expect(await screen.findByText("Vui lòng chọn mẫu văn bản.")).toBeInTheDocument();
});

function renderPage(element: React.ReactNode, entries: string[], value: AuthContextValue) {
  return render(<AuthContext.Provider value={value}><MemoryRouter initialEntries={entries}>{element}</MemoryRouter></AuthContext.Provider>);
}

function auth(role: "Drafter" | "Leader"): AuthContextValue {
  return { status: "authenticated", currentUser: { staff: { id: "staff", fullName: "Người soạn", position: null, department: null }, roles: [role], mustChangePassword: false }, errorMessage: null, establishSession: vi.fn(), refreshCurrentUser: vi.fn(), logout: vi.fn() };
}

function outgoingFixture(): OutgoingDocumentResponse {
  return { id: "outgoing", template: { id: "template", name: "Quyết định", documentType: { id: "type", code: "QD", name: "Quyết định" } }, relatedIncomingDocument: null, relatedMember: null, title: "Quyết định mẫu", content: "Kính gửi {{member.email}}", aiDraftContent: null, draftedByStaff: { id: "staff", fullName: "Người soạn", position: null, department: null }, status: "Editing", reviewIssues: [], approvedByStaff: null, approvedAt: null, referenceNumber: null, issuedDate: null, archivedAt: null, attachments: [], createdAt: "2026-08-01T00:00:00Z", updatedAt: "2026-08-01T00:00:00Z" };
}
