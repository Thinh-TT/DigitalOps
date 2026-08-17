import { apiRequest } from "../api/api-client";
import type { PagedResponse } from "../api/types";
import type { DocumentSearchQuery, DocumentSearchResult } from "./types";

export function searchDocuments(
  query: DocumentSearchQuery,
): Promise<PagedResponse<DocumentSearchResult>> {
  const searchParams = new URLSearchParams();

  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== "") {
      searchParams.set(name, String(value));
    }
  }

  const queryString = searchParams.size > 0 ? `?${searchParams.toString()}` : "";
  return apiRequest<PagedResponse<DocumentSearchResult>>(`/documents/search${queryString}`);
}
