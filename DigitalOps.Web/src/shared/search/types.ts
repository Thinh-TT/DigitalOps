import type { IncomingDocumentStatus } from "../incoming-documents/types";
import type { OutgoingDocumentStatus } from "../outgoing-documents/types";

export type DocumentKind = "Incoming" | "Outgoing";

export type DocumentSearchMatchSource =
  | "Summary"
  | "Title"
  | "Content"
  | "AiDraftContent"
  | "Attachment";

export interface DocumentSearchQuery {
  q: string;
  documentKind?: DocumentKind;
  documentTypeId?: string;
  incomingStatus?: IncomingDocumentStatus;
  outgoingStatus?: OutgoingDocumentStatus;
  dateFrom?: string;
  dateTo?: string;
  matchSource?: DocumentSearchMatchSource;
  page?: number;
  pageSize?: number;
}

export interface DocumentSearchResult {
  documentKind: DocumentKind;
  documentId: string;
  referenceNumber: string;
  title: string;
  documentType: string;
  documentDate: string;
  matchSource: DocumentSearchMatchSource;
  snippet: string;
  score: number;
}
