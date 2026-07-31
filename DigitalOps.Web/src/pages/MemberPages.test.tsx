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
import * as memberService from "../shared/members/member-service";
import type { MemberResponse } from "../shared/members/types";
import {
  MemberCreatePage,
  MemberDetailPage,
  MemberListPage,
} from "./MemberPages";

vi.mock("../shared/members/member-service");

describe("MemberListPage", () => {
  it("loads URL filters and applies a new server-side search", async () => {
    const user = userEvent.setup();
    vi.mocked(memberService.getMembers).mockResolvedValue({
      items: [createMemberResponse()],
      page: 2,
      pageSize: 50,
      totalCount: 51,
      totalPages: 2,
    });
    renderMemberRoute(
      "/members?q=An&status=Active&page=2&pageSize=50",
      "/members",
      <MemberListPage />,
    );

    expect(
      await screen.findByRole("heading", { name: "Hội viên" }),
    ).toBeInTheDocument();
    expect(memberService.getMembers).toHaveBeenCalledWith({
      q: "An",
      status: "Active",
      page: 2,
      pageSize: 50,
    });
    expect(screen.getByText("Nguyễn Văn An")).toBeInTheDocument();

    await user.clear(screen.getByLabelText("Từ khóa hội viên"));
    await user.type(screen.getByLabelText("Từ khóa hội viên"), "Bình");
    await user.click(screen.getByRole("button", { name: /Tìm$/ }));

    await waitFor(() =>
      expect(memberService.getMembers).toHaveBeenLastCalledWith({
        q: "Bình",
        status: "Active",
        page: 1,
        pageSize: 50,
      }),
    );
  });
});

describe("MemberCreatePage", () => {
  it("validates required fields and creates an active member payload", async () => {
    const user = userEvent.setup();
    vi.mocked(memberService.createMember).mockResolvedValue(
      createMemberResponse({ id: "created-member" }),
    );
    renderMemberRoute(
      "/members/new",
      "/members/new",
      <MemberCreatePage />,
      [
        {
          path: "/members/created-member",
          element: <div>created member destination</div>,
        },
      ],
    );

    await user.click(
      screen.getByRole("button", { name: /Tạo hội viên$/ }),
    );
    expect(
      await screen.findByText("Vui lòng nhập họ và tên."),
    ).toBeInTheDocument();
    expect(memberService.createMember).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Họ và tên"), "Nguyễn Văn Mới");
    await user.type(
      screen.getByLabelText("Email"),
      "member@digitalops.local",
    );
    await user.click(
      screen.getByRole("button", { name: /Tạo hội viên$/ }),
    );

    await waitFor(() =>
      expect(memberService.createMember).toHaveBeenCalledWith({
        fullName: "Nguyễn Văn Mới",
        dateOfBirth: null,
        gender: null,
        address: null,
        phone: null,
        email: "member@digitalops.local",
        position: null,
        joinDate: null,
        notes: null,
      }),
    );
    expect(
      await screen.findByText("created member destination"),
    ).toBeInTheDocument();
  });
});

