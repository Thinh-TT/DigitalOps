import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import {
  AuthContext,
  type AuthContextValue,
} from "../shared/auth/auth-context";
import type { Role } from "../shared/auth/types";
import { ReminderBadgeContext } from "../shared/reminders/reminder-badge-context";
import * as reminderService from "../shared/reminders/reminder-service";
import type { ReminderResponse } from "../shared/reminders/types";
import * as staffService from "../shared/staff/staff-service";
import { ReminderPage } from "./ReminderPage";

vi.mock("../shared/reminders/reminder-service");
vi.mock("../shared/staff/staff-service");

beforeEach(() => {
  vi.mocked(reminderService.getReminders).mockResolvedValue({
    items: [createReminder()],
    page: 1,
    pageSize: 20,
    totalCount: 1,
    totalPages: 1,
  });
  vi.mocked(staffService.getStaffList).mockResolvedValue({
    items: [
      {
        id: "staff-id",
        identityUserId: "user-id",
        userName: "target",
        fullName: "Cán bộ nhận việc",
        position: null,
        department: null,
        email: "target@example.local",
        phone: null,
        isActive: false,
        roles: ["Clerk"],
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: "2026-08-01T00:00:00Z",
      },
    ],
    page: 1,
    pageSize: 100,
    totalCount: 1,
    totalPages: 1,
  });
  vi.mocked(reminderService.markReminderRead).mockResolvedValue({
    ...createReminder(),
    deliveryStatus: "Read",
    readAt: "2026-08-01T02:30:00Z",
  });
});

describe("Reminder page", () => {
  it("loads URL filters, renders an unread reminder and marks it read", async () => {
    const user = userEvent.setup();
    const refreshUnreadCount = vi.fn().mockResolvedValue(undefined);
    renderReminderRoute(
      "/reminders?deliveryStatus=Unread&page=2&pageSize=50",
      ["Clerk"],
      refreshUnreadCount,
    );

    expect(await screen.findByText("01/NH")).toBeInTheDocument();
    expect(reminderService.getReminders).toHaveBeenCalledWith({
      deliveryStatus: "Unread",
      recipientStaffId: undefined,
      page: 2,
      pageSize: 50,
    });
    expect(screen.getByText("Sắp đến hạn")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Đánh dấu đã đọc$/ }));

    await waitFor(() => expect(reminderService.markReminderRead)
      .toHaveBeenCalledWith("reminder-id"));
    expect(refreshUnreadCount).toHaveBeenCalled();
  });

  it("shows the administrator recipient filter and retrieves all Staff", async () => {
    renderReminderRoute("/reminders", ["Administrator"], vi.fn());

    expect(await screen.findByLabelText("Người nhận thông báo")).toBeInTheDocument();
    expect(staffService.getStaffList).toHaveBeenCalledWith({ page: 1, pageSize: 100 });
  });

  it("shows an error while preserving the screen when loading fails", async () => {
    vi.mocked(reminderService.getReminders).mockRejectedValue(
      new Error("Máy chủ không phản hồi."),
    );
    renderReminderRoute("/reminders", ["Clerk"], vi.fn());

    expect(await screen.findByText("Máy chủ không phản hồi.")).toBeInTheDocument();
  });
});

function renderReminderRoute(
  initialEntry: string,
  roles: Role[],
  refreshUnreadCount: () => Promise<void>,
) {
  const router = createMemoryRouter(
    [{ path: "/reminders", element: <ReminderPage /> }],
    { initialEntries: [initialEntry] },
  );

  return render(
    <AuthContext.Provider value={createAuthValue(roles)}>
      <ReminderBadgeContext.Provider value={{
        unreadCount: 1,
        refreshUnreadCount,
      }}>
        <RouterProvider router={router} />
      </ReminderBadgeContext.Provider>
    </AuthContext.Provider>,
  );
}

function createAuthValue(roles: Role[]): AuthContextValue {
  return {
    status: "authenticated",
    currentUser: {
      staff: {
        id: "current-staff",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
      roles,
      mustChangePassword: false,
    },
    errorMessage: null,
    establishSession: vi.fn(),
    refreshCurrentUser: vi.fn(),
    logout: vi.fn(),
  };
}

function createReminder(
  overrides: Partial<ReminderResponse> = {},
): ReminderResponse {
  return {
    id: "reminder-id",
    incomingDocumentId: "incoming-id",
    referenceNumber: "01/NH",
    summary: "Thông báo cần xử lý",
    reminderKind: "BeforeDeadline",
    reminderDate: "2026-08-01",
    deliveryStatus: "Unread",
    createdAt: "2026-08-01T02:00:00Z",
    readAt: null,
    ...overrides,
  };
}
