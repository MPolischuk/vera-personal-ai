import { MOTION_GESTURES } from "../avatar/motionGestures";
import { describe, expect, it } from "vitest";
import { AVATAR_EXPRESSION_WHITELIST } from "../avatar/applyExpression";
import {
  AVATAR_GESTURE_WHITELIST,
  CHARACTER_GESTURE_DURATION_MS,
  CHARACTER_GESTURE_INTENSITY,
} from "../avatar/applyGesture";
import {
  BANK_EXPRESSION_IDS,
  BANK_GESTURE_IDS,
  BANK_MOTIONS,
  bankExpressionCue,
  bankGestureCue,
} from "./expressionBank";

describe("expressionBank catalog", () => {
  it("UI expression list equals the live whitelist", () => {
    expect(BANK_EXPRESSION_IDS).toEqual(AVATAR_EXPRESSION_WHITELIST);
  });

  it("UI gesture list equals the live whitelist", () => {
    expect(BANK_GESTURE_IDS).toEqual(AVATAR_GESTURE_WHITELIST);
  });

  it("offers a rest pose plus every hub gesture", () => {
    expect(BANK_MOTIONS.some((item) => item.kind === "rest")).toBe(true);
    expect(BANK_MOTIONS.some((item) => item.kind === "pose")).toBe(true);
    const hubs = BANK_MOTIONS.filter((item) => item.kind === "hub").map((item) => item.id);
    expect(hubs).toEqual([...AVATAR_GESTURE_WHITELIST]);
  });

  it("lists every procedural motion gesture by its registry name", () => {
    const motions = BANK_MOTIONS.filter((item) => item.kind === "motion").map((item) => item.id);
    expect(motions.length).toBeGreaterThanOrEqual(19);
    for (const id of motions) expect(MOTION_GESTURES).toHaveProperty(id);
  });
});

describe("bankExpressionCue", () => {
  it("holds non-neutral expressions at weight 1 with durationMs 0 (D6)", () => {
    expect(bankExpressionCue("happy")).toEqual({
      expression: "happy",
      weight: 1,
      durationMs: 0,
    });
  });

  it("maps neutral to weight 0", () => {
    expect(bankExpressionCue("neutral")).toEqual({
      expression: "neutral",
      weight: 0,
      durationMs: 0,
    });
  });
});

describe("bankGestureCue", () => {
  it("builds a gesture with the protocol duration and intensity (D7)", () => {
    expect(bankGestureCue("nod", "play-1")).toEqual({
      name: "nod",
      intensity: CHARACTER_GESTURE_INTENSITY,
      durationMs: CHARACTER_GESTURE_DURATION_MS,
      playId: "play-1",
    });
  });
});
