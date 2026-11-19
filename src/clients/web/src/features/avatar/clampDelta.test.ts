import { describe, expect, it } from "vitest";
import { clampDelta } from "./clampDelta";

describe("clampDelta", () => {
  it("caps a one-second gap at 1/30 s", () => {
    expect(clampDelta(1)).toBe(1 / 30);
  });

  it("keeps a normal frame and drops a non-positive step", () => {
    expect(clampDelta(1 / 60)).toBe(1 / 60);
    expect(clampDelta(1 / 30)).toBe(1 / 30);
    expect(clampDelta(0)).toBe(0);
    expect(clampDelta(-0.5)).toBe(0);
    expect(clampDelta(Number.NaN)).toBe(0);
    expect(clampDelta(Number.POSITIVE_INFINITY)).toBe(0);
  });
});