describe("MemberDetailPage", () => {
  it("patches only touched fields and maps cleared values to null", async () => {
    const user = userEvent.setup();
    const original = createMemberResponse();
    vi.mocked(memberService.getMember).mockResolvedValue(original);
    vi.mocked(memberService.updateMember).mockResolvedValue({
      ...original,
      fullName: "Tên đã đổi",
      position: null,
    });
    renderMemberRoute(
      `/members/${original.id}`,
      "/members/:id",
      <MemberDetailPage />,
    );

    await screen.findByDisplayValue(original.fullName);
    await user.clear(screen.getByLabelText("Họ và tên"));
    await user.type(screen.getByLabelText("Họ và tên"), "Tên đã đổi");
    await user.clear(screen.getByLabelText("Chức vụ"));
    await user.click(
      screen.getByRole("button", { name: /Lưu hồ sơ$/ }),
    );

    await waitFor(() =>
      expect(memberService.updateMember).toHaveBeenCalledWith(original.id, {
        fullName: "Tên đã đổi",
        position: null,
      }),
    );
    expect(
      await screen.findByText("Đã cập nhật hồ sơ hội viên."),
    ).toBeInTheDocument();
  });

  it("deactivates with confirmation and can reactivate the member", async () => {
    const user = userEvent.setup();
    const original = createMemberResponse();
    vi.mocked(memberService.getMember).mockResolvedValue(original);
    vi.mocked(memberService.deactivateMember).mockResolvedValue({
      ...original,
      status: "Inactive",
    });
    vi.mocked(memberService.updateMember).mockResolvedValue(original);
    renderMemberRoute(
      `/members/${original.id}`,
      "/members/:id",
      <MemberDetailPage />,
    );

    await screen.findByDisplayValue(original.fullName);
    await user.click(
      screen.getByRole("button", { name: /Ngừng hoạt động$/ }),
    );
    const dialog = screen.getByRole("dialog");
    expect(
      within(dialog).getByText(/vẫn được giữ lại để tra cứu lịch sử/),
    ).toBeInTheDocument();
    await user.click(
      within(dialog).getByRole("button", { name: "Ngừng hoạt động" }),
    );

    await waitFor(() =>
      expect(memberService.deactivateMember).toHaveBeenCalledWith(original.id),
    );
    expect(
      await screen.findByText("Đã ngừng hoạt động hội viên."),
    ).toBeInTheDocument();

    const reactivateButton = screen.getByRole("button", {
      name: /Kích hoạt lại$/,
    });
    await waitFor(() =>
      expect(reactivateButton).not.toHaveClass("ant-btn-loading"),
    );
    await user.click(reactivateButton);
    await waitFor(() =>
      expect(memberService.updateMember).toHaveBeenCalledWith(original.id, {
        status: "Active",
      }),
    );
    expect(
      await screen.findByText("Đã kích hoạt lại hội viên."),
    ).toBeInTheDocument();
  });

  it("keeps the form when deactivate returns a conflict", async () => {
    const user = userEvent.setup();
    const original = createMemberResponse();
    vi.mocked(memberService.getMember).mockResolvedValue(original);
    vi.mocked(memberService.deactivateMember).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail: "Hội viên đã ngừng hoạt động.",
      }),
    );
    renderMemberRoute(
      `/members/${original.id}`,
      "/members/:id",
      <MemberDetailPage />,
    );

    await screen.findByDisplayValue(original.fullName);
    await user.click(
      screen.getByRole("button", { name: /Ngừng hoạt động$/ }),
    );
    await user.click(
      within(screen.getByRole("dialog")).getByRole("button", {
        name: "Ngừng hoạt động",
      }),
    );

    expect(
      await screen.findByText("Hội viên đã ngừng hoạt động."),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue(original.fullName)).toBeInTheDocument();
  });
});

function renderMemberRoute(
  initialEntry: string,
  path: string,
  element: React.ReactNode,
  extraRoutes: Parameters<typeof createMemoryRouter>[0] = [],
) {
  const router = createMemoryRouter(
    [
      {
        path,
        element,
      },
      ...extraRoutes,
    ],
    { initialEntries: [initialEntry] },
  );

  return render(<RouterProvider router={router} />);
}

function createMemberResponse(
  overrides: Partial<MemberResponse> = {},
): MemberResponse {
  return {
    id: "member-id",
    fullName: "Nguyễn Văn An",
    dateOfBirth: "1990-01-02",
    gender: "Male",
    address: "Phường 1",
    phone: "0901000000",
    email: "member@digitalops.local",
    position: "Hội viên",
    joinDate: "2026-07-01",
    status: "Active",
    notes: "Ghi chú",
    createdAt: "2026-07-31T00:00:00Z",
    updatedAt: "2026-07-31T00:00:00Z",
    ...overrides,
  };
}
