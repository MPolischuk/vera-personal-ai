import { describe, expect, it } from "vitest";
import { CATALOG_SCALE, HOLD_POSE } from "./gestureCatalog";

describe("hold (presenting hand) pose", () => {
  it("is reachable without shrinking toward rest", () => {
    expect(CATALOG_SCALE.hold).toBeGreaterThan(0.9);
  });

  it("raises the forearm forward with the palm up", () => {
    expect(HOLD_POSE.f.z).toBeGreaterThan(0.7);
    expect(HOLD_POSE.f.y).toBeGreaterThan(0);
    // ArmDef.palm is dorsal (+Y of the hand): y:-1 ⇒ true palm up.
    const n = Math.hypot(HOLD_POSE.palm.x, HOLD_POSE.palm.y, HOLD_POSE.palm.z);
    expect(HOLD_POSE.palm.y / n).toBeLessThan(-0.9);
  });

  it("keeps fingers softly cupped and the thumb relaxed, not splayed flat", () => {
    for (const c of HOLD_POSE.curl) {
      expect(c).toBeGreaterThan(0.15);
      expect(c).toBeLessThan(0.45);
    }
    expect(HOLD_POSE.spread).toBeLessThan(0.5);
    expect(HOLD_POSE.thumb).toBeGreaterThan(0.15);
  });
});
