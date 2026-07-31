import { writeSession } from "../auth/session-store";
import {
  completeIncomingDocument,
  createIncomingDocument,
  deleteAttachment,
  downloadAttachment,
  getIncomingDocuments,
  uploadIncomingAttachment,
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

  it("uploads multipart, downloads a blob and deletes an attachment", async () => {
    writeValidSession();
    const pdfBlob = new Blob(["%PDF-1.7"], { type: "application/pdf" });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ id: "attachment-id" }))
      .mockResolvedValueOnce(new Response(pdfBlob, {
        status: 200,
        headers: {
          "Content-Type": "application/pdf",
          "Content-Disposition": "attachment; filename*=UTF-8''report.pdf",
        },
      }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    const file = new File(["%PDF-1.7"], "report.pdf", {
      type: "application/pdf",
    });

    await uploadIncomingAttachment("incoming-id", file);
    const downloaded = await downloadAttachment("attachment-id");
    await deleteAttachment("attachment-id");

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/incoming-documents/incoming-id/attachments",
    );
    const uploadOptions = fetchMock.mock.calls[0][1] as RequestInit;
    expect(uploadOptions.method).toBe("POST");
    expect(uploadOptions.body).toBeInstanceOf(FormData);
    expect(new Headers(uploadOptions.headers).has("Content-Type")).toBe(false);
    const uploadedFile = (uploadOptions.body as FormData).get("file") as File;
    expect(uploadedFile.name).toBe(file.name);
    expect(uploadedFile.size).toBe(file.size);

    expect(fetchMock.mock.calls[1][0]).toBe(
      "/api/v1/attachments/attachment-id/download",
    );
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get("Accept")).toBe("*/*");
    expect(downloaded.fileName).toBe("report.pdf");
    expect(downloaded.blob.type).toBe("application/pdf");

    expect(fetchMock.mock.calls[2][0]).toBe(
      "/api/v1/attachments/attachment-id",
    );
    expect(fetchMock.mock.calls[2][1]?.method).toBe("DELETE");
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
