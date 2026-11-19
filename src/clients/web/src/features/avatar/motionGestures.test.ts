import { describe, expect, it } from "vitest";
import { armPoints, violation } from "./arms";
import {
  createMotionFrame, IDLE_FIDGETS, MOTION_GESTURES, MOTION_POSE_DESIRED, MOTION_POSE_SCALE, MOTION_POSES,
} from "./motionGestures";

describe("motion poses", () => {
  it("are all reachable without containment (scale 1) and clip-free", () => {
    expect(MOTION_POSE_SCALE.length).toBe(Object.keys(MOTION_POSES).length);
    MOTION_POSE_SCALE.forEach((s, i) => {
      expect(s, Object.keys(MOTION_POSES)[i]).toBeCloseTo(1, 2);
      expect(violation(MOTION_POSE_DESIRED[i]!)).toBeLessThan(1e-6);
    });
  });

  it("keep both wrists in front of the torso/apron box", () => {
    for (const d of MOTION_POSE_DESIRED) {
      const w = armPoints(d).wrist;
      if (Math.abs(w.x) < 0.11 && w.y > -0.62 && w.y < 0.16) expect(w.z).toBeGreaterThanOrEqual(0.1);
    }
  });
});

describe("motion gesture registry", () => {
  it("covers idle fidgets, gestures and reactions with finite frames", () => {
    for (const n of [...IDLE_FIDGETS, "yawn", "sigh", "neckStretch", "bow", "handChest", "tapFingers",
      "clap", "happyFist", "sadClasp", "laugh", "surpriseStep", "count", "openPalms", "sidePoint", "openHand"]) {
      const g = MOTION_GESTURES[n];
      expect(g, n).toBeDefined();
      expect(g!.durationMs).toBeGreaterThan(500);
      for (const p of [0, 0.25, 0.5, 0.75, 1]) {
        const f = createMotionFrame();
        g!.frame?.(p, p * g!.durationMs / 1000, f);
        expect(JSON.stringify(f)).not.toMatch(/NaN|null/);
      }
    }
  });
});
