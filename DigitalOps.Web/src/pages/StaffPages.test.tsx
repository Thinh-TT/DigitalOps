import {
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { ApiError } from "../shared/api/api-client";
import {
  AuthContext,
  type AuthContextValue,
} from "../shared/auth/auth-context";
import * as staffService from "../shared/staff/staff-service";
import type { StaffResponse } from "../shared/staff/types";
import {
  StaffCreatePage,
  StaffDetailPage,
  StaffListPage,
} from "./StaffPages";

vi.mock("../shared/staff/staff-service");

describe("StaffListPage", () => {
  it("loads a paged table and reloads with the active-only filter", async () => {
    const user = userEvent.setup();
    vi.mocked(staffService.getStaffList).mockResolvedValue({
      items: [createStaffResponse()],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });
    renderStaffRoute("/staff", <StaffListPage />);

    expect(
      await screen.findByRole("heading", { name: "Staff và role" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Cán bộ Văn thư")).toBeInTheDocument();
    expect(staffService.getStaffList).toHaveBeenCalledWith({
      activeOnly: false,
      page: 1,
      pageSize: 20,
    });

    await user.click(screen.getByRole("switch", {
      name: "Chỉ Staff đang hoạt động",
    }));

    await waitFor(() =>
      expect(staffService.getStaffList).toHaveBeenLastCalledWith({
        activeOnly: true,
        page: 1,
        pageSize: 20,
      }),
    );
  });
});

describe("StaffCreatePage", () => {
  it("validates required fields and creates a multi-role temporary account", async () => {
    const user = userEvent.setup();
    vi.mocked(staffService.createStaff).mockResolvedValue(
      createStaffResponse({
        id: "created-id",
        userName: "multi.role",
        roles: ["Clerk", "Leader"],
      }),
    );
    renderStaffRoute("/staff/new", <StaffCreatePage />, [
      {
        path: "/staff/created-id",
        element: <div>created destination</div>,
      },
    ]);

    await user.click(screen.getByRole("button", { name: /Tạo Staff$/ }));
    expect(
      await screen.findByText("Vui lòng nhập tên đăng nhập."),
    ).toBeInTheDocument();
    expect(staffService.createStaff).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Tên đăng nhập"), "multi.role");
    await user.type(
      screen.getByLabelText("Email"),
      "multi.role@digitalops.local",
    );
    await user.type(screen.getByLabelText("Họ và tên"), "Nhân sự đa role");
    await user.type(
      screen.getByLabelText("Mật khẩu tạm"),
      "Temporary2!Password",
    );
    await user.type(
      screen.getByLabelText("Xác nhận mật khẩu tạm"),
      "Temporary2!Password",
    );
    await user.click(screen.getByLabelText("Role"));
    await user.click(await screen.findByText("Văn thư"));
    await user.click(screen.getByLabelText("Role"));
    await user.click(await screen.findByText("Lãnh đạo"));
    await user.click(screen.getByRole("button", { name: /Tạo Staff$/ }));

    await waitFor(() =>
      expect(staffService.createStaff).toHaveBeenCalledWith({
        userName: "multi.role",
        email: "multi.role@digitalops.local",
        temporaryPassword: "Temporary2!Password",
        fullName: "Nhân sự đa role",
        position: null,
        department: null,
        phone: null,
        roles: ["Clerk", "Leader"],
      }),
    );
    expect(await screen.findByText("created destination")).toBeInTheDocument();
  }, 20_000);

  it("keeps form values and displays a conflict returned by the API", async () => {
    const user = userEvent.setup();
    vi.mocked(staffService.createStaff).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail: "Tên đăng nhập hoặc email đã được sử dụng.",
      }),
    );
    renderStaffRoute("/staff/new", <StaffCreatePage />);

    await fillMinimumCreateForm(user);
    await user.click(screen.getByRole("button", { name: /Tạo Staff$/ }));

    expect(
      await screen.findByText("Tên đăng nhập hoặc email đã được sử dụng."),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue("duplicate")).toBeInTheDocument();
  });
});

describe("StaffDetailPage", () => {
  it("sends null for a cleared optional field and preserves omitted fields", async () => {
    const user = userEvent.setup();
    const original = createStaffResponse();
    vi.mocked(staffService.getStaff).mockResolvedValue(original);
    vi.mocked(staffService.updateStaff).mockResolvedValue({
      ...original,
      fullName: "Tên đã đổi",
      position: null,
    });
    renderStaffRoute(`/staff/${original.id}`, <StaffDetailPage />);

    await screen.findByDisplayValue(original.fullName);
    await user.clear(screen.getByLabelText("Họ và tên"));
    await user.type(screen.getByLabelText("Họ và tên"), "Tên đã đổi");
    await user.clear(screen.getByLabelText("Chức vụ"));
    await user.click(screen.getByRole("button", { name: /Lưu hồ sơ$/ }));

    await waitFor(() =>
      expect(staffService.updateStaff).toHaveBeenCalledWith(original.id, {
        fullName: "Tên đã đổi",
        position: null,
      }),
    );
    expect(
      await screen.findByText("Đã cập nhật hồ sơ Staff."),
    ).toBeInTheDocument();
  });

  it("maps reset-password validation and completes a valid reset", async () => {
    const user = userEvent.setup();
    const staff = createStaffResponse();
    vi.mocked(staffService.getStaff).mockResolvedValue(staff);
    vi.mocked(staffService.resetStaffPassword)
      .mockRejectedValueOnce(
        new ApiError(400, {
          status: 400,
          errors: {
            temporaryPassword: ["Mật khẩu phải có ít nhất 6 ký tự."],
          },
        }),
      )
      .mockResolvedValueOnce(undefined);
    renderStaffRoute(`/staff/${staff.id}`, <StaffDetailPage />);

    await screen.findByDisplayValue(staff.fullName);
    await user.click(screen.getByRole("button", {
      name: /Reset mật khẩu$/,
    }));
    const dialog = screen.getByRole("dialog");
    await fillResetForm(user, dialog, "weak");
    await user.click(within(dialog).getByRole("button", {
      name: "Reset mật khẩu",
    }));
    expect(
      await within(dialog).findByText("Mật khẩu phải có ít nhất 6 ký tự."),
    ).toBeInTheDocument();

    await user.clear(within(dialog).getByLabelText("Mật khẩu tạm mới"));
    await user.type(
      within(dialog).getByLabelText("Mật khẩu tạm mới"),
      "Temporary2!Password",
    );
    await user.clear(
      within(dialog).getByLabelText("Xác nhận mật khẩu tạm"),
    );
    await user.type(
      within(dialog).getByLabelText("Xác nhận mật khẩu tạm"),
      "Temporary2!Password",
    );
    await user.click(within(dialog).getByRole("button", {
      name: "Reset mật khẩu",
    }));

    await waitFor(() =>
      expect(staffService.resetStaffPassword).toHaveBeenLastCalledWith(
        staff.id,
        { temporaryPassword: "Temporary2!Password" },
      ),
    );
    expect(
      await screen.findByText(/Đã đặt mật khẩu tạm/),
    ).toBeInTheDocument();
  });

  it("logs out when the Administrator deactivates its own Staff", async () => {
    const user = userEvent.setup();
    const staff = createStaffResponse();
    const logout = vi.fn();
    vi.mocked(staffService.getStaff).mockResolvedValue(staff);
    vi.mocked(staffService.updateStaff).mockResolvedValue({
      ...staff,
      isActive: false,
    });
    renderStaffRoute(
      `/staff/${staff.id}`,
      <StaffDetailPage />,
      [
        {
          path: "/login",
          element: <div>login destination</div>,
        },
      ],
      createAuthValue(logout, staff.id),
    );

    await screen.findByDisplayValue(staff.fullName);
    await user.click(screen.getByRole("button", { name: /Vô hiệu hóa$/ }));
    const dialog = screen.getByRole("dialog");
    expect(
      within(dialog).getByText(/Dữ liệu lịch sử vẫn được giữ nguyên/),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", {
      name: "Vô hiệu hóa",
    }));

    await waitFor(() =>
      expect(staffService.updateStaff).toHaveBeenCalledWith(staff.id, {
        isActive: false,
      }),
    );
    expect(logout).toHaveBeenCalledOnce();
    expect(await screen.findByText("login destination")).toBeInTheDocument();
  });
});

