import { describe, expect, it } from "vitest";
import type { AvatarExpressionCue } from "../features/avatar/applyExpression";
import {
  CHARACTER_EXPRESSION_DURATION_MS,
  holdIsLive,
  isCharacterExpressionCue,
  onExpressionCue,
  onGestureCue,
  onTurnCompleted,
  onTurnFailed,
  onTurnStarted,
  type AvatarCueGesture,
  type AvatarCueState,
} from "./avatarCueLifetime";

const empty: AvatarCueState = {
  characterExpression: null,
  avatarGesture: null,
};

const happy: AvatarExpressionCue = {
  expression: "happy",
  weight: 0.55,
  durationMs: CHARACTER_EXPRESSION_DURATION_MS,
};

const relaxedHold: AvatarExpressionCue = {
  expression: "relaxed",
  weight: 0.55,
  durationMs: CHARACTER_EXPRESSION_DURATION_MS,
};

const thinkingRelaxed: AvatarExpressionCue = {
  expression: "relaxed",
  weight: 0.4,
  durationMs: 280,
};

const idleNeutral: AvatarExpressionCue = {
  expression: "neutral",
  weight: 0,
  durationMs: 280,
};

const wave: AvatarCueGesture = {
  name: "wave",
  intensity: 0.7,
  durationMs: 700,
  playId: "turn-1",
};

function withHold(turnId: string): AvatarCueState {
  return {
    characterExpression: { cue: happy, turnId },
    avatarGesture: wave,
  };
}

describe("isCharacterExpressionCue", () => {
  it("accepts a 400 ms non-neutral whitelist expression", () => {
    expect(isCharacterExpressionCue(happy)).toBe(true);
    expect(isCharacterExpressionCue(relaxedHold)).toBe(true);
  });

  it("rejects thinking overlay and idle/speaking neutrals", () => {
    expect(isCharacterExpressionCue(thinkingRelaxed)).toBe(false);
    expect(isCharacterExpressionCue(idleNeutral)).toBe(false);
    expect(
      isCharacterExpressionCue({
        expression: "neutral",
        weight: 0,
        durationMs: CHARACTER_EXPRESSION_DURATION_MS,
      }),
    ).toBe(false);
  });
});

describe("holdIsLive", () => {
  const hold = { cue: happy, turnId: "T" };

  it("is live while the hold belongs to the current turn", () => {
    expect(holdIsLive(hold, "T")).toBe(true);
  });

  it("dies when currentTurnId is null", () => {
    expect(holdIsLive(hold, null)).toBe(false);
  });

  it("dies when currentTurnId is another turn", () => {
    expect(holdIsLive(hold, "otro")).toBe(false);
  });

  it("is false when there is no hold", () => {
    expect(holdIsLive(null, "T")).toBe(false);
  });
});

describe("avatar cue lifetime", () => {
  it("onTurnStarted always clears gesture and hold", () => {
    expect(onTurnStarted(withHold("T"))).toEqual(empty);
  });

  it("onTurnCompleted with Completed keeps the gesture", () => {
    const state = withHold("T");
    expect(onTurnCompleted(state, "Completed")).toBe(state);
  });

  it("onTurnCompleted with Interrupted clears the gesture and keeps the hold", () => {
    expect(onTurnCompleted(withHold("T"), "Interrupted")).toEqual({
      characterExpression: { cue: happy, turnId: "T" },
      avatarGesture: null,
    });
  });

  it("onTurnFailed clears the gesture", () => {
    expect(onTurnFailed(withHold("T"))).toEqual({
      characterExpression: { cue: happy, turnId: "T" },
      avatarGesture: null,
    });
  });

  it("onExpressionCue writes the slot for a character cue", () => {
    expect(onExpressionCue(empty, happy, "T")).toEqual({
      characterExpression: { cue: happy, turnId: "T" },
      avatarGesture: null,
    });
  });

  it("a later neutral of the same turn does not clear the hold", () => {
    const held = onExpressionCue(empty, happy, "T");
    expect(onExpressionCue(held, idleNeutral, "T")).toEqual(held);
  });

  it("onGestureCue replaces the gesture", () => {
    const next = { ...wave, playId: "turn-2" };
    expect(onGestureCue(withHold("T"), next).avatarGesture).toBe(next);
  });
});
