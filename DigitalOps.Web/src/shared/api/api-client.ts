import {
  clearSession,
  readSession,
} from "../auth/session-store";
import type { ProblemDetails } from "./types";

export type AccessEvent =
  | "unauthorized"
  | "forbidden"
  | "password-change-required";

type AccessEventListener = (event: AccessEvent) => void;

export interface ApiRequestOptions extends RequestInit {
  authenticated?: boolean;
}

export interface DownloadedFile {
  blob: Blob;
  fileName: string | null;
}

const accessEventListeners = new Set<AccessEventListener>();
const apiBaseUrl = (
  import.meta.env.VITE_API_BASE_URL ?? "/api/v1"
).replace(/\/+$/, "");

export class ApiError extends Error {
  public constructor(
    public readonly status: number,
    public readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? `API request failed (${status}).`);
    this.name = "ApiError";
  }
}

export function subscribeToAccessEvents(
  listener: AccessEventListener,
): () => void {
  accessEventListeners.add(listener);
  return () => accessEventListeners.delete(listener);
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const response = await sendRequest(path, options, "application/json");

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export async function apiDownload(
  path: string,
  options: ApiRequestOptions = {},
  accept = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
): Promise<DownloadedFile> {
  const response = await sendRequest(
    path,
    options,
    accept,
  );

  return {
    blob: await response.blob(),
    fileName: readDownloadFileName(response.headers.get("Content-Disposition")),
  };
}

async function sendRequest(
  path: string,
  options: ApiRequestOptions,
  accept: string,
): Promise<Response> {
  const { authenticated = true, ...requestOptions } = options;
  const headers = new Headers(requestOptions.headers);

  headers.set("Accept", accept);

  if (
    requestOptions.body !== undefined &&
    !(requestOptions.body instanceof FormData) &&
    !headers.has("Content-Type")
  ) {
    headers.set("Content-Type", "application/json");
  }

  if (authenticated) {
    const session = readSession();

    if (session !== null) {
      headers.set("Authorization", `Bearer ${session.accessToken}`);
    }
  }

  const response = await fetch(buildApiUrl(path), {
    ...requestOptions,
    headers,
  });

  if (!response.ok) {
    const problem = await readProblemDetails(response);

    if (response.status === 401) {
      clearSession();
      publishAccessEvent("unauthorized");
    } else if (response.status === 403) {
      publishAccessEvent(
        isPasswordChangeRequired(problem.type)
          ? "password-change-required"
          : "forbidden",
      );
    }

    throw new ApiError(response.status, problem);
  }

  return response;
}

function buildApiUrl(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${apiBaseUrl}${normalizedPath}`;
}

async function readProblemDetails(response: Response): Promise<ProblemDetails> {
  const contentType = response.headers.get("Content-Type") ?? "";

  if (contentType.includes("json")) {
    try {
      return (await response.json()) as ProblemDetails;
    } catch {
      // Fall back to the HTTP status when the server returned invalid JSON.
    }
  }

  return {
    title: response.statusText || "Yêu cầu không thành công",
    status: response.status,
  };
}

function isPasswordChangeRequired(type: string | undefined): boolean {
  return type?.toLowerCase().endsWith("password-change-required") ?? false;
}

function publishAccessEvent(event: AccessEvent): void {
  for (const listener of accessEventListeners) {
    listener(event);
  }
}

function readDownloadFileName(contentDisposition: string | null): string | null {
  if (contentDisposition === null) {
    return null;
  }

  const encodedMatch = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (encodedMatch?.[1] !== undefined) {
    try {
      return decodeURIComponent(encodedMatch[1].trim());
    } catch {
      return encodedMatch[1].trim();
    }
  }

  const match = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return match?.[1]?.trim() ?? null;
}
