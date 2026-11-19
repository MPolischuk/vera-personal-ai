import { afterEach, describe, expect, it } from "vitest";
import {
  beginExpressionCue,
  blendedExpressionWeights,
  createExpressionMotionState,
} from "./applyExpression";
import { EXPLAIN_WEIGHT, EXPRESSIVENESS, HAPPY_GESTURE_SCALE } from "./avatarMotion";
import { avatarMotionV2 } from "./avatarMotionFlag";
import { createDirector, directGestures, type Director } from "./director";
import {
  accentPeak,
  accentSchedule,
  blendExpressions,
  EXPRESSION_PRESETS,
  resetExpressionPublish,
  type BlendAccent,
  type BlendInput,
  type ExpressionPreset,
  type ExpressionWeights,
} from "./expressionBlend";

const QUIET = {
  shot: "bust" as const,
  hubGesture: null,
  turnError: false,
  ready: false,
};

const FACE_OFF: ExpressionWeights = {
  happy: 0,
  angry: 0,
  sad: 0,
  relaxed: 0,
  surprised: 0,
};

afterEach(() => {
  resetExpressionPublish();
});

describe("expression blend", () => {
  it("keeps a single base when nothing is accented", () => {
    expect(blendAt({ visualState: "idle", nowMs: 0 }).relaxed).toBeCloseTo(0.15);
    expect(blendAt({ visualState: "listening", nowMs: 0 }).relaxed).toBeCloseTo(0.2);
    expect(blendAt({ visualState: "speaking", nowMs: 0 }).relaxed).toBeCloseTo(0.2);
    expect(blendAt({ visualState: "thinking", nowMs: 0 }).relaxed).toBeCloseTo(0.4);
    expect(presetsAbove(blendAt({ visualState: "thinking", nowMs: 0 }))).toEqual(["relaxed"]);
  });

  it("caps happy at 0.7 and lifts the subtle maid presets", () => {
    expect(EXPRESSIVENESS).toBe(0.6);
    expect(accentPeak("happy", 0.8)).toBeCloseTo(0.7);
    expect(accentPeak("surprised", 0.8)).toBeCloseTo(0.8);
    expect(accentPeak("angry", 0.8)).toBeCloseTo(0.7);
    expect(accentPeak("sad", 0.8)).toBeCloseTo(0.85);
    expect(accentPeak("relaxed", 0.8)).toBeCloseTo(0.9);
    expect(accentPeak("sad", 0.4)).toBeCloseTo(0.48);
    for (const name of ["angry", "sad", "relaxed", "happy"] as const) {
      expect(accentPeak(name, 1)).toBeLessThan(0.95);
    }
  });

  it("allows two presets above 0.3 only inside a 600 ms accent change", () => {
    const sample = (nowMs: number) =>
      blendExpressions({
        visualState: "thinking",
        nowMs,
        previous: { expression: "happy", hubWeight: 0.8, replacedAtMs: 0 },
        accent: accentAt("sad", 0.8, 0, true),
      });

    expect(presetsAbove(sample(300)).length).toBe(2);
    expect(presetsAbove(sample(700)).length).toBe(1);
    expect(sample(700).sad).toBeGreaterThan(0.3);

    for (let nowMs = 0; nowMs <= 4000; nowMs += 10) {
      if (presetsAbove(sample(nowMs)).length > 1) {
        expect(nowMs).toBeLessThan(600);
      }
    }
  });

  it("keeps a fresh accent to one preset above 0.3 outside the rise", () => {
    const sample = (nowMs: number) =>
      blendExpressions({
        visualState: "thinking",
        nowMs,
        previous: null,
        accent: accentAt("happy", 0.8, 0, false),
      });

    expect(presetsAbove(sample(700))).toEqual(["happy"]);
    expect(sample(700).happy).toBeCloseTo(0.7);
    for (let nowMs = 0; nowMs <= 4000; nowMs += 10) {
      expect(presetsAbove(sample(nowMs)).length).toBeLessThanOrEqual(1);
    }
  });

  it("brings surprised back under 0.3 before 1000 ms", () => {
    const sample = (nowMs: number) =>
      blendExpressions({
        visualState: "speaking",
        nowMs,
        previous: null,
        accent: accentAt("surprised", 0.8, 0, false),
      });

    expect(sample(400).surprised).toBeGreaterThan(0.3);
    expect(sample(999).surprised).toBeLessThan(0.3);
    const stillHigh = [];
    for (let nowMs = 0; nowMs < 1000; nowMs += 10) {
      if (sample(nowMs).surprised > 0.3) {
        stillHigh.push(nowMs);
      }
    }
    expect(stillHigh.length).toBeGreaterThan(0);
    expect(Math.max(...stillHigh)).toBeLessThan(1000);
  });
});

