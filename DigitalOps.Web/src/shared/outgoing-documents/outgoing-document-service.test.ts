import { writeSession } from "../auth/session-store";
import {
  createOutgoingDocument,
  createOutgoingReview,
  generateOutgoingAiDraft,
  getOutgoingDocument,
  getOutgoingDocuments,
  getOutgoingReviews,
  updateOutgoingDocument,
  uploadOutgoingAttachment,
} from "./outgoing-document-service";

describe("outgoing-document-service", () => {
  beforeEach(() => writeSession({ accessToken: "drafter-token", expiresAt: new Date(Date.now() + 60_000).toISOString() }));

  it("builds list filters and detail requests", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ items: [] })));
    vi.stubGlobal("fetch", fetchMock);
    await getOutgoingDocuments({ q: "quyết định", templateId: "template", relatedMemberId: "member", relatedIncomingDocumentId: "incoming", status: "Editing", draftedByStaffId: "staff", dateFrom: "2026-08-01", dateTo: "2026-08-02", page: 2, pageSize: 50 });
    await getOutgoingDocument("outgoing");
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/outgoing-documents?q=quy%E1%BA%BFt+%C4%91%E1%BB%8Bnh&templateId=template&relatedMemberId=member&relatedIncomingDocumentId=incoming&status=Editing&draftedByStaffId=staff&dateFrom=2026-08-01&dateTo=2026-08-02&page=2&pageSize=50");
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/outgoing-documents/outgoing");
  });

  it("sends create JSON and outgoing multipart", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ id: "outgoing" })));
    vi.stubGlobal("fetch", fetchMock);
    const payload = { templateId: "template", title: "  Tiêu đề  ", relatedMemberId: "member" };
    await createOutgoingDocument(payload);
    const file = new File(["%PDF-1.7"], "sample.pdf", { type: "application/pdf" });
    await uploadOutgoingAttachment("outgoing", file);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/outgoing-documents");
    expect(fetchMock.mock.calls[0][1]?.method).toBe("POST");
    expect(JSON.parse(fetchMock.mock.calls[0][1]?.body as string)).toEqual(payload);
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/outgoing-documents/outgoing/attachments");
    expect(fetchMock.mock.calls[1][1]?.body).toBeInstanceOf(FormData);
    expect((fetchMock.mock.calls[1][1]?.body as FormData).get("file")).toBeInstanceOf(File);
  });

  it("sends partial update and AI draft JSON", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ id: "outgoing" })));
    vi.stubGlobal("fetch", fetchMock);

    await updateOutgoingDocument("outgoing", { title: "Tiêu đề mới", content: "Nội dung mới" });
    await generateOutgoingAiDraft("outgoing", { instruction: "Nhấn mạnh tiến độ" });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/outgoing-documents/outgoing");
    expect(fetchMock.mock.calls[0][1]?.method).toBe("PATCH");
    expect(JSON.parse(fetchMock.mock.calls[0][1]?.body as string)).toEqual({ title: "Tiêu đề mới", content: "Nội dung mới" });
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/outgoing-documents/outgoing/ai-draft");
    expect(fetchMock.mock.calls[1][1]?.method).toBe("POST");
    expect(JSON.parse(fetchMock.mock.calls[1][1]?.body as string)).toEqual({ instruction: "Nhấn mạnh tiến độ" });
  });

  it("sends review requests and history paging", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ items: [] })));
    vi.stubGlobal("fetch", fetchMock);

    await createOutgoingReview("outgoing");
    await getOutgoingReviews("outgoing", { page: 2, pageSize: 50 });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/outgoing-documents/outgoing/reviews");
    expect(fetchMock.mock.calls[0][1]?.method).toBe("POST");
    expect(fetchMock.mock.calls[0][1]?.body).toBeUndefined();
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/outgoing-documents/outgoing/reviews?page=2&pageSize=50");
  });
});

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}
