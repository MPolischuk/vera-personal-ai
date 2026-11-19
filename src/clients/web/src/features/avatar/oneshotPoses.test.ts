import { describe, expect, it } from "vitest";
import { armPoints, chooseSide, DOMINANT_SIDE, violation } from "./arms";
import { CATALOG_SCALE, DEFAULT_POINT_DIR, ONESHOT_SCALE, SHRUG_POSE, THINK_POSE, WAVE_POSE, pointPose } from "./gestureCatalog";
import { armBlendWeight, createGestureQueue, enqueue, sampleArm, tick, type GestureRequest } from "./gestureQueue";

const oneshot = (name: string, hands: "R" | "both"): GestureRequest => ({
  name, priority: 2, channel: "arm", hands, oneshot: true, weight: 1, durationMs: 1100, intensity: 0.7, pose: null, head: null,
});

describe("one-shot arm poses", () => {
  it("wave and shrug have legal full-scale poses", () => {
    expect(ONESHOT_SCALE.wave).toBe(1);
    expect(ONESHOT_SCALE.shrug).toBe(1);
    expect(violation(WAVE_POSE)).toBe(0);
    expect(violation(SHRUG_POSE)).toBe(0);
    // Wave lifts the hand to about shoulder height.
    expect(armPoints(WAVE_POSE).wrist.y).toBeGreaterThan(-0.05);
  });

  for (const [name, hands] of [["wave", "R"], ["shrug", "both"]] as const) {
    it(`${name} moves the arm away from rest`, () => {
      const queue = createGestureQueue();
      const rest = sampleArm(queue, "R", 0);
      enqueue(queue, oneshot(name, hands), 10_000);
      for (let t = 10_000; t <= 10_500; t += 16) tick(queue, t);
      expect(armBlendWeight(queue, "R", 10.5)).toBeGreaterThan(0.5);
      const posed = sampleArm(queue, "R", 10.5);
      expect(Math.abs(posed.f.y - rest.f.y) + Math.abs(posed.f.z - rest.f.z)).toBeGreaterThan(0.3);
    });
  }

  it("think puts the hand near the chin, in front, without being scaled away", () => {
    expect(CATALOG_SCALE.think).toBe(1);
    const w = armPoints(THINK_POSE).wrist;
    expect(w.x).toBeLessThan(0.08);
    expect(w.y).toBeGreaterThan(-0.05);
    expect(w.z).toBeGreaterThan(0.2);
  });

  it("default point uses one arm, index extended, thumb tucked", () => {
    const { pose, scale } = pointPose(DEFAULT_POINT_DIR);
    expect(scale).toBe(1);
    expect(chooseSide({ x: -DEFAULT_POINT_DIR.x, y: DEFAULT_POINT_DIR.y, z: DEFAULT_POINT_DIR.z }).side).toBe(DOMINANT_SIDE);
    expect(pose.curl[0]).toBeLessThan(0.1);
    expect(Math.min(pose.curl[1], pose.curl[2], pose.curl[3])).toBeGreaterThan(0.95);
    expect(pose.thumb).toBeGreaterThanOrEqual(0.95);
  });
});
