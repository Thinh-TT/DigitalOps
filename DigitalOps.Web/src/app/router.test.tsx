import { render, screen } from "@testing-library/react";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { appRoutes } from "./router";
import {
  AuthContext,
  type AuthContextValue,
} from "../shared/auth/auth-context";
import type { Role } from "../shared/auth/types";
import * as memberService from "../shared/members/member-service";
import * as catalogService from "../shared/document-catalog/document-catalog-service";

vi.mock("../shared/members/member-service");
vi.mock("../shared/document-catalog/document-catalog-service");

describe("route guards and App Shell", () => {
  it("redirects an anonymous user to login", async () => {
    renderRoute("/incoming-documents", createAuthValue("anonymous"));

    expect(
      await screen.findByRole("heading", { name: "Đăng nhập DigitalOps" }),
    ).toBeInTheDocument();
  });

  it("redirects forced-password users to change password", async () => {
    renderRoute(
      "/incoming-documents",
      createAuthValue("authenticated", ["Clerk"], true),
    );

    expect(
      await screen.findByRole("heading", { name: "Đổi mật khẩu" }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Điều hướng chính")).not.toBeInTheDocument();
  });

  it("shows Forbidden when a required role is missing", async () => {
    renderRoute(
      "/staff",
      createAuthValue("authenticated", ["Clerk"], false),
    );

    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
  });

  it("renders the shell and the union of multi-role navigation", async () => {
    renderRoute(
      "/incoming-documents",
      createAuthValue(
        "authenticated",
        ["Administrator", "Clerk", "Leader"],
        false,
      ),
    );

    expect(
      await screen.findByRole("heading", { name: "Văn bản đến" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Điều hướng chính")).toBeInTheDocument();
    expect(screen.getByText("Staff")).toBeInTheDocument();
    expect(screen.getByText("Import hội viên")).toBeInTheDocument();
    expect(screen.getByText("Hàng chờ duyệt")).toBeInTheDocument();
    expect(screen.getByText("Phát hành / lưu trữ")).toBeInTheDocument();
  });

  it("allows Administrator and Clerk to open members and blocks other roles", async () => {
    vi.mocked(memberService.getMembers).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
    });
    renderRoute(
      "/members",
      createAuthValue("authenticated", ["Clerk"], false),
    );

    expect(
      await screen.findByRole("heading", { name: "Hội viên" }),
    ).toBeInTheDocument();

    renderRoute(
      "/members",
      createAuthValue("authenticated", ["Leader"], false),
    );
    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
  });

  it("allows Administrator and Clerk to open SCR-005 and blocks other roles", async () => {
    renderRoute(
      "/members/import",
      createAuthValue("authenticated", ["Administrator"], false),
    );
    expect(
      await screen.findByRole("heading", { name: "Import hội viên" }),
    ).toBeInTheDocument();

    renderRoute(
      "/members/import",
      createAuthValue("authenticated", ["Drafter"], false),
    );
    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
  });

  it("allows only Administrator to open SCR-006 and SCR-007", async () => {
    vi.mocked(catalogService.getDocumentTypes).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
    });
    vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([]);
    vi.mocked(catalogService.getDocumentTemplates).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
    });

    renderRoute(
      "/document-types",
      createAuthValue("authenticated", ["Administrator"], false),
    );
    expect(
      await screen.findByRole("heading", { name: "Loại văn bản" }),
    ).toBeInTheDocument();

    renderRoute(
      "/document-templates",
      createAuthValue("authenticated", ["Clerk"], false),
    );
    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
  });
});

function renderRoute(path: string, authValue: AuthContextValue) {
  const router = createMemoryRouter(appRoutes, {
    initialEntries: [path],
  });

  return render(
    <AuthContext.Provider value={authValue}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

function createAuthValue(
  status: AuthContextValue["status"],
  roles: Role[] = [],
  mustChangePassword = false,
): AuthContextValue {
  const currentUser =
    status === "authenticated"
      ? {
          staff: {
            id: "9cf15a35-e213-4b22-9e13-4401f93dd826",
            fullName: "Nguyễn Văn A",
            position: "Chuyên viên",
            department: "Văn phòng",
          },
          roles,
          mustChangePassword,
        }
      : null;

  return {
    status,
    currentUser,
    errorMessage: null,
    establishSession: vi.fn(),
    refreshCurrentUser: vi.fn(),
    logout: vi.fn(),
  };
}
