export type MemberStatus = "Active" | "Inactive";

export type MemberGender = "Male" | "Female" | "Other";

export interface MemberResponse {
  id: string;
  fullName: string;
  dateOfBirth: string | null;
  gender: MemberGender | null;
  address: string | null;
  phone: string | null;
  email: string | null;
  position: string | null;
  joinDate: string | null;
  status: MemberStatus;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface MemberLookupResponse {
  id: string;
  fullName: string;
  position: string | null;
}

export interface MemberListParameters {
  q?: string;
  status?: MemberStatus;
  page?: number;
  pageSize?: number;
}

export interface MemberLookupParameters {
  q?: string;
  page?: number;
  pageSize?: number;
}

export interface MemberCreateRequest {
  fullName: string;
  dateOfBirth: string | null;
  gender: MemberGender | null;
  address: string | null;
  phone: string | null;
  email: string | null;
  position: string | null;
  joinDate: string | null;
  notes: string | null;
}

export interface MemberUpdateRequest {
  fullName?: string;
  dateOfBirth?: string | null;
  gender?: MemberGender | null;
  address?: string | null;
  phone?: string | null;
  email?: string | null;
  position?: string | null;
  joinDate?: string | null;
  status?: "Active";
  notes?: string | null;
}
