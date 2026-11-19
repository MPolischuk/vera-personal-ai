import { describe, expect, it } from "vitest";
import { shouldAckNotification } from "./notificationAck";

describe("shouldAckNotification", () => {
  it("acks only while the tab is visible", () => {
    expect(shouldAckNotification("visible")).toBe(true);
    expect(shouldAckNotification("hidden")).toBe(false);
    expect(shouldAckNotification("prerender")).toBe(false);
    expect(shouldAckNotification("")).toBe(false);
  });
});