describe("applyExpression with the flag off", () => {
  it("still stores a happy cue at the hub weight 0.8", () => {
    expect(avatarMotionV2).toBe(true);
    const state = createExpressionMotionState(0);
    beginExpressionCue(state, { expression: "happy", weight: 0.8, durationMs: 400 }, 0, false);
    expect(state.targetName).toBe("happy");
    expect(state.targetWeight).toBe(0.8);
  });
});

describe("applyExpression with motion v2", () => {
  it("holds a durationMs 0 happy cue above 0.3 after 10 s", () => {
    const state = createExpressionMotionState(0);
    beginExpressionCue(state, { expression: "happy", weight: 1, durationMs: 0 }, 0, true);
    expect(blendedExpressionWeights(state, 10_000).happy).toBeGreaterThan(0.3);
  });

  it("delegates a hub cue of 0.8 to the capped accent", () => {
    const state = createExpressionMotionState(0);
    beginExpressionCue(state, { expression: "happy", weight: 0.8, durationMs: 400 }, 0, true);
    const weights = blendedExpressionWeights(state, 400);
    expect(weights.happy).toBeCloseTo(0.7);
    expect(weights.happy).not.toBe(0.8);
  });

  it("publishes sad above 0.3 before the mesh has risen, so wave is omitted", () => {
    const state = createExpressionMotionState(0);
    beginExpressionCue(state, { expression: "sad", weight: 0.8, durationMs: 400 }, 0, true);
    expect(blendedExpressionWeights(state, 0).sad).toBeLessThanOrEqual(0.3);

    const director = createDirector("idle", 0);
    directGestures(director, 16, {
      ...QUIET,
      hubGesture: { name: "wave", intensity: 1, durationMs: 1200, playId: "sad-wave" },
    }, true);
    expect(queuedNames(director)).not.toContain("wave");
  });
});

describe("director expression modifiers", () => {
  it("does not accept wave while sad is above 0.3", () => {
    const blocked = createDirector("idle", 0);
    directGestures(blocked, 0, waveCue("sad"), true, undefined, { ...FACE_OFF, sad: 0.31 });
    expect(queuedNames(blocked)).not.toContain("wave");

    const open = createDirector("idle", 0);
    directGestures(open, 0, waveCue("edge"), true, undefined, { ...FACE_OFF, sad: 0.3 });
    expect(queuedNames(open)).toContain("wave");
  });

  it("does not accept wave or hands while angry is above 0.3", () => {
    const director = createDirector("idle", 0);
    directGestures(
      director,
      0,
      { ...QUIET, ready: true, hubGesture: { name: "wave", intensity: 1, durationMs: 800, playId: "angry" } },
      true,
      undefined,
      { ...FACE_OFF, angry: 0.4 },
    );
    const names = queuedNames(director);
    expect(names).not.toContain("wave");
    expect(names).not.toContain("hands");
  });

  it("scales the arm layer up while happy is above 0.3", () => {
    const director = createDirector("idle", 0);
    requestSpeaking(director);
    directGestures(director, 250, QUIET, true, undefined, { ...FACE_OFF, happy: 0.7 });
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.weight).toBeCloseTo(EXPLAIN_WEIGHT * HAPPY_GESTURE_SCALE);
  });

  it("scales the arm layer down while sad is above 0.3", () => {
    const director = createDirector("idle", 0);
    requestSpeaking(director);
    directGestures(director, 250, QUIET, true, undefined, { ...FACE_OFF, sad: 0.5 });
    expect(director.queue.arm?.weight).toBeCloseTo(EXPLAIN_WEIGHT * 0.7);
  });
});

function blendAt(partial: Pick<BlendInput, "visualState" | "nowMs">): ExpressionWeights {
  return blendExpressions({ ...partial, accent: null, previous: null });
}

function accentAt(
  expression: ExpressionPreset,
  hubWeight: number,
  startedAtMs: number,
  replacing: boolean,
): BlendAccent {
  return {
    expression,
    hubWeight,
    startedAtMs,
    ...accentSchedule(expression, replacing),
  };
}

function presetsAbove(weights: ExpressionWeights): ExpressionPreset[] {
  return EXPRESSION_PRESETS.filter((name) => weights[name] > 0.3);
}

function waveCue(playId: string) {
  return {
    ...QUIET,
    hubGesture: { name: "wave", intensity: 1, durationMs: 1200, playId },
  };
}

function requestSpeaking(director: Director): void {
  director.requested = "speaking";
  director.requestedSinceMs = 0;
}

function queuedNames(director: Director): string[] {
  return [
    director.queue.arm?.name,
    director.queue.head?.name,
    ...director.queue.pending.map((item) => item.name),
  ].filter((name): name is string => name !== undefined);
}
