import { writeSession } from "../auth/session-store";
import {
  createMember,
  deactivateMember,
  downloadMemberImportTemplate,
  getMemberLookup,
  getMembers,
  importMembers,
  updateMember,
} from "./member-service";

describe("member-service", () => {
  it("builds list and lookup queries with the bearer token", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(jsonResponse({
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0,
      })));
    vi.stubGlobal("fetch", fetchMock);

    await getMembers({
      q: "  Nguyễn An ",
      status: "Inactive",
      page: 2,
      pageSize: 50,
    });
    await getMemberLookup({ q: "Văn thư", page: 1, pageSize: 10 });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/members?q=Nguy%E1%BB%85n+An&status=Inactive&page=2&pageSize=50",
    );
    expect(fetchMock.mock.calls[1][0]).toBe(
      "/api/v1/members/lookup?q=V%C4%83n+th%C6%B0&page=1&pageSize=10",
    );
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("Authorization")).toBe("Bearer administrator-token");
  });

  it("sends create, partial patch and deactivate requests", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(jsonResponse(memberResponse())));
    vi.stubGlobal("fetch", fetchMock);

    await createMember({
      fullName: "Nguyễn Văn An",
      dateOfBirth: null,
      gender: "Male",
      address: null,
      phone: "0901000000",
      email: null,
      position: null,
      joinDate: null,
      notes: null,
    });
    await updateMember("member-id", {
      fullName: "Tên mới",
      position: null,
    });
    await deactivateMember("member-id");

    expectRequest(fetchMock, 0, "/api/v1/members", "POST", {
      fullName: "Nguyễn Văn An",
      dateOfBirth: null,
      gender: "Male",
      address: null,
      phone: "0901000000",
      email: null,
      position: null,
      joinDate: null,
      notes: null,
    });
    expectRequest(fetchMock, 1, "/api/v1/members/member-id", "PATCH", {
      fullName: "Tên mới",
      position: null,
    });
    expectRequest(
      fetchMock,
      2,
      "/api/v1/members/member-id/deactivate",
      "POST",
      undefined,
    );
  });

  it("downloads the XLSX template and uploads multipart without a content type override", async () => {
    writeValidSession();
    const templateBlob = new Blob(["xlsx"], {
      type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(templateBlob, {
        status: 200,
        headers: {
          "Content-Type": templateBlob.type,
          "Content-Disposition":
            "attachment; filename=DigitalOps-Member-Import-Template.xlsx",
        },
      }))
      .mockResolvedValueOnce(jsonResponse({
        importedCount: 1,
        totalRows: 1,
        errors: [],
      }));
    vi.stubGlobal("fetch", fetchMock);
    const file = new File(["members"], "members.xlsx", {
      type: templateBlob.type,
    });

    const downloaded = await downloadMemberImportTemplate();
    const imported = await importMembers(file);

    expect(downloaded.fileName).toBe("DigitalOps-Member-Import-Template.xlsx");
    expect(downloaded.blob.type).toBe(templateBlob.type);
    expect(imported.importedCount).toBe(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/members/import-template");
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/members/import");
    const downloadHeaders = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(downloadHeaders.get("Accept")).toBe(templateBlob.type);
    const uploadOptions = fetchMock.mock.calls[1][1] as RequestInit;
    const uploadHeaders = new Headers(uploadOptions.headers);
    expect(uploadOptions.method).toBe("POST");
    expect(uploadHeaders.has("Content-Type")).toBe(false);
    expect(uploadOptions.body).toBeInstanceOf(FormData);
    const uploadedFile = (uploadOptions.body as FormData).get("file") as File;
    expect(uploadedFile.name).toBe(file.name);
    expect(uploadedFile.size).toBe(file.size);
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
  expect(
    options.body === undefined
      ? undefined
      : JSON.parse(options.body as string),
  ).toEqual(body);
}

function writeValidSession() {
  writeSession({
    accessToken: "administrator-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  });
}

function memberResponse() {
  return {
    id: "member-id",
    fullName: "Nguyễn Văn An",
    dateOfBirth: null,
    gender: "Male",
    address: null,
    phone: "0901000000",
    email: null,
    position: null,
    joinDate: null,
    status: "Active",
    notes: null,
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
  };
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "Content-Type": "application/json",
    },
  });
}
