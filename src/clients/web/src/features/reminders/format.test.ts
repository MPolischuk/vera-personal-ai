import { describe, expect, it } from "vitest";
import {
  actionLabel,
  channelLabel,
  deliveryStatusLabel,
  groupByLocalDay,
  localClockLabel,
  localDayHeading,
  recurrenceLabel,
} from "./format";

describe("deliveryStatusLabel", () => {
  it("names the four final states", () => {
    expect(deliveryStatusLabel("delivered", "telegram")).toBe("Entregado por Telegram");
    expect(deliveryStatusLabel("acted", "telegram")).toBe("Entregado por Telegram");
    expect(deliveryStatusLabel("delivered", "webpush")).toBe("Push aceptado");
    expect(deliveryStatusLabel("delivered", "hub")).toBe("Visto en la app");
    expect(deliveryStatusLabel("failed", null)).toBe("No llegó por ningún canal");
  });

  it("keeps an in-flight notice out of the failure phrase", () => {
    expect(deliveryStatusLabel("pending", null)).toBe("Todavía en camino");
    expect(deliveryStatusLabel("dispatching", "hub")).toBe("Todavía en camino");
  });
});

describe("action and recurrence labels", () => {
  it("translates the reminder buttons and a single shot", () => {
    expect(actionLabel("done")).toBe("Listo");
    expect(actionLabel("snooze:10m")).toBe("+10 min");
    expect(actionLabel(null)).toBe("Sin acción");
    expect(recurrenceLabel(null)).toBe("Una sola vez");
    expect(recurrenceLabel("todos los jueves a las 21")).toBe("todos los jueves a las 21");
    expect(channelLabel("telegram")).toBe("Telegram");
    expect(channelLabel("hub")).toBe("La app");
  });
});

describe("groupByLocalDay", () => {
  it("groups by the date in the offset string, not by UTC", () => {
    expect(localDayHeading("2026-10-08T21:00:00-03:00")).toBe("jueves 8/10");
    expect(localClockLabel("2026-10-08T21:00:00-03:00")).toBe("21:00");

    const groups = groupByLocalDay([
      { localAt: "2026-10-08T21:00:00-03:00", id: "a" },
      { localAt: "2026-10-08T22:15:00-03:00", id: "b" },
      { localAt: "2026-10-09T00:30:00-03:00", id: "c" },
    ]);

    expect(groups.map((group) => group.heading)).toEqual(["jueves 8/10", "viernes 9/10"]);
    expect(groups[0]?.items.map((item) => item.id)).toEqual(["a", "b"]);
    expect(groups[1]?.items.map((item) => item.id)).toEqual(["c"]);
  });
});
