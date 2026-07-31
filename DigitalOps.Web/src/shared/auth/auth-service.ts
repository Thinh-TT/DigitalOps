import { apiRequest } from "../api/api-client";
import type {
  ChangePasswordRequest,
  CurrentUserResponse,
  LoginRequest,
  LoginResponse,
} from "./types";

export function login(request: LoginRequest): Promise<LoginResponse> {
  return apiRequest<LoginResponse>("/auth/login", {
    method: "POST",
    authenticated: false,
    body: JSON.stringify(request),
  });
}

export function getCurrentUser(): Promise<CurrentUserResponse> {
  return apiRequest<CurrentUserResponse>("/auth/me");
}

export function changePassword(
  request: ChangePasswordRequest,
): Promise<LoginResponse> {
  return apiRequest<LoginResponse>("/auth/change-password", {
    method: "POST",
    body: JSON.stringify(request),
  });
}
