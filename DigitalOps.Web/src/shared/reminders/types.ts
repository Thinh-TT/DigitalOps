export type ReminderKind = "BeforeDeadline" | "DueDate" | "Overdue";

export type ReminderDeliveryStatus = "Unread" | "Read";

export interface ReminderResponse {
  id: string;
  incomingDocumentId: string;
  referenceNumber: string;
  summary: string;
  reminderKind: ReminderKind;
  reminderDate: string;
  deliveryStatus: ReminderDeliveryStatus;
  createdAt: string;
  readAt: string | null;
}

export interface ReminderListParameters {
  deliveryStatus?: ReminderDeliveryStatus;
  recipientStaffId?: string;
  page?: number;
  pageSize?: number;
}
