import {
  apiDownload,
  apiRequest,
  type DownloadedFile,
} from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  IncomingDocumentCreateRequest,
  IncomingDocumentListParameters,
  IncomingDocumentResponse,
  IncomingDocumentUpdateRequest,
  AttachmentResponse,
  AssignmentConfirmRequest,
  AssignmentSuggestionResponse,
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

export function suggestIncomingDocumentAssignment(
  id: string,
): Promise<AssignmentSuggestionResponse> {
  return apiRequest<AssignmentSuggestionResponse>(
    `/incoming-documents/${id}/assignment-suggestion`,
    { method: "POST" },
  );
}

export function confirmIncomingDocumentAssignment(
  id: string,
  request: AssignmentConfirmRequest,
): Promise<IncomingDocumentResponse> {
  return apiRequest<IncomingDocumentResponse>(
    `/incoming-documents/${id}/assignment`,
    { method: "POST", body: JSON.stringify(request) },
  );
}

export function uploadIncomingAttachment(
  incomingDocumentId: string,
  file: File,
): Promise<AttachmentResponse> {
  const form = new FormData();
  form.append("file", file, file.name);

  return apiRequest<AttachmentResponse>(
    `/incoming-documents/${incomingDocumentId}/attachments`,
    { method: "POST", body: form },
  );
}

export function downloadAttachment(id: string): Promise<DownloadedFile> {
  return apiDownload(`/attachments/${id}/download`, {}, "*/*");
}

export function deleteAttachment(id: string): Promise<void> {
  return apiRequest<void>(`/attachments/${id}`, { method: "DELETE" });
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
