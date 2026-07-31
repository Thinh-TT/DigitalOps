import {
  authSessionStorageKey,
  clearSession,
  readSession,
  writeSession,
} from "./session-store";

describe("AuthSessionStore", () => {
  const now = Date.parse("2026-07-31T08:00:00.000Z");

  it("stores and reads a valid session", () => {
    writeSession(
      {
        accessToken: "signed-token",
        expiresAt: "2026-07-31T16:00:00.000Z",
      },
      now,
    );

    expect(readSession(now)).toEqual({
      accessToken: "signed-token",
      expiresAt: "2026-07-31T16:00:00.000Z",
    });
  });

  it("removes an expired session", () => {
    window.localStorage.setItem(
      authSessionStorageKey,
      JSON.stringify({
        accessToken: "expired-token",
        expiresAt: "2026-07-31T07:59:59.000Z",
      }),
    );

    expect(readSession(now)).toBeNull();
    expect(window.localStorage.getItem(authSessionStorageKey)).toBeNull();
  });

  it("removes malformed storage data", () => {
    window.localStorage.setItem(authSessionStorageKey, "{invalid");

    expect(readSession(now)).toBeNull();
    expect(window.localStorage.getItem(authSessionStorageKey)).toBeNull();
  });

  it("rejects an invalid session and supports explicit logout", () => {
    expect(() =>
      writeSession(
        {
          accessToken: "",
          expiresAt: "2026-07-31T16:00:00.000Z",
        },
        now,
      ),
    ).toThrow("Phiên đăng nhập không hợp lệ");

    window.localStorage.setItem(authSessionStorageKey, "value");
    clearSession();

    expect(window.localStorage.getItem(authSessionStorageKey)).toBeNull();
  });
});
