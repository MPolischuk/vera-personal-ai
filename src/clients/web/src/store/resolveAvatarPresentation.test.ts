import { describe, expect, it } from "vitest";
import {
  resolveAvatarExpression,
  resolveAvatarPresentation,
} from "./resolveAvatarPresentation";

const happyHold = {
  cue: { expression: "happy", weight: 0.55, durationMs: 400 },
  turnId: "T",
};

const thinkingOverlay = {
  expression: "relaxed",
  weight: 0.4,
  durationMs: 220,
  visualState: "thinking",
};

describe("resolveAvatarPresentation", () => {
  it("prefers playing / speaking over everything else", () => {
    expect(
      resolveAvatarPresentation({
        voiceStatus: "playing",
        turnActive: true,
        avatarState: "thinking",
      }),
    ).toBe("speaking");

    expect(
      resolveAvatarPresentation({
        voiceStatus: "off",
        turnActive: false,
        avatarState: "speaking",
      }),
    ).toBe("speaking");
  });

  it("uses thinking while the turn is active", () => {
    expect(
      resolveAvatarPresentation({
        voiceStatus: "listening",
        turnActive: true,
        avatarState: "idle",
      }),
    ).toBe("thinking");

    expect(
      resolveAvatarPresentation({
        voiceStatus: "off",
        turnActive: false,
        avatarState: "thinking",
      }),
    ).toBe("thinking");
  });

  it("shows listening when the mic is on or the server said so", () => {
    expect(
      resolveAvatarPresentation({
        voiceStatus: "listening",
        turnActive: false,
        avatarState: "idle",
      }),
    ).toBe("listening");

    expect(
      resolveAvatarPresentation({
        voiceStatus: "capturing",
        turnActive: false,
        avatarState: null,
      }),
    ).toBe("listening");

    expect(
      resolveAvatarPresentation({
        voiceStatus: "off",
        turnActive: false,
        avatarState: "listening",
      }),
    ).toBe("listening");
  });

  it("falls back to idle", () => {
    expect(
      resolveAvatarPresentation({
        voiceStatus: "off",
        turnActive: false,
        avatarState: "idle",
      }),
    ).toBe("idle");

    expect(
      resolveAvatarPresentation({
        voiceStatus: "off",
        turnActive: false,
        avatarState: null,
      }),
    ).toBe("idle");
  });
});

describe("resolveAvatarExpression", () => {
  it("lets a live character hold override thinking overlay", () => {
    expect(
      resolveAvatarExpression({
        visualState: "thinking",
        characterHold: happyHold,
        currentTurnId: "T",
      }),
    ).toEqual(happyHold.cue);
  });

  it("keeps the G4 thinking overlay without a hold", () => {
    expect(
      resolveAvatarExpression({
        visualState: "thinking",
        characterHold: null,
        currentTurnId: "T",
      }),
    ).toEqual(thinkingOverlay);
  });

  it("keeps happy when a later same-turn neutral did not replace the hold", () => {
    expect(
      resolveAvatarExpression({
        visualState: "speaking",
        characterHold: happyHold,
        currentTurnId: "T",
      }),
    ).toEqual(happyHold.cue);
  });

  it("falls back to the state preset when currentTurnId is null", () => {
    expect(
      resolveAvatarExpression({
        visualState: "speaking",
        characterHold: happyHold,
        currentTurnId: null,
      }),
    ).toEqual({
      expression: "relaxed",
      weight: 0.2,
      durationMs: 220,
      visualState: "speaking",
    });
  });

  it("falls back to the state preset when currentTurnId is another turn", () => {
    expect(
      resolveAvatarExpression({
        visualState: "thinking",
        characterHold: happyHold,
        currentTurnId: "otro",
      }),
    ).toEqual(thinkingOverlay);
  });

  it("accepts a live relaxed hold at 400 ms", () => {
    const hold = {
      cue: { expression: "relaxed", weight: 0.55, durationMs: 400 },
      turnId: "T",
    };
    expect(
      resolveAvatarExpression({
        visualState: "thinking",
        characterHold: hold,
        currentTurnId: "T",
      }),
    ).toEqual(hold.cue);
  });
});
