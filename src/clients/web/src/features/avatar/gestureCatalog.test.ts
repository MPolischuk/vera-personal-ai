import { describe, expect, it } from "vitest";
import { REST, limitDef, mixDef, violation } from "./arms";
import {
  CATALOG_SCALE,
  EXPLAIN_POSE,
  HANDS_POSE,
  HOLD_POSE,
  THINK_HEAD,
  THINK_POSE,
  pointPose,
} from "./gestureCatalog";

const POSES = [EXPLAIN_POSE, THINK_POSE, HOLD_POSE, HANDS_POSE];

describe("gesture catalog", () => {
  it("keeps every peak pose inside the arm limit", () => {
    for (const pose of POSES) {
      expect(violation(pose)).toBe(0);
      expect(violation(limitDef(pose))).toBe(0);
    }
  });

  it("keeps ten explain blends over rest inside the arm limit", () => {
    for (let step = 0; step <= 10; step += 1) {
      const mixed = mixDef(REST, EXPLAIN_POSE, step / 10);
      expect(violation(mixed)).toBe(0);
      expect(violation(limitDef(mixed))).toBe(0);
    }
  });

  it("keeps a usable scale on every pose", () => {
    expect(CATALOG_SCALE.explain).toBeGreaterThan(0);
    expect(CATALOG_SCALE.think).toBeGreaterThan(0);
    expect(CATALOG_SCALE.hold).toBeGreaterThan(0);
    expect(CATALOG_SCALE.hands).toBeGreaterThan(0);
  });

  it("aims point at a target without leaving the limit", () => {
    const aimed = pointPose({ x: 0.2, y: 0.05, z: 1 });
    expect(aimed.scale).toBeGreaterThan(0);
    expect(violation(aimed.pose)).toBe(0);
    expect(violation(limitDef(aimed.pose))).toBe(0);
    expect(aimed.pose.curl[0]).toBeLessThan(aimed.pose.curl[1]);
  });

  it("keeps think head at the catalog amplitudes", () => {
    expect(THINK_HEAD).toEqual({ pitch: 0.1, yaw: 0, roll: 0.1 });
  });
});
