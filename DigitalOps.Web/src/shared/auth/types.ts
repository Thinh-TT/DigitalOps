export const systemRoles = [
  "Administrator",
  "Clerk",
  "Drafter",
  "Leader",
] as const;

export type Role = (typeof systemRoles)[number];

export interface StaffReference {
  id: string;
  fullName: string;
  position: string | null;
  department: string | null;
}

export interface CurrentUserResponse {
  staff: StaffReference;
  roles: Role[];
  mustChangePassword: boolean;
}

export interface LoginResponse extends CurrentUserResponse {
  accessToken: string;
  expiresAt: string;
}

export interface LoginRequest {
  userNameOrEmail: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
