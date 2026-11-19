import { describe, expect, it } from "vitest";
import { blinkWeightAt, breathOffsetAt } from "./idleMotion";

describe("idleMotion pure helpers", () => {
  it("blink weight rises then falls within [0, 1]", () => {
    expect(blinkWeightAt(0)).toBe(0);
    expect(blinkWeightAt(0.25)).toBeCloseTo(0.5);
    expect(blinkWeightAt(0.5)).toBe(1);
    expect(blinkWeightAt(0.75)).toBeCloseTo(0.5);
    expect(blinkWeightAt(1)).toBe(0);
  });

  it("breath offset is a bounded sine", () => {
    const a = breathOffsetAt(0);
    const b = breathOffsetAt(4_200 / 4);
    expect(a).toBeCloseTo(0);
    expect(Math.abs(b)).toBeGreaterThan(0.01);
    expect(Math.abs(b)).toBeLessThanOrEqual(0.021);
  });
});
