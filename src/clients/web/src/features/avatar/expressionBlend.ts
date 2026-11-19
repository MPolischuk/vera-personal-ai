import {
  ACCENT_FALL_MS,
  ACCENT_HOLD_MS,
  ACCENT_RISE_MS,
  ACCENT_GAIN,
  ACCENT_PRESET_CAP,
  ACCENT_WEIGHT_CAP,
  ANGRY_ACCENT_CAP,
  BASE_RELAXED_WEIGHT,
  EMOTION_BODY_THRESHOLD,
  EXPRESSION_CROSS_MS,
  EXPRESSIVENESS,
  HAPPY_GESTURE_SCALE,
  HAPPY_HEAD_PITCH,
  SAD_GESTURE_SCALE,
  SURPRISED_HEAD_PITCH,
  SURPRISED_MAX_MS,
  SURPRISED_WEIGHT_CAP,
} from "./avatarMotion";

/** Presets the blend writes. `neutral` is not a channel (D2). */
export const EXPRESSION_PRESETS = ["happy", "angry", "sad", "relaxed", "surprised"] as const;

export type ExpressionPreset = (typeof EXPRESSION_PRESETS)[number];

export type ExpressionVisualState = keyof typeof BASE_RELAXED_WEIGHT;

export interface ExpressionWeights {
  happy: number;
  angry: number;
  sad: number;
  relaxed: number;
  surprised: number;
}

export interface BlendAccent {
  expression: ExpressionPreset;
  hubWeight: number;
  startedAtMs: number;
  riseMs: number;
  holdMs: number;
  fallMs: number;
}

export interface BlendPrevious {
  expression: ExpressionPreset;
  hubWeight: number;
  replacedAtMs: number;
}

export interface BlendInput {
  visualState: ExpressionVisualState;
  nowMs: number;
  accent: BlendAccent | null;
  previous: BlendPrevious | null;
}

export interface ExpressionGestureModifier {
  /** Multiplies arm-layer weight. `GESTURE_SCALE` is applied later by the queue. */
  scale: number;
  /** Added to a head pose the director already owns (radians). */
  pitch: number;
  omitWave: boolean;
  omitHands: boolean;
}

const EMPTY_WEIGHTS: ExpressionWeights = {
  happy: 0,
  angry: 0,
  sad: 0,
  relaxed: 0,
  surprised: 0,
};

let publishedGestureFace: ExpressionWeights = { ...EMPTY_WEIGHTS };
let notedVisualState: ExpressionVisualState | null = null;

export function accentSchedule(
  expression: ExpressionPreset,
  replacing: boolean,
): { riseMs: number; holdMs: number; fallMs: number } {
  if (expression === "surprised") {
    const riseMs = ACCENT_RISE_MS;
    const fallMs = ACCENT_FALL_MS;
    const holdMs = Math.max(0, SURPRISED_MAX_MS - riseMs - fallMs);
    return { riseMs, holdMs, fallMs };
  }
  if (replacing) {
    return { riseMs: EXPRESSION_CROSS_MS, holdMs: ACCENT_HOLD_MS, fallMs: ACCENT_FALL_MS };
  }
  return { riseMs: ACCENT_RISE_MS, holdMs: ACCENT_HOLD_MS, fallMs: ACCENT_FALL_MS };
}

/** Bank preview. Stays at the peak until another cue replaces it. */
export function heldAccentSchedule(): { riseMs: number; holdMs: number; fallMs: number } {
  return { riseMs: ACCENT_RISE_MS, holdMs: Number.POSITIVE_INFINITY, fallMs: 0 };
}

/** Client cap. The hub may still send 0.8 (D1, D4). */
export function accentPeak(expression: ExpressionPreset, hubWeight: number): number {
  const hub = clamp01(hubWeight);
  const scaled = (cap: number) => Math.min(hub, cap) * (EXPRESSIVENESS / 0.6);
  if (expression === "surprised") {
    return scaled(SURPRISED_WEIGHT_CAP);
  }
  const gain = (ACCENT_GAIN as Record<string, number>)[expression] ?? 1;
  const cap =
    expression === "angry"
      ? ANGRY_ACCENT_CAP
      : ((ACCENT_PRESET_CAP as Record<string, number>)[expression] ?? ACCENT_WEIGHT_CAP);
  return Math.min(hub * gain, cap) * (EXPRESSIVENESS / 0.6);
}

