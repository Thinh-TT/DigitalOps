import { apiRequest } from "../api/api-client";
import type { CurrentUserResponse } from "./types";

export function getCurrentUser(): Promise<CurrentUserResponse> {
  return apiRequest<CurrentUserResponse>("/auth/me");
}
