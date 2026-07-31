import { writeSession } from "../auth/session-store";
import {
  completeIncomingDocument,
  createIncomingDocument,
  getIncomingDocuments,
  updateIncomingDocument,
} from "./incoming-document-service";

describe("incoming-document-service", () => {
  it("builds all list filters and sends authenticated requests", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await getIncomingDocuments({
      q: "báo cáo",
      documentTypeId: "type-id",
      status: "InProgress",
      assignedToStaffId: "staff-id",
      deadlineFrom: "2026-08-01",
      deadlineTo: "2026-08-31",
      page: 2,
      pageSize: 50,
    });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/incoming-documents?q=b%C3%A1o+c%C3%A1o&documentTypeId=type-id&status=InProgress&assignedToStaffId=staff-id&deadlineFrom=2026-08-01&deadlineTo=2026-08-31&page=2&pageSize=50",
    );
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("Authorization")).toBe("Bearer clerk-token");
  });

  it("sends create, partial patch and complete payloads", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(jsonResponse({ id: "incoming-id" })));
    vi.stubGlobal("fetch", fetchMock);

    const createRequest = {
      referenceNumber: "01/BC",
      senderOrg: "UBND phường",
      summary: "Báo cáo tháng",
      receivedDate: "2026-07-30",
      deadline: "2026-08-05",
      documentTypeId: "type-id",
    };
    await createIncomingDocument(createRequest);
    await updateIncomingDocument("incoming-id", { summary: "Nội dung mới" });
    await completeIncomingDocument("incoming-id");

    expectRequest(fetchMock, 0, "/api/v1/incoming-documents", "POST", createRequest);
    expectRequest(fetchMock, 1, "/api/v1/incoming-documents/incoming-id", "PATCH", {
      summary: "Nội dung mới",
    });
    expect(fetchMock.mock.calls[2][0]).toBe(
      "/api/v1/incoming-documents/incoming-id/complete",
    );
    expect(fetchMock.mock.calls[2][1]?.method).toBe("POST");
  });
});

function expectRequest(
  fetchMock: ReturnType<typeof vi.fn>,
  index: number,
  path: string,
  method: string,
  body: unknown,
) {
  expect(fetchMock.mock.calls[index][0]).toBe(path);
  const options = fetchMock.mock.calls[index][1] as RequestInit;
  expect(options.method).toBe(method);
  expect(JSON.parse(options.body as string)).toEqual(body);
}

function writeValidSession() {
  writeSession({
    accessToken: "clerk-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  });
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
