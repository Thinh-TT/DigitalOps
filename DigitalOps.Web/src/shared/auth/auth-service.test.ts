import { readSession, writeSession } from "./session-store";
import { changePassword, login } from "./auth-service";

describe("auth-service", () => {
  it("posts login credentials without bearer authentication", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(loginResponse()));
    vi.stubGlobal("fetch", fetchMock);

    await login({
      userNameOrEmail: "clerk",
      password: "Valid1!Password",
    });

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/auth/login");
    const options = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(options.headers);
    expect(options.method).toBe("POST");
    expect(headers.has("Authorization")).toBe(false);
    expect(JSON.parse(options.body as string)).toEqual({
      userNameOrEmail: "clerk",
      password: "Valid1!Password",
    });
    expect(readSession()).toBeNull();
  });

  it("posts change-password with the persisted bearer token", async () => {
    writeSession({
      accessToken: "forced-password-token",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(loginResponse()));
    vi.stubGlobal("fetch", fetchMock);

    await changePassword({
      currentPassword: "Valid1!Password",
      newPassword: "Changed2!Password",
    });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/auth/change-password",
    );
    const options = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(options.headers);
    expect(options.method).toBe("POST");
    expect(headers.get("Authorization")).toBe(
      "Bearer forced-password-token",
    );
    expect(JSON.parse(options.body as string)).toEqual({
      currentPassword: "Valid1!Password",
      newPassword: "Changed2!Password",
    });
  });
});

function loginResponse() {
  return {
    accessToken: "new-signed-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    staff: {
      id: "9cf15a35-e213-4b22-9e13-4401f93dd826",
      fullName: "Nguyễn Văn A",
      position: "Chuyên viên",
      department: "Văn phòng",
    },
    roles: ["Clerk"],
    mustChangePassword: false,
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
