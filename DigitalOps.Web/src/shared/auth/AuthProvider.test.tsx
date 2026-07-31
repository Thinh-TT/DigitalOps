import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AuthProvider } from "./AuthProvider";
import { useAuth } from "./auth-context";
import { readSession, writeSession } from "./session-store";

describe("AuthProvider", () => {
  it("becomes anonymous when no persisted session exists", async () => {
    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    expect(await screen.findByText("anonymous")).toBeInTheDocument();
  });

  it("loads GET /auth/me for a persisted session", async () => {
    writeValidSession();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(currentUserResponse()));

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    expect(await screen.findByText("authenticated")).toBeInTheDocument();
    expect(screen.getByText("Nguyễn Văn A")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/auth/me",
      expect.objectContaining({
        headers: expect.any(Headers),
      }),
    );
  });

  it("clears an invalid server session after GET /auth/me returns 401", async () => {
    writeValidSession();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            type: "unauthorized",
            title: "Unauthorized",
            status: 401,
          }),
          {
            status: 401,
            headers: {
              "Content-Type": "application/problem+json",
            },
          },
        ),
      ),
    );

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    expect(await screen.findByText("anonymous")).toBeInTheDocument();
    expect(readSession()).toBeNull();
  });

  it("keeps the session on a network error and supports retry", async () => {
    writeValidSession();
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new Error("Mất kết nối"))
      .mockResolvedValueOnce(currentUserResponse());
    vi.stubGlobal("fetch", fetchMock);

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    expect(await screen.findByText("error")).toBeInTheDocument();
    expect(screen.getByText("Mất kết nối")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "retry" }));

    await waitFor(() =>
      expect(screen.getByText("authenticated")).toBeInTheDocument(),
    );
  });

  it("establishes a login session through GET /auth/me and clears it on logout", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(currentUserResponse()));

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>,
    );

    expect(await screen.findByText("anonymous")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "establish" }));

    await waitFor(() =>
      expect(screen.getByText("authenticated")).toBeInTheDocument(),
    );
    expect(readSession()?.accessToken).toBe("new-signed-token");

    fireEvent.click(screen.getByRole("button", { name: "logout" }));

    expect(screen.getByText("anonymous")).toBeInTheDocument();
    expect(readSession()).toBeNull();
  });
});

function AuthProbe() {
  const auth = useAuth();

  return (
    <div>
      <span>{auth.status}</span>
      <span>{auth.currentUser?.staff.fullName}</span>
      <span>{auth.errorMessage}</span>
      <button type="button" onClick={() => void auth.refreshCurrentUser()}>
        retry
      </button>
      <button
        type="button"
        onClick={() =>
          void auth.establishSession({
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
          })
        }
      >
        establish
      </button>
      <button type="button" onClick={auth.logout}>
        logout
      </button>
    </div>
  );
}

function writeValidSession() {
  writeSession({
    accessToken: "signed-token",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  });
}

function currentUserResponse(): Response {
  return new Response(
    JSON.stringify({
      staff: {
        id: "9cf15a35-e213-4b22-9e13-4401f93dd826",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
      roles: ["Clerk"],
      mustChangePassword: false,
    }),
    {
      status: 200,
      headers: {
        "Content-Type": "application/json",
      },
    },
  );
}
