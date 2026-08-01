import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  ReminderListParameters,
  ReminderResponse,
} from "./types";

export function getReminders(
  parameters: ReminderListParameters = {},
): Promise<PagedResponse<ReminderResponse>> {
  const query = new URLSearchParams();

  for (const [name, value] of Object.entries(parameters)) {
    if (value !== undefined && value !== "") {
      query.set(name, String(value));
    }
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  return apiRequest<PagedResponse<ReminderResponse>>(`/reminders${suffix}`);
}

export function markReminderRead(id: string): Promise<ReminderResponse> {
  return apiRequest<ReminderResponse>(`/reminders/${id}/read`, {
    method: "POST",
  });
}
