import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { ApiError } from "../shared/api/api-client";
import {
  AuthContext,
  type AuthContextValue,
} from "../shared/auth/auth-context";
import * as authService from "../shared/auth/auth-service";
import type { LoginResponse } from "../shared/auth/types";
import { ChangePasswordPage, LoginPage } from "./AuthPages";

vi.mock("../shared/auth/auth-service");

describe("LoginPage", () => {
  it("validates required fields before calling the API", async () => {
    const user = userEvent.setup();
    renderLoginRoute(createAuthValue("anonymous"));

    await user.click(screen.getByRole("button", { name: /Đăng nhập$/ }));

    expect(
      await screen.findByText("Vui lòng nhập tên đăng nhập hoặc email."),
    ).toBeInTheDocument();
    expect(screen.getByText("Vui lòng nhập mật khẩu.")).toBeInTheDocument();
    expect(authService.login).not.toHaveBeenCalled();
  });

  it("establishes a forced-password session and navigates to change-password", async () => {
    const user = userEvent.setup();
    const establishSession = vi.fn().mockResolvedValue(undefined);
    const response = createLoginResponse(true);
    vi.mocked(authService.login).mockResolvedValue(response);
    renderLoginRoute(createAuthValue("anonymous", false, establishSession));

    await user.type(
      screen.getByLabelText("Tên đăng nhập hoặc email"),
      "forced",
    );
    await user.type(screen.getByLabelText("Mật khẩu"), "Valid1!Password");
    await user.click(screen.getByRole("button", { name: /Đăng nhập$/ }));

    await waitFor(() =>
      expect(authService.login).toHaveBeenCalledWith({
        userNameOrEmail: "forced",
        password: "Valid1!Password",
      }),
    );
    expect(establishSession).toHaveBeenCalledWith(response);
    expect(
      await screen.findByText("change-password destination"),
    ).toBeInTheDocument();
  });

  it("shows the generic message for every unauthorized login", async () => {
    const user = userEvent.setup();
    vi.mocked(authService.login).mockRejectedValue(
      new ApiError(401, {
        status: 401,
        detail: "Sensitive server detail",
      }),
    );
    renderLoginRoute(createAuthValue("anonymous"));

    await user.type(
      screen.getByLabelText("Tên đăng nhập hoặc email"),
      "inactive",
    );
    await user.type(screen.getByLabelText("Mật khẩu"), "Valid1!Password");
    await user.click(screen.getByRole("button", { name: /Đăng nhập$/ }));

    expect(
      await screen.findByText(
        "Tên đăng nhập/email hoặc mật khẩu không đúng.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText("Sensitive server detail")).not.toBeInTheDocument();
    expect(screen.getByDisplayValue("inactive")).toBeInTheDocument();
  });
});

describe("ChangePasswordPage", () => {
  it("rejects a mismatched confirmation without calling the API", async () => {
    const user = userEvent.setup();
    renderChangePasswordRoute(createAuthValue("authenticated", true));

    await fillPasswordForm(
      user,
      "Valid1!Password",
      "Changed2!Password",
      "Other2!Password",
    );
    await user.click(
      screen.getByRole("button", { name: /Đổi mật khẩu$/ }),
    );

    expect(
      await screen.findByText("Mật khẩu xác nhận không khớp."),
    ).toBeInTheDocument();
    expect(authService.changePassword).not.toHaveBeenCalled();
  });

  it("maps server validation to the matching password field", async () => {
    const user = userEvent.setup();
    vi.mocked(authService.changePassword).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          currentPassword: ["Mật khẩu hiện tại không đúng."],
        },
      }),
    );
    renderChangePasswordRoute(createAuthValue("authenticated", true));

    await fillPasswordForm(
      user,
      "Wrong1!Password",
      "Changed2!Password",
      "Changed2!Password",
    );
    await user.click(
      screen.getByRole("button", { name: /Đổi mật khẩu$/ }),
    );

    expect(
      await screen.findByText("Mật khẩu hiện tại không đúng."),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue("Wrong1!Password")).toBeInTheDocument();
  });

  it("replaces the session after success and supports forced logout", async () => {
    const user = userEvent.setup();
    const establishSession = vi.fn().mockResolvedValue(undefined);
    const logout = vi.fn();
    const response = createLoginResponse(false);
    vi.mocked(authService.changePassword).mockResolvedValue(response);
    const firstRender = renderChangePasswordRoute(
      createAuthValue("authenticated", true, establishSession, logout),
    );

    await fillPasswordForm(
      user,
      "Valid1!Password",
      "Changed2!Password",
      "Changed2!Password",
    );
    await user.click(
      screen.getByRole("button", { name: /Đổi mật khẩu$/ }),
    );

    await waitFor(() =>
      expect(authService.changePassword).toHaveBeenCalledWith({
        currentPassword: "Valid1!Password",
        newPassword: "Changed2!Password",
      }),
    );
    expect(establishSession).toHaveBeenCalledWith(response);
    expect(await screen.findByText("business destination")).toBeInTheDocument();

    firstRender.unmount();
    renderChangePasswordRoute(
      createAuthValue("authenticated", true, establishSession, logout),
    );
    await user.click(screen.getByRole("button", { name: /Đăng xuất$/ }));
    expect(logout).toHaveBeenCalledOnce();
    expect(await screen.findByText("login destination")).toBeInTheDocument();
  });
});

