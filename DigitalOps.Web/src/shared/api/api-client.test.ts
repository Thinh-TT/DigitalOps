import {
  ApiError,
  apiRequest,
  subscribeToAccessEvents,
  type AccessEvent,
} from "./api-client";
import { readSession, writeSession } from "../auth/session-store";

describe("apiRequest", () => {
  it("attaches the bearer token and returns JSON", async () => {
    writeValidSession();
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        staff: {
          id: "staff-id",
          fullName: "Nguyễn Văn A",
          position: null,
          department: null,
        },
        roles: ["Clerk"],
        mustChangePassword: false,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await apiRequest("/auth/me");

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/auth/me");
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("Authorization")).toBe("Bearer signed-token");
    expect(headers.get("Accept")).toBe("application/json");
  });

  it("clears the session and publishes unauthorized on 401", async () => {
    writeValidSession();
    const events: AccessEvent[] = [];
    const unsubscribe = subscribeToAccessEvents((event) => events.push(event));
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse(
          {
            type: "unauthorized",
            title: "Unauthorized",
            status: 401,
          },
          401,
        ),
      ),
    );

    await expect(apiRequest("/auth/me")).rejects.toMatchObject({
      status: 401,
    });

    expect(readSession()).toBeNull();
    expect(events).toEqual(["unauthorized"]);
    unsubscribe();
  });

  it("distinguishes password-change-required from a generic 403", async () => {
    const events: AccessEvent[] = [];
    const unsubscribe = subscribeToAccessEvents((event) => events.push(event));
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse(
          {
            type: "https://digitalops/errors/password-change-required",
            status: 403,
          },
          403,
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse({ type: "forbidden", status: 403 }, 403),
      );
    vi.stubGlobal("fetch", fetchMock);

    await expect(apiRequest("/documents")).rejects.toBeInstanceOf(ApiError);
    await expect(apiRequest("/staff")).rejects.toBeInstanceOf(ApiError);

    expect(events).toEqual(["password-change-required", "forbidden"]);
    unsubscribe();
  });
});

function writeValidSession() {
  writeSession({
    accessToken: "signed-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  });
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/problem+json",
    },
  });
}
