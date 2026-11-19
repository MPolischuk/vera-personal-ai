import { describe, expect, it } from "vitest";
import { reminderActionButtons } from "./useNotifications";

describe("reminderActionButtons", () => {
  it("keeps Listo and +10 min", () => {
    expect(
      reminderActionButtons([
        { actionId: "done", label: "Listo" },
        { actionId: "snooze:10m", label: "+10 min" },
        { actionId: "opened", label: "Abrir" },
      ]).map((action) => action.actionId),
    ).toEqual(["done", "snooze:10m"]);
  });

  it("is empty when there are no actions", () => {
    expect(reminderActionButtons(undefined)).toEqual([]);
    expect(reminderActionButtons([])).toEqual([]);
  });
});
