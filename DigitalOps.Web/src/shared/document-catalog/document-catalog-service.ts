import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type {
  DocumentTemplateCreateRequest,
  DocumentTemplateListParameters,
  DocumentTemplateResponse,
  DocumentTemplateUpdateRequest,
  DocumentTypeCreateRequest,
  DocumentTypeListParameters,
  DocumentTypeResponse,
  DocumentTypeUpdateRequest,
} from "./types";

export function getDocumentTypes(
  parameters: DocumentTypeListParameters = {},
): Promise<PagedResponse<DocumentTypeResponse>> {
  return apiRequest<PagedResponse<DocumentTypeResponse>>(
    `/document-types${buildQuery(parameters)}`,
  );
}

export async function getAllDocumentTypes(
  activeOnly?: boolean,
): Promise<DocumentTypeResponse[]> {
  const items: DocumentTypeResponse[] = [];
  for (let page = 1; ; page += 1) {
    const response = await getDocumentTypes({
      activeOnly,
      page,
      pageSize: 100,
    });
    items.push(...response.items);
    if (page >= response.totalPages) {
      break;
    }
  }

  return items;
}

export function getDocumentType(id: string): Promise<DocumentTypeResponse> {
  return apiRequest<DocumentTypeResponse>(`/document-types/${id}`);
}

export function createDocumentType(
  request: DocumentTypeCreateRequest,
): Promise<DocumentTypeResponse> {
  return apiRequest<DocumentTypeResponse>("/document-types", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateDocumentType(
  id: string,
  request: DocumentTypeUpdateRequest,
): Promise<DocumentTypeResponse> {
  return apiRequest<DocumentTypeResponse>(`/document-types/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

export function getDocumentTemplates(
  parameters: DocumentTemplateListParameters = {},
): Promise<PagedResponse<DocumentTemplateResponse>> {
  return apiRequest<PagedResponse<DocumentTemplateResponse>>(
    `/document-templates${buildQuery(parameters)}`,
  );
}

export function getDocumentTemplate(
  id: string,
): Promise<DocumentTemplateResponse> {
  return apiRequest<DocumentTemplateResponse>(`/document-templates/${id}`);
}

export async function getAllDocumentTemplates(
  activeOnly?: boolean,
): Promise<DocumentTemplateResponse[]> {
  const items: DocumentTemplateResponse[] = [];
  for (let page = 1; ; page += 1) {
    const response = await getDocumentTemplates({ activeOnly, page, pageSize: 100 });
    items.push(...response.items);
    if (page >= response.totalPages) break;
  }
  return items;
}

export function createDocumentTemplate(
  request: DocumentTemplateCreateRequest,
): Promise<DocumentTemplateResponse> {
  return apiRequest<DocumentTemplateResponse>("/document-templates", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateDocumentTemplate(
  id: string,
  request: DocumentTemplateUpdateRequest,
): Promise<DocumentTemplateResponse> {
  return apiRequest<DocumentTemplateResponse>(`/document-templates/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
  });
}

function buildQuery(parameters: {
  documentTypeId?: string;
  activeOnly?: boolean;
  page?: number;
  pageSize?: number;
}): string {
  const query = new URLSearchParams();

  if (parameters.documentTypeId !== undefined) {
    query.set("documentTypeId", parameters.documentTypeId);
  }
  if (parameters.activeOnly === true) {
    query.set("activeOnly", "true");
  }
  if (parameters.page !== undefined) {
    query.set("page", String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set("pageSize", String(parameters.pageSize));
  }

  return query.size > 0 ? `?${query.toString()}` : "";
}
