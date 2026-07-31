export interface AuthSession {
  accessToken: string;
  expiresAt: string;
}

const storageKey = "digitalops.auth.session";

function normalizeSession(value: unknown, now: number): AuthSession | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }

  const session = value as Partial<AuthSession>;
  const expiresAt = Date.parse(session.expiresAt ?? "");

  if (
    typeof session.accessToken !== "string" ||
    session.accessToken.trim().length === 0 ||
    !Number.isFinite(expiresAt) ||
    expiresAt <= now
  ) {
    return null;
  }

  return {
    accessToken: session.accessToken,
    expiresAt: new Date(expiresAt).toISOString(),
  };
}

export function readSession(now = Date.now()): AuthSession | null {
  const serialized = window.localStorage.getItem(storageKey);

  if (serialized === null) {
    return null;
  }

  try {
    const session = normalizeSession(JSON.parse(serialized), now);

    if (session === null) {
      clearSession();
    }

    return session;
  } catch {
    clearSession();
    return null;
  }
}

export function writeSession(session: AuthSession, now = Date.now()): void {
  const normalized = normalizeSession(session, now);

  if (normalized === null) {
    throw new Error("Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
  }

  window.localStorage.setItem(storageKey, JSON.stringify(normalized));
}

export function clearSession(): void {
  window.localStorage.removeItem(storageKey);
}

export function subscribeToSessionChanges(
  listener: () => void,
): () => void {
  const handleStorage = (event: StorageEvent) => {
    if (event.key === storageKey) {
      listener();
    }
  };

  window.addEventListener("storage", handleStorage);
  return () => window.removeEventListener("storage", handleStorage);
}

export const authSessionStorageKey = storageKey;
