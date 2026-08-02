import { writeSession } from "../auth/session-store";
import {
  createStaff,
  getAllActiveStaff,
  getStaffList,
  replaceStaffRoles,
  resetStaffPassword,
  updateStaff,
} from "./staff-service";

describe("staff-service", () => {
  it("builds the server-side list query and attaches the bearer token", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        items: [],
        page: 2,
        pageSize: 50,
        totalCount: 0,
        totalPages: 0,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await getStaffList({
      activeOnly: true,
      page: 2,
      pageSize: 50,
    });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/staff?activeOnly=true&page=2&pageSize=50",
    );
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("Authorization")).toBe("Bearer administrator-token");
  });

  it("sends create, patch and role payloads without adding client fields", async () => {
    writeValidSession();
    const fetchMock = vi
      .fn()
      .mockImplementation(() => Promise.resolve(jsonResponse(staffResponse())));
    vi.stubGlobal("fetch", fetchMock);

    await createStaff({
      userName: "new.staff",
      email: "new.staff@digitalops.local",
      temporaryPassword: "Temporary2!Password",
      fullName: "Nhân sự mới",
      position: null,
      department: "Văn phòng",
      phone: null,
      roles: ["Clerk", "Leader"],
    });
    await updateStaff("staff-id", {
      fullName: "Tên mới",
      position: null,
    });
    await replaceStaffRoles("staff-id", {
      roles: ["Drafter", "Leader"],
    });

    expectRequest(fetchMock, 0, "/api/v1/staff", "POST", {
      userName: "new.staff",
      email: "new.staff@digitalops.local",
      temporaryPassword: "Temporary2!Password",
      fullName: "Nhân sự mới",
      position: null,
      department: "Văn phòng",
      phone: null,
      roles: ["Clerk", "Leader"],
    });
    expectRequest(fetchMock, 1, "/api/v1/staff/staff-id", "PATCH", {
      fullName: "Tên mới",
      position: null,
    });
    expectRequest(
      fetchMock,
      2,
      "/api/v1/staff/staff-id/roles",
      "PUT",
      {
        roles: ["Drafter", "Leader"],
      },
    );
  });

  it("handles the no-content reset-password response", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, {
      status: 204,
    }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      resetStaffPassword("staff-id", {
        temporaryPassword: "Temporary2!Password",
      }),
    ).resolves.toBeUndefined();
    expectRequest(
      fetchMock,
      0,
      "/api/v1/staff/staff-id/reset-password",
      "POST",
      {
        temporaryPassword: "Temporary2!Password",
      },
    );
  });

  it("loads every page of the active Staff directory", async () => {
    writeValidSession();
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({
        items: [{ ...staffResponse(), id: "staff-1" }],
        page: 1,
        pageSize: 100,
        totalCount: 2,
        totalPages: 2,
      }))
      .mockResolvedValueOnce(jsonResponse({
        items: [{ ...staffResponse(), id: "staff-2" }],
        page: 2,
        pageSize: 100,
        totalCount: 2,
        totalPages: 2,
      }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await getAllActiveStaff();

    expect(result.map((staff) => staff.id)).toEqual(["staff-1", "staff-2"]);
    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/staff?activeOnly=true&page=1&pageSize=100",
    );
    expect(fetchMock.mock.calls[1][0]).toBe(
      "/api/v1/staff?activeOnly=true&page=2&pageSize=100",
    );
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

function staffResponse() {
  return {
    id: "staff-id",
    identityUserId: "user-id",
    userName: "new.staff",
    fullName: "Nhân sự mới",
    position: null,
    department: "Văn phòng",
    email: "new.staff@digitalops.local",
    phone: null,
    isActive: true,
    roles: ["Clerk", "Leader"],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
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
