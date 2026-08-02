import { apiDownload, apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type { AttachmentResponse } from "../incoming-documents/types";
import type {
  AiDraftRequest,
  OutgoingDocumentCreateRequest,
  OutgoingDocumentListParameters,
  OutgoingDocumentResponse,
  OutgoingDocumentUpdateRequest,
} from "./types";

export function getOutgoingDocuments(parameters: OutgoingDocumentListParameters = {}) {
  return apiRequest<PagedResponse<OutgoingDocumentResponse>>(`/outgoing-documents${buildQuery(parameters)}`);
}

export function getOutgoingDocument(id: string) {
  return apiRequest<OutgoingDocumentResponse>(`/outgoing-documents/${id}`);
}

export function createOutgoingDocument(request: OutgoingDocumentCreateRequest) {
  return apiRequest<OutgoingDocumentResponse>("/outgoing-documents", { method: "POST", body: JSON.stringify(request) });
}

export function updateOutgoingDocument(id: string, request: OutgoingDocumentUpdateRequest) {
  return apiRequest<OutgoingDocumentResponse>(`/outgoing-documents/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export function generateOutgoingAiDraft(id: string, request: AiDraftRequest) {
  return apiRequest<OutgoingDocumentResponse>(`/outgoing-documents/${id}/ai-draft`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function uploadOutgoingAttachment(documentId: string, file: File) {
  const form = new FormData();
  form.append("file", file, file.name);
  return apiRequest<AttachmentResponse>(`/outgoing-documents/${documentId}/attachments`, { method: "POST", body: form });
}

export function deleteOutgoingAttachment(id: string) {
  return apiRequest<void>(`/attachments/${id}`, { method: "DELETE" });
}

export function downloadOutgoingAttachment(id: string) {
  return apiDownload(`/attachments/${id}/download`, {}, "*/*").then(result => ({
    ...result,
    fileName: result.fileName ?? "attachment",
  }));
}

function buildQuery(parameters: OutgoingDocumentListParameters): string {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(parameters)) {
    if (value !== undefined && value !== "") query.set(name, String(value));
  }
  return query.size ? `?${query.toString()}` : "";
}
