import type { AttachmentResponse, IncomingStaffReference } from "../incoming-documents/types";
import type { DocumentTypeReference, DocumentTemplateResponse } from "../document-catalog/types";

export type OutgoingDocumentStatus =
  | "Editing" | "AiDraft" | "PendingReview" | "ReviewFailed"
  | "PendingApproval" | "Approved" | "Archived";

export interface OutgoingTemplateReference { id: string; name: string; documentType: DocumentTypeReference; }
export interface OutgoingIncomingReference { id: string; referenceNumber: string; summary: string; }
export interface OutgoingMemberReference { id: string; fullName: string; position: string | null; }
export type OutgoingStaffReference = IncomingStaffReference;
export interface ReviewIssueResponse { ruleCode: string; severity: string; message: string; location: string | null; }
export type ReviewSource = "Rule" | "AI" | "Hybrid";
export type ReviewResult = "Failed" | "Passed";
export interface ReviewResponse {
  id: string;
  outgoingDocumentId: string;
  attemptNo: number;
  reviewSource: ReviewSource;
  reviewedByStaff: OutgoingStaffReference | null;
  contentSnapshot: string;
  reviewResult: ReviewResult;
  reviewIssues: ReviewIssueResponse[];
  reviewedAt: string;
  documentStatus: OutgoingDocumentStatus;
}

export interface OutgoingDocumentResponse {
  id: string;
  template: OutgoingTemplateReference;
  relatedIncomingDocument: OutgoingIncomingReference | null;
  relatedMember: OutgoingMemberReference | null;
  title: string;
  content: string;
  aiDraftContent: string | null;
  draftedByStaff: OutgoingStaffReference;
  status: OutgoingDocumentStatus;
  reviewIssues: ReviewIssueResponse[];
  approvedByStaff: OutgoingStaffReference | null;
  approvedAt: string | null;
  referenceNumber: string | null;
  issuedDate: string | null;
  archivedAt: string | null;
  attachments: AttachmentResponse[];
  createdAt: string;
  updatedAt: string;
}

export interface OutgoingDocumentCreateRequest {
  templateId: string;
  title: string;
  relatedIncomingDocumentId?: string;
  relatedMemberId?: string;
}

export interface OutgoingDocumentUpdateRequest {
  title?: string;
  content?: string;
}

export interface AiDraftRequest {
  instruction?: string;
}

export interface ReviewListParameters {
  page?: number;
  pageSize?: number;
}

export interface OutgoingDocumentListParameters {
  q?: string;
  templateId?: string;
  relatedIncomingDocumentId?: string;
  relatedMemberId?: string;
  status?: OutgoingDocumentStatus;
  draftedByStaffId?: string;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export type OutgoingTemplateOption = DocumentTemplateResponse;
