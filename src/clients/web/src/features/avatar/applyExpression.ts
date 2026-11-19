import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName } from "@pixiv/three-vrm";
import { BASE_RELAXED_WEIGHT } from "./avatarMotion";
import { avatarMotionV2 } from "./avatarMotionFlag";
import {
  accentSchedule,
  blendExpressions,
  heldAccentSchedule,
  EXPRESSION_PRESETS,
  gestureExpressionWeights,
  liveVisualState,
  publishGestureFace,
  type BlendAccent,
  type BlendPrevious,
  type ExpressionPreset,
  type ExpressionVisualState,
} from "./expressionBlend";

/** Hub seal (AvatarCues). State overlays use another duration. */
const CHARACTER_CUE_MS = 400;

export const AVATAR_EXPRESSION_WHITELIST = [
  "happy",
  "angry",
  "sad",
  "relaxed",
  "surprised",
  "neutral",
] as const;

export type AvatarExpressionName = (typeof AVATAR_EXPRESSION_WHITELIST)[number];

export interface AvatarExpressionCue {
  expression: string;
  weight: number;
  durationMs: number;
  /** State overlay while motion v2 is on. Hub cues omit it. */
  visualState?: ExpressionVisualState;
}

export interface ExpressionMotionState {
  activeName: AvatarExpressionName | null;
  currentWeight: number;
  targetName: AvatarExpressionName;
  targetWeight: number;
  startWeight: number;
  startMs: number;
  durationMs: number;
  warnedMissing: Set<string>;
  /** Base mood for the blend. Ignored while motion v2 is off. */
  visualState: ExpressionVisualState;
  accent: BlendAccent | null;
  previous: BlendPrevious | null;
}

const PRESET_BY_NAME: Record<AvatarExpressionName, VRMExpressionPresetName> = {
  happy: VRMExpressionPresetName.Happy,
  angry: VRMExpressionPresetName.Angry,
  sad: VRMExpressionPresetName.Sad,
  relaxed: VRMExpressionPresetName.Relaxed,
  surprised: VRMExpressionPresetName.Surprised,
  neutral: VRMExpressionPresetName.Neutral,
};

const EMOTION_PRESETS: readonly AvatarExpressionName[] = AVATAR_EXPRESSION_WHITELIST;

export function isAvatarExpressionName(value: string): value is AvatarExpressionName {
  return (AVATAR_EXPRESSION_WHITELIST as readonly string[]).includes(value);
}

/** Client overlay for G4: hold relaxed while thinking even if the last hub cue was idle/neutral. */
export function expressionCueForVisualState(
  visualState: "idle" | "listening" | "thinking" | "speaking",
): AvatarExpressionCue {
  if (avatarMotionV2) {
    return {
      expression: "relaxed",
      weight: BASE_RELAXED_WEIGHT[visualState],
      durationMs: 220,
      visualState,
    };
  }
  if (visualState === "thinking") {
    return { expression: "relaxed", weight: 0.75, durationMs: 220 };
  }
  return { expression: "neutral", weight: 0, durationMs: 220 };
}

export function createExpressionMotionState(
  nowMs = 0,
): ExpressionMotionState {
  return {
    activeName: null,
    currentWeight: 0,
    targetName: "neutral",
    targetWeight: 0,
    startWeight: 0,
    startMs: nowMs,
    durationMs: 0,
    warnedMissing: new Set(),
    visualState: "idle",
    accent: null,
    previous: null,
  };
}

/** Linear lerp of expression weight; progress clamped to [0, 1]. */
export function lerpExpressionWeight(
  startWeight: number,
  targetWeight: number,
  progress: number,
): number {
  const p = Math.min(1, Math.max(0, progress));
  return startWeight + (targetWeight - startWeight) * p;
}

export function beginExpressionCue(
  state: ExpressionMotionState,
  cue: AvatarExpressionCue,
  nowMs: number,
  motionV2: boolean = avatarMotionV2,
): void {
  if (motionV2) {
    beginBlendedCue(state, cue, nowMs);
    return;
  }
  if (!isAvatarExpressionName(cue.expression)) {
    return;
  }

  state.startWeight = state.currentWeight;
  state.targetName = cue.expression;
  state.targetWeight = Math.min(1, Math.max(0, cue.weight));
  state.startMs = nowMs;
  state.durationMs = Math.max(0, cue.durationMs);
  if (state.durationMs === 0) {
    state.currentWeight = state.targetWeight;
    state.activeName = cue.expression === "neutral" ? null : cue.expression;
  }
}

/**
 * Applies the active emotion preset. Never writes blink or viseme morphs.
 */
