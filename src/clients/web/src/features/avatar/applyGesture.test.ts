import { describe, expect, it } from "vitest";
import {
  beginGestureCue,
  createGestureMotionState,
  gestureArmOffsetAt,
  gestureHeadOffsetAt,
  gestureShrugOffsetAt,
  isAvatarGestureName,
  isGestureMotionActive,
} from "./applyGesture";

describe("applyGesture helpers", () => {
  it("accepts only the gesture whitelist", () => {
    expect(isAvatarGestureName("nod")).toBe(true);
    expect(isAvatarGestureName("none")).toBe(false);
    expect(isAvatarGestureName("clap")).toBe(false);
  });

  it("returns zero head offset at progress 0, 0.5 and 1", () => {
    expect(gestureHeadOffsetAt("nod", 0, 1).pitch).toBe(0);
    expect(gestureHeadOffsetAt("nod", 0.5, 1).pitch).toBeCloseTo(0);
    expect(gestureHeadOffsetAt("nod", 1, 1).pitch).toBe(0);
    expect(gestureHeadOffsetAt("shake", 0, 1).yaw).toBe(0);
    expect(gestureHeadOffsetAt("shake", 0.5, 1).yaw).toBeCloseTo(0);
    expect(gestureHeadOffsetAt("shake", 1, 1).yaw).toBe(0);
  });

  it("changes sign once for nod and shake in (0, 1)", () => {
    expect(countSignChanges("nod")).toBe(1);
    expect(countSignChanges("shake")).toBe(1);
  });

  it("nods with non-zero pitch at progress 0.125", () => {
    expect(gestureHeadOffsetAt("nod", 0.125, 1).pitch).not.toBe(0);
    expect(gestureHeadOffsetAt("nod", 0.125, 0.7).pitch).toBeCloseTo(
      gestureHeadOffsetAt("nod", 0.125, 1).pitch * 0.7,
    );
  });

  it("shakes with non-zero yaw mid-cycle", () => {
    expect(gestureHeadOffsetAt("shake", 0.125, 1).yaw).not.toBe(0);
  });

  it("returns zero arm / shrug offsets at progress 0 and 1", () => {
    expect(gestureArmOffsetAt(0, 1).roll).toBe(0);
    expect(gestureArmOffsetAt(1, 1).roll).toBe(0);
    expect(gestureShrugOffsetAt(0, 1)).toBe(0);
    expect(gestureShrugOffsetAt(1, 1)).toBe(0);
  });

  it("clears active gesture when cue is null", () => {
    const state = createGestureMotionState();
    beginGestureCue(state, { name: "nod", intensity: 0.7, durationMs: 700 }, 10);
    expect(state.active).toBe("nod");
    beginGestureCue(state, null, 20);
    expect(state.active).toBeNull();
  });

  it("ignores unknown gesture names", () => {
    const state = createGestureMotionState();
    beginGestureCue(state, { name: "clap", intensity: 1, durationMs: 100 }, 0);
    expect(state.active).toBeNull();
  });

  it("reports procedural gesture flight from start until duration elapses", () => {
    const state = createGestureMotionState();
    expect(isGestureMotionActive(state, 0)).toBe(false);
    beginGestureCue(state, { name: "nod", intensity: 0.7, durationMs: 1100 }, 10);
    expect(isGestureMotionActive(state, 10)).toBe(true);
    expect(isGestureMotionActive(state, 1109)).toBe(true);
    expect(isGestureMotionActive(state, 1110)).toBe(false);
    beginGestureCue(state, null, 2000);
    expect(isGestureMotionActive(state, 2000)).toBe(false);
  });
});

function countSignChanges(gesture: "nod" | "shake"): number {
  const samples = 400;
  let lastSign = 0;
  let changes = 0;
  for (let i = 1; i < samples; i++) {
    const offset = gestureHeadOffsetAt(gesture, i / samples, 1);
    const value = gesture === "nod" ? offset.pitch : offset.yaw;
    const sign = Math.abs(value) < 1e-10 ? 0 : Math.sign(value);
    if (sign !== 0 && lastSign !== 0 && sign !== lastSign) {
      changes += 1;
    }
    if (sign !== 0) {
      lastSign = sign;
    }
  }
  return changes;
}
