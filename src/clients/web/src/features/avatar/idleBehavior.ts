import { IDLE_FIDGETS } from "./motionGestures";

/**
 * Living idle: weight shifts, eye saccades and glances to camera, fidgets,
 * sighs, progressive boredom, and an attentive straighten while the user
 * types. Pure scheduler: the director turns its output into queue cues and
 * the stage applies posture, gaze and smile.
 */

export const IDLE_TIMING = {
  /** Weight shift period range (ms). */
  shiftMinMs: 28_000,
  shiftMaxMs: 52_000,
  /** Weight crossfade time constant (ms). */
  shiftTauMs: 550,
  /** Fidget interval when fresh / fully bored (ms). */
  fidgetFreshMs: 26_000,
  fidgetBoredMs: 12_000,
  /** Sigh / neck stretch interval fresh / bored (ms). */
  sighFreshMs: 72_000,
  sighBoredMs: 36_000,
  /** Boredom ramps from 0 at `boredFromMs` of idle to 1 at `boredFullMs`. */
  boredFromMs: 60_000,
  boredFullMs: 300_000,
  /** Hidden yawn only after this much idle, at most once per `yawnGapMs`. */
  yawnAfterMs: 150_000,
  yawnGapMs: 70_000,
  /** Glance to camera every so often, held for `glanceMs`. */
  glanceMinMs: 12_000,
  glanceMaxMs: 22_000,
  glanceMs: 1_900,
  /** First fidget is not before this much idle. */
  firstFidgetMs: 10_000,
  /** Eye saccade gap when fresh (ms). Bored shortens it by up to 30%. */
  saccadeMinMs: 1_600,
  saccadeMaxMs: 4_200,
} as const;

/** Gaze offsets (rad, eyes; the head follows `HEAD_FOLLOW` of it). */
export const GAZE = {
  saccadeFresh: 0.05,
  saccadeBored: 0.2,
  pitchShare: 0.45,
  glanceSmile: 0.28,
  typingSmile: 0.12,
} as const;


export interface IdleBehaviorState {
  seed: number;
  idleSinceMs: number;
  lastMs: number;
  nextFidgetMs: number;
  nextSighMs: number;
  nextShiftMs: number;
  nextSaccadeMs: number;
  nextGlanceMs: number;
  glanceUntilMs: number;
  lastYawnMs: number;
  weightTarget: number;
  weight: number;
  straighten: number;
  gazeYaw: number;
  gazePitch: number;
  smile: number;
  typing: boolean;
  fidgetIndex: number;
  sighIndex: number;
}

export interface IdleBehaviorInput {
  /** Published director state is idle and nothing higher is playing. */
  idle: boolean;
  typing: boolean;
  armFree: boolean;
  headFree: boolean;
  /** Bench only: pretend the avatar has been idle this much longer. */
  extraIdleMs?: number;
}

export interface IdleBehaviorOutput {
  fidget: string | null;
  headCue: string | null;
  /** Typing just started: drop idle fidgets now. */
  attention: boolean;
  weight: number;
  straighten: number;
  gaze: { yaw: number; pitch: number };
  smile: number;
  boredom: number;
}

export function createIdleBehavior(nowMs = 0, seed = 1): IdleBehaviorState {
  return {
    seed,
    idleSinceMs: -1,
    lastMs: nowMs,
    nextFidgetMs: 0,
    nextSighMs: 0,
    nextShiftMs: nowMs + IDLE_TIMING.shiftMinMs,
    nextSaccadeMs: nowMs,
    nextGlanceMs: nowMs + IDLE_TIMING.glanceMinMs,
    glanceUntilMs: -1,
    lastYawnMs: -Infinity,
    weightTarget: 1,
    weight: 1,
    straighten: 0,
    gazeYaw: 0,
    gazePitch: 0,
    smile: 0,
    typing: false,
    fidgetIndex: 0,
    sighIndex: 0,
  };
}

