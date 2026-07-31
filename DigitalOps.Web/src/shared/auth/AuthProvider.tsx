import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from "react";
import {
  ApiError,
  subscribeToAccessEvents,
} from "../api/api-client";
import { AuthContext, type AuthContextValue } from "./auth-context";
import { getCurrentUser } from "./auth-service";
import {
  clearSession,
  readSession,
  subscribeToSessionChanges,
  writeSession,
} from "./session-store";
import type { CurrentUserResponse, LoginResponse } from "./types";

interface AuthState {
  status: AuthContextValue["status"];
  currentUser: CurrentUserResponse | null;
  errorMessage: string | null;
}

const initialState: AuthState = {
  status: "initializing",
  currentUser: null,
  errorMessage: null,
};

export function AuthProvider({ children }: PropsWithChildren) {
  const [state, setState] = useState<AuthState>(initialState);

  const refreshCurrentUser = useCallback(async () => {
    if (readSession() === null) {
      setState({
        status: "anonymous",
        currentUser: null,
        errorMessage: null,
      });
      return;
    }

    setState((current) => ({
      status: "initializing",
      currentUser: current.currentUser,
      errorMessage: null,
    }));

    try {
      const currentUser = await getCurrentUser();
      setState({
        status: "authenticated",
        currentUser,
        errorMessage: null,
      });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        clearSession();
        setState({
          status: "anonymous",
          currentUser: null,
          errorMessage: null,
        });
        return;
      }

      if (error instanceof ApiError && error.status === 403) {
        setState((current) => ({
          status: "forbidden",
          currentUser: current.currentUser,
          errorMessage: null,
        }));
        return;
      }

      setState((current) => ({
        status: "error",
        currentUser: current.currentUser,
        errorMessage:
          error instanceof Error
            ? error.message
            : "Không thể tải thông tin phiên làm việc.",
      }));
    }
  }, []);

  const establishSession = useCallback(
    async (login: LoginResponse) => {
      writeSession({
        accessToken: login.accessToken,
        expiresAt: login.expiresAt,
      });
      await refreshCurrentUser();
    },
    [refreshCurrentUser],
  );

  const logout = useCallback(() => {
    clearSession();
    setState({
      status: "anonymous",
      currentUser: null,
      errorMessage: null,
    });
  }, []);

  useEffect(() => {
    void refreshCurrentUser();
    return subscribeToSessionChanges(() => {
      void refreshCurrentUser();
    });
  }, [refreshCurrentUser]);

  useEffect(
    () =>
      subscribeToAccessEvents((event) => {
        if (event === "unauthorized") {
          setState({
            status: "anonymous",
            currentUser: null,
            errorMessage: null,
          });
          return;
        }

        if (event === "password-change-required") {
          setState((current) =>
            current.currentUser === null
              ? {
                  status: "forbidden",
                  currentUser: null,
                  errorMessage: null,
                }
              : {
                  status: "authenticated",
                  currentUser: {
                    ...current.currentUser,
                    mustChangePassword: true,
                  },
                  errorMessage: null,
                },
          );
          return;
        }

        setState((current) => ({
          status: "forbidden",
          currentUser: current.currentUser,
          errorMessage: null,
        }));
      }),
    [],
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      ...state,
      establishSession,
      refreshCurrentUser,
      logout,
    }),
    [establishSession, logout, refreshCurrentUser, state],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
