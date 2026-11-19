import { describe, expect, it } from "vitest";
import { parseNotificationDeepLink, parseServiceWorkerNotification } from "./deepLink";

describe("parseNotificationDeepLink", () => {
  it("reads the notification, conversation and action", () => {
    expect(
      parseNotificationDeepLink("?notification=8f1c&conversation=2b9e&action=done"),
    ).toEqual({
      notificationId: "8f1c",
      conversationId: "2b9e",
      action: "done",
    });
  });

  it("keeps a click without an action", () => {
    expect(parseNotificationDeepLink("?notification=8f1c&conversation=2b9e")).toEqual({
      notificationId: "8f1c",
      conversationId: "2b9e",
      action: null,
    });
  });

  it("returns null when the query has no push fields", () => {
    expect(parseNotificationDeepLink("")).toBeNull();
    expect(parseNotificationDeepLink("?view=settings")).toBeNull();
  });
});

describe("parseServiceWorkerNotification", () => {
  it("reads the forwarded click", () => {
    expect(
      parseServiceWorkerNotification({
        type: "vera.notification",
        navigate: "https://assistant.example/?notification=8f1c&conversation=2b9e&action=snooze:10m",
        notificationId: "8f1c",
        action: "snooze:10m",
        conversationId: "2b9e",
      }),
    ).toEqual({
      notificationId: "8f1c",
      conversationId: "2b9e",
      action: "snooze:10m",
    });
  });

  it("ignores other messages", () => {
    expect(parseServiceWorkerNotification(null)).toBeNull();
    expect(parseServiceWorkerNotification({ type: "other" })).toBeNull();
  });
});
