import { writeSession } from "../auth/session-store";
import { getReminders, markReminderRead } from "./reminder-service";

describe("reminder-service", () => {
  it("builds the list query and marks a reminder as read", async () => {
    writeSession({
      accessToken: "staff-token",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ items: [] }))
      .mockResolvedValueOnce(jsonResponse({ id: "reminder-id" }));
    vi.stubGlobal("fetch", fetchMock);

    await getReminders({
      deliveryStatus: "Unread",
      recipientStaffId: "staff-id",
      page: 2,
      pageSize: 50,
    });
    await markReminderRead("reminder-id");

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/v1/reminders?deliveryStatus=Unread&recipientStaffId=staff-id&page=2&pageSize=50",
    );
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get("Authorization")).toBe(
      "Bearer staff-token",
    );
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/reminders/reminder-id/read");
    expect(fetchMock.mock.calls[1][1]?.method).toBe("POST");
  });
});

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