async function fillMinimumCreateForm(
  user: ReturnType<typeof userEvent.setup>,
) {
  await user.type(screen.getByLabelText("Tên đăng nhập"), "duplicate");
  await user.type(
    screen.getByLabelText("Email"),
    "duplicate@digitalops.local",
  );
  await user.type(screen.getByLabelText("Họ và tên"), "Trùng tài khoản");
  await user.type(
    screen.getByLabelText("Mật khẩu tạm"),
    "Temporary2!Password",
  );
  await user.type(
    screen.getByLabelText("Xác nhận mật khẩu tạm"),
    "Temporary2!Password",
  );
  await user.click(screen.getByLabelText("Role"));
  await user.click(await screen.findByText("Văn thư"));
}

async function fillResetForm(
  user: ReturnType<typeof userEvent.setup>,
  dialog: HTMLElement,
  password: string,
) {
  await user.type(
    within(dialog).getByLabelText("Mật khẩu tạm mới"),
    password,
  );
  await user.type(
    within(dialog).getByLabelText("Xác nhận mật khẩu tạm"),
    password,
  );
}

function renderStaffRoute(
  path: string,
  element: React.ReactNode,
  extraRoutes: Parameters<typeof createMemoryRouter>[0] = [],
  authValue = createAuthValue(),
) {
  const router = createMemoryRouter(
    [
      {
        path,
        element,
      },
      ...extraRoutes,
    ],
    { initialEntries: [path] },
  );

  return render(
    <AuthContext.Provider value={authValue}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

function createAuthValue(
  logout = vi.fn(),
  staffId = "current-admin-id",
): AuthContextValue {
  return {
    status: "authenticated",
    currentUser: {
      staff: {
        id: staffId,
        fullName: "Quản trị viên",
        position: null,
        department: null,
      },
      roles: ["Administrator"],
      mustChangePassword: false,
    },
    errorMessage: null,
    establishSession: vi.fn(),
    refreshCurrentUser: vi.fn().mockResolvedValue(undefined),
    logout,
  };
}

function createStaffResponse(
  overrides: Partial<StaffResponse> = {},
): StaffResponse {
  return {
    id: "staff-id",
    identityUserId: "identity-user-id",
    userName: "clerk",
    fullName: "Cán bộ Văn thư",
    position: "Chuyên viên",
    department: "Văn phòng",
    email: "clerk@digitalops.local",
    phone: "0901000000",
    isActive: true,
    roles: ["Clerk"],
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}
