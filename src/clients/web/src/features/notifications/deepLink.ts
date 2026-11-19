export interface NotificationDeepLink {
  notificationId: string | null;
  conversationId: string | null;
  action: string | null;
}

export function parseNotificationDeepLink(search: string): NotificationDeepLink | null {
  const query = search.startsWith("?") ? search.slice(1) : search;
  const params = new URLSearchParams(query);
  return linkFrom(
    params.get("notification"),
    params.get("conversation"),
    params.get("action"),
  );
}

export function parseServiceWorkerNotification(data: unknown): NotificationDeepLink | null {
  if (!data || typeof data !== "object") {
    return null;
  }

  const record = data as Record<string, unknown>;
  if (record.type !== "vera.notification") {
    return null;
  }

  if (typeof record.navigate === "string" && record.navigate.length > 0) {
    const fromUrl = parseNotificationDeepLink(searchOf(record.navigate));
    if (fromUrl) {
      return fromUrl;
    }
  }

  return linkFrom(
    typeof record.notificationId === "string" ? record.notificationId : null,
    typeof record.conversationId === "string" ? record.conversationId : null,
    typeof record.action === "string" ? record.action : null,
  );
}

function linkFrom(
  notificationId: string | null,
  conversationId: string | null,
  action: string | null,
): NotificationDeepLink | null {
  const notification = blankToNull(notificationId);
  const conversation = blankToNull(conversationId);
  const chosen = blankToNull(action);
  if (!notification && !conversation && !chosen) {
    return null;
  }

  return {
    notificationId: notification,
    conversationId: conversation,
    action: chosen,
  };
}

function blankToNull(value: string | null): string | null {
  if (!value || value.trim().length === 0) {
    return null;
  }

  return value;
}

function searchOf(navigate: string): string {
  try {
    return new URL(navigate, "https://push.invalid").search;
  } catch {
    return "";
  }
}
