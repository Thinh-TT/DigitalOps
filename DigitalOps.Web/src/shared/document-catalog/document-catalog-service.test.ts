import { writeSession } from "../auth/session-store";
import {
  createDocumentTemplate,
  createDocumentType,
  getAllDocumentTypes,
  getDocumentTemplates,
  getDocumentTypes,
  updateDocumentTemplate,
  updateDocumentType,
} from "./document-catalog-service";

describe("document-catalog-service", () => {
  it("builds catalog filters and pages through all document types", async () => {
    writeValidSession();
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(pageResponse([typeResponse("one")], 1, 2)))
      .mockResolvedValueOnce(jsonResponse(pageResponse([typeResponse("two")], 2, 2)))
      .mockResolvedValueOnce(jsonResponse(pageResponse([], 1, 0)));
    vi.stubGlobal("fetch", fetchMock);

    const all = await getAllDocumentTypes(true);
    await getDocumentTemplates({
      documentTypeId: "type-id",
      activeOnly: true,
      page: 2,
      pageSize: 50,
    });

    expect(all.map((item) => item.code)).toEqual(["one", "two"]);
    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/document-types?activeOnly=true&page=1&pageSize=100",
    );
    expect(fetchMock.mock.calls[1][0]).toBe(
      "/api/v1/document-types?activeOnly=true&page=2&pageSize=100",
    );
    expect(fetchMock.mock.calls[2][0]).toBe(
      "/api/v1/document-templates?documentTypeId=type-id&activeOnly=true&page=2&pageSize=50",
    );
  });

  it("sends create and partial patch payloads for both resources", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(jsonResponse(typeResponse("REPORT"))));
    vi.stubGlobal("fetch", fetchMock);

    await createDocumentType({
      code: "REPORT",
      name: "Báo cáo",
      description: null,
      isActive: true,
    });
    await updateDocumentType("type-id", { description: null, isActive: false });
    await createDocumentTemplate({
      documentTypeId: "type-id",
      name: "Mẫu báo cáo",
      templateContent: "Nội dung",
      formatRules: { version: 1, rules: [] },
      isActive: true,
    });
    await updateDocumentTemplate("template-id", { templateContent: "Mới" });

    expectRequest(fetchMock, 0, "/api/v1/document-types", "POST", {
      code: "REPORT",
      name: "Báo cáo",
      description: null,
      isActive: true,
    });
    expectRequest(fetchMock, 1, "/api/v1/document-types/type-id", "PATCH", {
      description: null,
      isActive: false,
    });
    expectRequest(fetchMock, 2, "/api/v1/document-templates", "POST", {
      documentTypeId: "type-id",
      name: "Mẫu báo cáo",
      templateContent: "Nội dung",
      formatRules: { version: 1, rules: [] },
      isActive: true,
    });
    expectRequest(
      fetchMock,
      3,
      "/api/v1/document-templates/template-id",
      "PATCH",
      { templateContent: "Mới" },
    );
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("Authorization")).toBe("Bearer administrator-token");
  });

  it("omits activeOnly=false from list requests", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(pageResponse([], 1, 0)));
    vi.stubGlobal("fetch", fetchMock);

    await getDocumentTypes({ activeOnly: false });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/document-types");
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
    accessToken: "administrator-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  });
}

function typeResponse(code: string) {
  return {
    id: `${code}-id`,
    code,
    name: `Loại ${code}`,
    description: null,
    isActive: true,
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
  };
}

function pageResponse(items: unknown[], page: number, totalPages: number) {
  return {
    items,
    page,
    pageSize: 100,
    totalCount: items.length * totalPages,
    totalPages,
  };
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
