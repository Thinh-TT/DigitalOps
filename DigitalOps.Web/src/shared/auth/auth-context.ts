import { createContext, useContext } from "react";
import type { CurrentUserResponse, LoginResponse } from "./types";

export type AuthStatus =
  | "initializing"
  | "anonymous"
  | "authenticated"
  | "forbidden"
  | "error";

export interface AuthContextValue {
  status: AuthStatus;
  currentUser: CurrentUserResponse | null;
  errorMessage: string | null;
  establishSession: (login: LoginResponse) => Promise<void>;
  refreshCurrentUser: () => Promise<void>;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);

  if (value === null) {
    throw new Error("useAuth must be used inside AuthProvider.");
  }

  return value;
}
