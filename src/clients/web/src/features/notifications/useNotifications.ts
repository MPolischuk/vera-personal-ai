import { useSyncExternalStore } from "react";
import type { NotificationActionPayload, NotificationPayload } from "../../api/protocol";

export interface VisibleNotification {
  notificationId: string;
  deliveryId: string;
  title: string;
  body: string;
  actions: NotificationActionPayload[];
}

/** Buttons the toast can run. Other actions stay on the payload for the channel that owns them. */
export function reminderActionButtons(
  actions: NotificationActionPayload[] | undefined,
): NotificationActionPayload[] {
  if (!actions) {
    return [];
  }

  return actions.filter(
    (action) => action.actionId === "done" || action.actionId === "snooze:10m",
  );
}

const listeners = new Set<() => void>();
let items: VisibleNotification[] = [];

function emit(): void {
  for (const listener of listeners) {
    listener();
  }
}

export function pushNotification(payload: NotificationPayload): void {
  const next: VisibleNotification = {
    notificationId: payload.notificationId,
    deliveryId: payload.deliveryId,
    title: payload.title,
    body: payload.body,
    actions: payload.actions ?? [],
  };
  items = [
    next,
    ...items.filter((item) => item.notificationId !== next.notificationId),
  ].slice(0, 5);
  emit();
}

export function dismissNotification(notificationId: string): void {
  items = items.filter((item) => item.notificationId !== notificationId);
  emit();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getSnapshot(): VisibleNotification[] {
  return items;
}

export function useNotifications(): VisibleNotification[] {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