function renderLoginRoute(authValue: AuthContextValue) {
  const router = createMemoryRouter(
    [
      {
        path: "/login",
        element: <LoginPage />,
      },
      {
        path: "/change-password",
        element: <div>change-password destination</div>,
      },
      {
        path: "/incoming-documents",
        element: <div>business destination</div>,
      },
    ],
    { initialEntries: ["/login"] },
  );

  return renderWithAuth(router, authValue);
}

function renderChangePasswordRoute(authValue: AuthContextValue) {
  const router = createMemoryRouter(
    [
      {
        path: "/login",
        element: <div>login destination</div>,
      },
      {
        path: "/change-password",
        element: <ChangePasswordPage />,
      },
      {
        path: "/incoming-documents",
        element: <div>business destination</div>,
      },
    ],
    { initialEntries: ["/change-password"] },
  );

  return renderWithAuth(router, authValue);
}

function renderWithAuth(
  router: ReturnType<typeof createMemoryRouter>,
  authValue: AuthContextValue,
) {
  return render(
    <AuthContext.Provider value={authValue}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

function createAuthValue(
  status: AuthContextValue["status"],
  mustChangePassword = false,
  establishSession = vi.fn().mockResolvedValue(undefined),
  logout = vi.fn(),
): AuthContextValue {
  return {
    status,
    currentUser:
      status === "authenticated"
        ? {
            staff: {
              id: "9cf15a35-e213-4b22-9e13-4401f93dd826",
              fullName: "Nguyễn Văn A",
              position: "Chuyên viên",
              department: "Văn phòng",
            },
            roles: ["Clerk"],
            mustChangePassword,
          }
        : null,
    errorMessage: null,
    establishSession,
    refreshCurrentUser: vi.fn().mockResolvedValue(undefined),
    logout,
  };
}

function createLoginResponse(mustChangePassword: boolean): LoginResponse {
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
    mustChangePassword,
  };
}

async function fillPasswordForm(
  user: ReturnType<typeof userEvent.setup>,
  currentPassword: string,
  newPassword: string,
  confirmPassword: string,
) {
  await user.type(
    screen.getByLabelText("Mật khẩu hiện tại"),
    currentPassword,
  );
  await user.type(screen.getByLabelText("Mật khẩu mới"), newPassword);
  await user.type(
    screen.getByLabelText("Xác nhận mật khẩu mới"),
    confirmPassword,
  );
}