/** Deterministic 0..1 noise so frozen-clock strips are reproducible. */
function rand(state: IdleBehaviorState): number {
  state.seed = (state.seed * 16807) % 2147483647;
  return (state.seed - 1) / 2147483646;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function clamp01(x: number): number {
  return x <= 0 ? 0 : x >= 1 ? 1 : x;
}

export function boredomFor(idleMs: number): number {
  return clamp01((idleMs - IDLE_TIMING.boredFromMs) / (IDLE_TIMING.boredFullMs - IDLE_TIMING.boredFromMs));
}

function jitter(state: IdleBehaviorState, base: number): number {
  return base * (0.7 + 0.6 * rand(state));
}

export function advanceIdleBehavior(
  state: IdleBehaviorState,
  nowMs: number,
  input: IdleBehaviorInput,
): IdleBehaviorOutput {
  const dt = Math.max(0, Math.min(100, nowMs - state.lastMs));
  state.lastMs = nowMs;
  const out: IdleBehaviorOutput = {
    fidget: null,
    headCue: null,
    attention: false,
    weight: state.weight,
    straighten: state.straighten,
    gaze: { yaw: 0, pitch: 0 },
    smile: 0,
    boredom: 0,
  };

  const typingEdge = input.typing && !state.typing;
  state.typing = input.typing;
  if (typingEdge) {
    out.attention = true;
    // Typing resets boredom: she has company again.
    state.idleSinceMs = nowMs;
  }

  if (!input.idle) {
    state.idleSinceMs = -1;
  } else if (state.idleSinceMs < 0) {
    state.idleSinceMs = nowMs;
    state.nextFidgetMs = nowMs + IDLE_TIMING.firstFidgetMs + rand(state) * 4000;
    state.nextSighMs = nowMs + jitter(state, IDLE_TIMING.sighFreshMs);
  }

  const idleMs = input.idle ? nowMs - state.idleSinceMs + (input.extraIdleMs ?? 0) : 0;
  const bored = input.idle && !input.typing ? boredomFor(idleMs) : 0;
  out.boredom = bored;

  // Weight shift (only while idle and not attentive).
  if (input.idle && !input.typing && nowMs >= state.nextShiftMs) {
    state.weightTarget = -state.weightTarget;
    state.nextShiftMs =
      nowMs + lerp(IDLE_TIMING.shiftMinMs, IDLE_TIMING.shiftMaxMs, rand(state)) * (1 - 0.3 * bored);
  }
  if (input.typing) {
    state.weightTarget = state.weightTarget >= 0 ? 1 : -1;
  }
  const k = 1 - Math.exp(-dt / IDLE_TIMING.shiftTauMs);
  state.weight += (state.weightTarget - state.weight) * k;
  const straightGoal = input.typing ? 1 : 0;
  state.straighten += (straightGoal - state.straighten) * (1 - Math.exp(-dt / 350));
  out.weight = state.weight;
  out.straighten = state.straighten;

  // Gaze: saccades, wandering with boredom, glances back to camera with a smile.
  if (input.idle && !input.typing) {
    if (nowMs >= state.nextSaccadeMs) {
      const size = lerp(GAZE.saccadeFresh, GAZE.saccadeBored, bored);
      state.gazeYaw = (rand(state) * 2 - 1) * size;
      state.gazePitch = (rand(state) * 2 - 1) * size * GAZE.pitchShare + size * 0.25;
      state.nextSaccadeMs =
        nowMs +
        lerp(IDLE_TIMING.saccadeMinMs, IDLE_TIMING.saccadeMaxMs, rand(state)) * (1 - 0.3 * bored);
    }
    if (nowMs >= state.nextGlanceMs) {
      state.glanceUntilMs = nowMs + IDLE_TIMING.glanceMs;
      state.nextGlanceMs =
        nowMs + lerp(IDLE_TIMING.glanceMinMs, IDLE_TIMING.glanceMaxMs, rand(state)) * (1 + bored);
    }
    const glancing = nowMs < state.glanceUntilMs;
    out.gaze.yaw = glancing ? 0 : state.gazeYaw;
    out.gaze.pitch = glancing ? 0 : state.gazePitch;
    out.smile = glancing ? GAZE.glanceSmile * (1 - 0.5 * bored) : 0;
  } else if (input.typing) {
    out.smile = GAZE.typingSmile;
  }
  const smileK = 1 - Math.exp(-dt / 250);
  state.smile += (out.smile - state.smile) * smileK;
  out.smile = state.smile;

  if (!input.idle || input.typing) {
    return out;
  }

  // Fidgets.
  if (input.armFree && nowMs >= state.nextFidgetMs) {
    const canYawn =
      idleMs >= IDLE_TIMING.yawnAfterMs && nowMs - state.lastYawnMs >= IDLE_TIMING.yawnGapMs;
    if (canYawn && rand(state) < 0.6) {
      out.fidget = "yawn";
      state.lastYawnMs = nowMs;
    } else {
      out.fidget = IDLE_FIDGETS[state.fidgetIndex % IDLE_FIDGETS.length] ?? null;
      state.fidgetIndex += 1 + Math.floor(rand(state) * 2);
    }
    state.nextFidgetMs =
      nowMs + jitter(state, lerp(IDLE_TIMING.fidgetFreshMs, IDLE_TIMING.fidgetBoredMs, bored));
  }
  if (input.headFree && nowMs >= state.nextSighMs) {
    out.headCue = state.sighIndex % 2 === 0 ? "sigh" : "neckStretch";
    state.sighIndex += 1;
    state.nextSighMs = nowMs + jitter(state, lerp(IDLE_TIMING.sighFreshMs, IDLE_TIMING.sighBoredMs, bored));
  }
  return out;
}
