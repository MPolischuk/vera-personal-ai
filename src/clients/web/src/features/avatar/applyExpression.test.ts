import { describe, expect, it } from "vitest";
import {
  beginExpressionCue,
  createExpressionMotionState,
  expressionCueForVisualState,
  isAvatarExpressionName,
  lerpExpressionWeight,
} from "./applyExpression";

describe("applyExpression helpers", () => {
  it("accepts only the emotion whitelist", () => {
    expect(isAvatarExpressionName("relaxed")).toBe(true);
    expect(isAvatarExpressionName("aa")).toBe(false);
    expect(isAvatarExpressionName("blink")).toBe(false);
  });

  it("lerps weight linearly", () => {
    expect(lerpExpressionWeight(0, 0.4, 0)).toBe(0);
    expect(lerpExpressionWeight(0, 0.4, 0.5)).toBeCloseTo(0.2);
    expect(lerpExpressionWeight(0, 0.4, 1)).toBeCloseTo(0.4);
    expect(lerpExpressionWeight(0.4, 0, 1)).toBe(0);
  });

  it("begins a cue from the current weight", () => {
    const state = createExpressionMotionState(0);
    state.currentWeight = 0.2;
    beginExpressionCue(
      state,
      { expression: "relaxed", weight: 0.4, durationMs: 280 },
      100,
      false,
    );
    expect(state.startWeight).toBe(0.2);
    expect(state.targetName).toBe("relaxed");
    expect(state.targetWeight).toBe(0.4);
    expect(state.durationMs).toBe(280);
  });

  it("ignores unknown expression names", () => {
    const state = createExpressionMotionState(0);
    beginExpressionCue(state, { expression: "smug", weight: 1, durationMs: 100 }, 0, false);
    expect(state.targetName).toBe("neutral");
  });

  it("maps thinking to a readable relaxed overlay", () => {
    expect(expressionCueForVisualState("thinking")).toEqual({
      expression: "relaxed",
      weight: 0.4,
      durationMs: 220,
      visualState: "thinking",
    });
    expect(expressionCueForVisualState("listening")).toEqual({
      expression: "relaxed",
      weight: 0.2,
      durationMs: 220,
      visualState: "listening",
    });
  });
});