export function blendExpressions(input: BlendInput): ExpressionWeights {
  const weights = emptyWeights();
  const accentLevel = input.accent
    ? envelope01(
        input.nowMs - input.accent.startedAtMs,
        input.accent.riseMs,
        input.accent.holdMs,
        input.accent.fallMs,
      )
    : 0;
  const previousLevel = fadingLevel(input.previous, input.nowMs);

  if (input.accent && input.accent.expression !== "relaxed") {
    weights[input.accent.expression] = accentPeak(input.accent.expression, input.accent.hubWeight) * accentLevel;
  }
  if (input.previous && input.previous.expression !== "relaxed" && previousLevel > 0) {
    const amount = accentPeak(input.previous.expression, input.previous.hubWeight) * previousLevel;
    weights[input.previous.expression] = Math.max(weights[input.previous.expression], amount);
  }

  const base = BASE_RELAXED_WEIGHT[input.visualState];
  weights.relaxed = relaxedWeight(input, base, accentLevel, previousLevel);
  return weights;
}

/**
 * Face weights the director should read. During the rise and the hold the
 * accent counts at its peak so a same-frame `wave` sees `sad` already above
 * the threshold. The mesh still uses `blendExpressions`.
 */
export function gestureExpressionWeights(input: BlendInput): ExpressionWeights {
  const visual = blendExpressions(input);
  const accent = input.accent;
  if (!accent) {
    return visual;
  }
  const elapsed = input.nowMs - accent.startedAtMs;
  if (elapsed < 0 || elapsed >= accent.riseMs + accent.holdMs) {
    return visual;
  }
  const peak = accentPeak(accent.expression, accent.hubWeight);
  if (peak <= EMOTION_BODY_THRESHOLD) {
    return visual;
  }
  return {
    ...visual,
    [accent.expression]: Math.max(visual[accent.expression], peak),
  };
}

export function expressionGestureModifier(weights: ExpressionWeights): ExpressionGestureModifier {
  let scale = 1;
  let pitch = 0;
  let omitWave = false;
  let omitHands = false;
  if (weights.happy > EMOTION_BODY_THRESHOLD) {
    scale *= HAPPY_GESTURE_SCALE;
    pitch += HAPPY_HEAD_PITCH;
  }
  if (weights.sad > EMOTION_BODY_THRESHOLD) {
    scale *= SAD_GESTURE_SCALE;
    omitWave = true;
  }
  if (weights.angry > EMOTION_BODY_THRESHOLD) {
    omitWave = true;
    omitHands = true;
  }
  if (weights.surprised > EMOTION_BODY_THRESHOLD) {
    pitch += SURPRISED_HEAD_PITCH;
  }
  return { scale, pitch, omitWave, omitHands };
}

export function publishGestureFace(weights: ExpressionWeights): void {
  publishedGestureFace = { ...weights };
}

export function publishedGestureFaceNow(): ExpressionWeights {
  return publishedGestureFace;
}

export function noteVisualState(state: ExpressionVisualState): void {
  notedVisualState = state;
}

export function liveVisualState(): ExpressionVisualState | null {
  return notedVisualState;
}

/** Test isolation. Production frames overwrite the snapshot every tick. */
export function resetExpressionPublish(): void {
  publishedGestureFace = emptyWeights();
  notedVisualState = null;
}

function relaxedWeight(
  input: BlendInput,
  base: number,
  accentLevel: number,
  previousLevel: number,
): number {
  const accent = input.accent;
  const previous = input.previous;
  if (accent?.expression === "relaxed") {
    const peak = accentPeak("relaxed", accent.hubWeight);
    return base * (1 - accentLevel) + peak * accentLevel;
  }

  const cover = Math.max(
    accent ? accentLevel : 0,
    previous && previous.expression !== "relaxed" ? previousLevel : 0,
  );
  const uncovered = base * (1 - cover);
  if (previous?.expression === "relaxed" && previousLevel > 0) {
    const peak = accentPeak("relaxed", previous.hubWeight);
    return Math.max(uncovered, peak * previousLevel);
  }
  return uncovered;
}

function fadingLevel(previous: BlendPrevious | null, nowMs: number): number {
  if (!previous) {
    return 0;
  }
  const elapsed = nowMs - previous.replacedAtMs;
  if (elapsed <= 0) {
    return 1;
  }
  if (elapsed >= EXPRESSION_CROSS_MS) {
    return 0;
  }
  return 1 - smoothstep01(elapsed / EXPRESSION_CROSS_MS);
}

function envelope01(elapsed: number, rise: number, hold: number, fall: number): number {
  if (elapsed <= 0) {
    return 0;
  }
  if (rise > 0 && elapsed < rise) {
    return smoothstep01(elapsed / rise);
  }
  if (elapsed < rise + hold) {
    return 1;
  }
  const fallElapsed = elapsed - rise - hold;
  if (fall <= 0 || fallElapsed >= fall) {
    return 0;
  }
  return 1 - smoothstep01(fallElapsed / fall);
}

function smoothstep01(t: number): number {
  const x = clamp01(t);
  return x * x * (3 - 2 * x);
}

function clamp01(value: number): number {
  if (value <= 0) {
    return 0;
  }
  if (value >= 1) {
    return 1;
  }
  return value;
}

function emptyWeights(): ExpressionWeights {
  return { ...EMPTY_WEIGHTS };
}
