import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  IncomingDocumentCreateRequest,
  IncomingDocumentListParameters,
  IncomingDocumentResponse,
  IncomingDocumentUpdateRequest,
} from "./types";

export function getIncomingDocuments(
  parameters: IncomingDocumentListParameters = {},
): Promise<PagedResponse<IncomingDocumentResponse>> {
  return apiRequest<PagedResponse<IncomingDocumentResponse>>(
    `/incoming-documents${buildQuery(parameters)}`,
  );
}

export function getIncomingDocument(
  id: string,
): Promise<IncomingDocumentResponse> {
  return apiRequest<IncomingDocumentResponse>(`/incoming-documents/${id}`);
}

export function createIncomingDocument(
  request: IncomingDocumentCreateRequest,
): Promise<IncomingDocumentResponse> {
  return apiRequest<IncomingDocumentResponse>("/incoming-documents", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateIncomingDocument(
  id: string,
  request: IncomingDocumentUpdateRequest,
): Promise<IncomingDocumentResponse> {
  return apiRequest<IncomingDocumentResponse>(`/incoming-documents/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export function completeIncomingDocument(
  id: string,
): Promise<IncomingDocumentResponse> {
  return apiRequest<IncomingDocumentResponse>(
    `/incoming-documents/${id}/complete`,
    { method: "POST" },
  );
}

function buildQuery(parameters: IncomingDocumentListParameters): string {
  const query = new URLSearchParams();

  for (const [name, value] of Object.entries(parameters)) {
    if (value !== undefined && value !== "") {
      query.set(name, String(value));
    }
  }

  return query.size > 0 ? `?${query.toString()}` : "";
}
