import type { DocumentTypeReference } from "../document-catalog/types";

export type IncomingDocumentStatus =
  | "New"
  | "InProgress"
  | "Completed"
  | "Overdue";

export type AttachmentExtractionStatus =
  | "Pending"
  | "Processing"
  | "Succeeded"
  | "Failed"
  | "Unsupported";

export interface IncomingStaffReference {
  id: string;
  fullName: string;
  position: string | null;
  department: string | null;
}

export interface IncomingAttachmentResponse {
  id: string;
  fileName: string;
  uploadedBy: IncomingStaffReference;
  uploadedAt: string;
  extractionStatus: AttachmentExtractionStatus;
  extractedAt: string | null;
}

export interface IncomingDocumentResponse {
  id: string;
  referenceNumber: string;
  senderOrg: string;
  summary: string;
  receivedDate: string;
  deadline: string;
  documentType: DocumentTypeReference;
  suggestedStaff: IncomingStaffReference | null;
  assignmentSuggestionReason: string | null;
  assignmentConfidence: number | null;
  assignmentSuggestedAt: string | null;
  assignedToStaff: IncomingStaffReference | null;
  assignmentConfirmedBy: IncomingStaffReference | null;
  assignmentConfirmedAt: string | null;
  status: IncomingDocumentStatus;
  completedAt: string | null;
  attachments: IncomingAttachmentResponse[];
  createdAt: string;
  updatedAt: string;
}

export interface IncomingDocumentCreateRequest {
  referenceNumber: string;
  senderOrg: string;
  summary: string;
  receivedDate: string;
  deadline: string;
  documentTypeId: string;
}

export interface IncomingDocumentUpdateRequest {
  referenceNumber?: string;
  senderOrg?: string;
  summary?: string;
  receivedDate?: string;
  deadline?: string;
  documentTypeId?: string;
}

export interface IncomingDocumentListParameters {
  q?: string;
  documentTypeId?: string;
  status?: IncomingDocumentStatus;
  assignedToStaffId?: string;
  deadlineFrom?: string;
  deadlineTo?: string;
  page?: number;
  pageSize?: number;
}
