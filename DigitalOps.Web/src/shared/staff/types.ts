import type { Role } from "../auth/types";

export interface StaffResponse {
  id: string;
  identityUserId: string;
  userName: string;
  fullName: string;
  position: string | null;
  department: string | null;
  email: string;
  phone: string | null;
  isActive: boolean;
  roles: Role[];
  createdAt: string;
  updatedAt: string;
}

export interface StaffListParameters {
  activeOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface StaffCreateRequest {
  userName: string;
  email: string;
  temporaryPassword: string;
  fullName: string;
  position: string | null;
  department: string | null;
  phone: string | null;
  roles: Role[];
}

export interface StaffUpdateRequest {
  fullName?: string;
  position?: string | null;
  department?: string | null;
  email?: string;
  phone?: string | null;
  isActive?: boolean;
}

export interface RoleAssignmentRequest {
  roles: Role[];
}

export interface ResetPasswordRequest {
  temporaryPassword: string;
}
