import { describe, expect, it } from "vitest";
import {
  POSE_SETTLE_MS,
  advancePoseMotion,
  createPoseMotionState,
  poseOffsetForState,
  setPoseTarget,
} from "./poseForState";

describe("poseForState", () => {
  it("returns distinct head offsets for the four visual states", () => {
    const idle = poseOffsetForState("idle");
    const listening = poseOffsetForState("listening");
    const thinking = poseOffsetForState("thinking");
    const speaking = poseOffsetForState("speaking");

    expect(idle).toEqual({ pitch: 0, yaw: 0, roll: 0 });
    expect(listening.yaw).toBeGreaterThan(0.2);
    expect(thinking.pitch).toBeLessThan(-0.2);
    expect(speaking.pitch).toBeGreaterThan(0.1);
  });

  it("covers the same pitch travel in 180 ms at 60 Hz and 144 Hz", () => {
    const at60 = pitchAfterSettleWindow(1 / 60);
    const at144 = pitchAfterSettleWindow(1 / 144);
    const thinking = poseOffsetForState("thinking");
    expect(Math.abs(at60 - at144)).toBeLessThan(Math.abs(thinking.pitch) * 0.05);
  });

  it("holds current on a frozen target and resumes after unfreeze", () => {
    const state = createPoseMotionState();
    const thinking = poseOffsetForState("thinking");
    const speaking = poseOffsetForState("speaking");

    setPoseTarget(state, "thinking");
    settle(state, 1 / 60);
    expect(state.current.pitch).toBeCloseTo(thinking.pitch, 2);

    for (let i = 0; i < 20; i += 1) {
      advancePoseMotion(state, 1 / 60);
    }
    expect(state.current.pitch).toBeCloseTo(thinking.pitch, 2);

    setPoseTarget(state, "speaking");
    settle(state, 1 / 60);
    expect(state.current.pitch).toBeCloseTo(speaking.pitch, 2);
  });
});

function pitchAfterSettleWindow(dt: number): number {
  const state = createPoseMotionState();
  setPoseTarget(state, "thinking");
  let elapsed = 0;
  while (elapsed < POSE_SETTLE_MS / 1000) {
    advancePoseMotion(state, dt);
    elapsed += dt;
  }
  return state.current.pitch;
}

function settle(state: ReturnType<typeof createPoseMotionState>, dt: number): void {
  let elapsed = 0;
  while (elapsed < (POSE_SETTLE_MS / 1000) * 8) {
    advancePoseMotion(state, dt);
    elapsed += dt;
  }
}