export function updateExpressionMotion(
  vrm: VRM,
  state: ExpressionMotionState,
  nowMs: number,
  motionV2: boolean = avatarMotionV2,
): void {
  if (motionV2) {
    updateBlendedExpression(vrm, state, nowMs);
    return;
  }
  const manager = vrm.expressionManager;
  if (!manager) {
    return;
  }

  if (state.durationMs > 0) {
    const progress = (nowMs - state.startMs) / state.durationMs;
    state.currentWeight = lerpExpressionWeight(
      state.startWeight,
      state.targetWeight,
      progress,
    );
    if (progress >= 1) {
      state.durationMs = 0;
      state.currentWeight = state.targetWeight;
      state.activeName =
        state.targetName === "neutral" || state.targetWeight <= 0
          ? null
          : state.targetName;
    } else if (state.targetName !== "neutral") {
      state.activeName = state.targetName;
    }
  }

  const writeName =
    state.targetName === "neutral" && state.currentWeight <= 0
      ? null
      : state.targetName === "neutral"
        ? state.activeName
        : state.targetName;

  for (const name of EMOTION_PRESETS) {
    if (name === "neutral") {
      continue;
    }

    const preset = PRESET_BY_NAME[name];
    const expression = manager.getExpression(preset);
    if (!expression) {
      if (!state.warnedMissing.has(name)) {
        state.warnedMissing.add(name);
        console.info(`[avatar] expression preset missing: ${name}`);
      }
      continue;
    }

    if (writeName === name) {
      manager.setValue(preset, state.currentWeight);
    } else {
      manager.setValue(preset, 0);
    }
  }
}

/** Weights the v2 path writes. The false path does not call this. */
export function blendedExpressionWeights(
  state: ExpressionMotionState,
  nowMs: number,
) {
  return blendExpressions(blendInput(state, nowMs));
}

function beginBlendedCue(
  state: ExpressionMotionState,
  cue: AvatarExpressionCue,
  nowMs: number,
): void {
  if (cue.visualState) {
    state.visualState = cue.visualState;
    publishGestureFace(gestureExpressionWeights(blendInput(state, nowMs)));
    return;
  }
  if (cue.expression === "neutral") {
    if (state.accent) {
      state.previous = {
        expression: state.accent.expression,
        hubWeight: state.accent.hubWeight,
        replacedAtMs: nowMs,
      };
    }
    state.accent = null;
    publishGestureFace(gestureExpressionWeights(blendInput(state, nowMs)));
    return;
  }
  const held = cue.durationMs === 0 && isPreviewExpression(cue.expression);
  if (!held && !isAccentCue(cue)) {
    return;
  }

  const replacing = state.accent !== null;
  if (state.accent) {
    state.previous = {
      expression: state.accent.expression,
      hubWeight: state.accent.hubWeight,
      replacedAtMs: nowMs,
    };
  }
  state.accent = {
    expression: cue.expression as ExpressionPreset,
    hubWeight: cue.weight,
    startedAtMs: nowMs,
    ...(held ? heldAccentSchedule() : accentSchedule(cue.expression as ExpressionPreset, replacing)),
  };
  publishGestureFace(gestureExpressionWeights(blendInput(state, nowMs)));
}

function updateBlendedExpression(
  vrm: VRM,
  state: ExpressionMotionState,
  nowMs: number,
): void {
  const live = liveVisualState();
  if (live) {
    state.visualState = live;
  }
  const input = blendInput(state, nowMs);
  publishGestureFace(gestureExpressionWeights(input));

  const manager = vrm.expressionManager;
  if (!manager) {
    return;
  }

  const weights = blendExpressions(input);
  for (const name of EXPRESSION_PRESETS) {
    const preset = PRESET_BY_NAME[name];
    const expression = manager.getExpression(preset);
    if (!expression) {
      if (!state.warnedMissing.has(name)) {
        state.warnedMissing.add(name);
        console.info(`[avatar] expression preset missing: ${name}`);
      }
      continue;
    }
    manager.setValue(preset, weights[name]);
  }
}

function blendInput(state: ExpressionMotionState, nowMs: number) {
  return {
    visualState: state.visualState,
    nowMs,
    accent: state.accent,
    previous: state.previous,
  };
}

function isPreviewExpression(expression: string): expression is ExpressionPreset {
  return (EXPRESSION_PRESETS as readonly string[]).includes(expression);
}

function isAccentCue(
  cue: AvatarExpressionCue,
): cue is AvatarExpressionCue & { expression: ExpressionPreset } {
  return (
    cue.durationMs === CHARACTER_CUE_MS &&
    cue.expression !== "neutral" &&
    (EXPRESSION_PRESETS as readonly string[]).includes(cue.expression)
  );
}
