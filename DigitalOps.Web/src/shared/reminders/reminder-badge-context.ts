import { createContext, useContext } from "react";

export interface ReminderBadgeContextValue {
  unreadCount: number | null;
  refreshUnreadCount: () => Promise<void>;
}

export const ReminderBadgeContext =
  createContext<ReminderBadgeContextValue | null>(null);

export function useReminderBadge(): ReminderBadgeContextValue {
  const value = useContext(ReminderBadgeContext);

  if (value === null) {
    throw new Error("useReminderBadge must be used inside AppShell.");
  }

  return value;
}
