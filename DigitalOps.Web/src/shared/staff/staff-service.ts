import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  ResetPasswordRequest,
  RoleAssignmentRequest,
  StaffCreateRequest,
  StaffListParameters,
  StaffResponse,
  StaffUpdateRequest,
} from "./types";

export function getStaffList(
  parameters: StaffListParameters = {},
): Promise<PagedResponse<StaffResponse>> {
  const query = new URLSearchParams();

  if (parameters.activeOnly !== undefined) {
    query.set("activeOnly", String(parameters.activeOnly));
  }

  if (parameters.page !== undefined) {
    query.set("page", String(parameters.page));
  }

  if (parameters.pageSize !== undefined) {
    query.set("pageSize", String(parameters.pageSize));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  return apiRequest<PagedResponse<StaffResponse>>(`/staff${suffix}`);
}

export async function getAllActiveStaff(): Promise<StaffResponse[]> {
  const staff: StaffResponse[] = [];
  let page = 1;

  while (true) {
    const response = await getStaffList({
      activeOnly: true,
      page,
      pageSize: 100,
    });
    staff.push(...response.items);
    if (page >= response.totalPages) {
      return staff;
    }

    page += 1;
  }
}

export function getStaff(id: string): Promise<StaffResponse> {
  return apiRequest<StaffResponse>(`/staff/${id}`);
}

export function createStaff(
  request: StaffCreateRequest,
): Promise<StaffResponse> {
  return apiRequest<StaffResponse>("/staff", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateStaff(
  id: string,
  request: StaffUpdateRequest,
): Promise<StaffResponse> {
  return apiRequest<StaffResponse>(`/staff/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export function replaceStaffRoles(
  id: string,
  request: RoleAssignmentRequest,
): Promise<StaffResponse> {
  return apiRequest<StaffResponse>(`/staff/${id}/roles`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export function resetStaffPassword(
  id: string,
  request: ResetPasswordRequest,
): Promise<void> {
  return apiRequest<void>(`/staff/${id}/reset-password`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}
