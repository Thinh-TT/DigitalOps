import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  MemberCreateRequest,
  MemberListParameters,
  MemberLookupParameters,
  MemberLookupResponse,
  MemberResponse,
  MemberUpdateRequest,
} from "./types";

export function getMembers(
  parameters: MemberListParameters = {},
): Promise<PagedResponse<MemberResponse>> {
  return apiRequest<PagedResponse<MemberResponse>>(
    `/members${buildQuery(parameters)}`,
  );
}

export function getMemberLookup(
  parameters: MemberLookupParameters = {},
): Promise<PagedResponse<MemberLookupResponse>> {
  return apiRequest<PagedResponse<MemberLookupResponse>>(
    `/members/lookup${buildQuery(parameters)}`,
  );
}

export function getMember(id: string): Promise<MemberResponse> {
  return apiRequest<MemberResponse>(`/members/${id}`);
}

export function createMember(
  request: MemberCreateRequest,
): Promise<MemberResponse> {
  return apiRequest<MemberResponse>("/members", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateMember(
  id: string,
  request: MemberUpdateRequest,
): Promise<MemberResponse> {
  return apiRequest<MemberResponse>(`/members/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export function deactivateMember(id: string): Promise<MemberResponse> {
  return apiRequest<MemberResponse>(`/members/${id}/deactivate`, {
    method: "POST",
  });
}

function buildQuery(parameters: {
  q?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}): string {
  const query = new URLSearchParams();

  if (parameters.q !== undefined && parameters.q.trim().length > 0) {
    query.set("q", parameters.q.trim());
  }
  if (parameters.status !== undefined) {
    query.set("status", parameters.status);
  }
  if (parameters.page !== undefined) {
    query.set("page", String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set("pageSize", String(parameters.pageSize));
  }

  return query.size > 0 ? `?${query.toString()}` : "";
}
