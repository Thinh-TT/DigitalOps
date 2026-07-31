export interface DocumentTypeReference {
  id: string;
  code: string;
  name: string;
}

export interface DocumentTypeResponse extends DocumentTypeReference {
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface FormatRule {
  code: string;
  required: boolean;
  [extension: string]: unknown;
}

export interface FormatRules {
  version: number;
  rules: FormatRule[];
  [extension: string]: unknown;
}

export interface DocumentTemplateResponse {
  id: string;
  documentType: DocumentTypeReference;
  name: string;
  templateContent: string;
  formatRules: FormatRules;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DocumentTypeListParameters {
  activeOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface DocumentTemplateListParameters
  extends DocumentTypeListParameters {
  documentTypeId?: string;
}

export interface DocumentTypeCreateRequest {
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
}

export interface DocumentTypeUpdateRequest {
  code?: string;
  name?: string;
  description?: string | null;
  isActive?: boolean;
}

export interface DocumentTemplateCreateRequest {
  documentTypeId: string;
  name: string;
  templateContent: string;
  formatRules: FormatRules;
  isActive: boolean;
}

export interface DocumentTemplateUpdateRequest {
  documentTypeId?: string;
  name?: string;
  templateContent?: string;
  formatRules?: FormatRules;
  isActive?: boolean;
}
