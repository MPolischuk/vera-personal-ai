import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName, VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";
import {
  blinkWeightWithHappy,
  DOUBLE_BLINK_GAP_MS,
  isDoubleBlink,
  shouldLeadBlink,
  stretchedBlinkGap,
} from "./lookAt";

export interface IdleMotionClock {
  /** Milliseconds since an arbitrary epoch (injectable for tests). */
  nowMs: () => number;
}

export interface IdleMotionState {
  nextBlinkAtMs: number;
  blinkActive: boolean;
  blinkStartMs: number;
  hasBlink: boolean | null;
  breathBone: Object3D | null;
  breathBaseX: number;
  warnedMissingBlink: boolean;
  /** Start of the latest blink. -1 until the first one. Motion v2 lead uses this. */
  lastBlinkStartMs: number;
  /** Completed blinks. Every sixth schedules a double. */
  blinkCount: number;
}

const BLINK_MIN_GAP_MS = 3_000;
const BLINK_MAX_GAP_MS = 6_000;
const BLINK_DURATION_MS = 140;
const BREATH_AMPLITUDE = 0.02;
const BREATH_PERIOD_MS = 4_200;

function nextBlinkDelayMs(seed: number): number {
  const t = (Math.sin(seed * 12.9898) * 43758.5453) % 1;
  const unit = t < 0 ? t + 1 : t;
  return BLINK_MIN_GAP_MS + unit * (BLINK_MAX_GAP_MS - BLINK_MIN_GAP_MS);
}

export function createIdleMotionState(
  clock: IdleMotionClock = { nowMs: () => performance.now() },
): IdleMotionState {
  const now = clock.nowMs();
  return {
    nextBlinkAtMs: now + nextBlinkDelayMs(now),
    blinkActive: false,
    blinkStartMs: 0,
    hasBlink: null,
    breathBone: null,
    breathBaseX: 0,
    warnedMissingBlink: false,
    lastBlinkStartMs: -1,
    blinkCount: 0,
  };
}

function ensureBreathBone(vrm: VRM, state: IdleMotionState): void {
  if (state.breathBone) {
    return;
  }
  const bone =
    vrm.humanoid?.getNormalizedBoneNode(VRMHumanBoneName.Chest) ??
    vrm.humanoid?.getNormalizedBoneNode(VRMHumanBoneName.Spine) ??
    null;
  if (!bone) {
    return;
  }
  state.breathBone = bone;
  state.breathBaseX = bone.rotation.x;
}

function ensureBlink(vrm: VRM, state: IdleMotionState): boolean {
  if (state.hasBlink !== null) {
    return state.hasBlink;
  }
  const manager = vrm.expressionManager;
  if (!manager) {
    state.hasBlink = false;
    return false;
  }
  const expression = manager.getExpression(VRMExpressionPresetName.Blink);
  state.hasBlink = expression !== null;
  if (!state.hasBlink && !state.warnedMissingBlink) {
    state.warnedMissingBlink = true;
    console.info("[avatar] blink expression missing; continuing with breath only");
  }
  return state.hasBlink;
}

/**
 * Pure-ish blink weight for a given progress in [0, 1] through the blink.
 * Goes 0 → 1 → 0.
 */
export function blinkWeightAt(progress: number): number {
  const p = Math.min(1, Math.max(0, progress));
  if (p <= 0.5) {
    return p * 2;
  }
  return (1 - p) * 2;
}

/**
 * Chest pitch offset from a sine wave (radians).
 */
export function breathOffsetAt(nowMs: number, periodMs = BREATH_PERIOD_MS): number {
  return Math.sin((nowMs / periodMs) * Math.PI * 2) * BREATH_AMPLITUDE;
}

/** Motion v2 blink policy. Absent, the blink stays the legacy one. */
export interface BlinkV2Options {
  /** Published director state changed on this tick, after hysteresis. */
  stateChanged: boolean;
  /** Happy expression weight, 0..1. */
  happy: number;
  listening: boolean;
}

export interface UpdateIdleMotionOptions {
  /** When true, skip chest breath so Mixamo Chest tracks are not overwritten (D6). */
  omitBreath?: boolean;
  /** Speech bob added by the chest owner. Legacy frames omit it. */
  speechChest?: number;
  blinkV2?: BlinkV2Options;
}

/**
 * Advances procedural idle (blink + breath). Does not write viseme expressions.
 */
export function updateIdleMotion(
  vrm: VRM,
  state: IdleMotionState,
  clock: IdleMotionClock = { nowMs: () => performance.now() },
  options: UpdateIdleMotionOptions = {},
): void {
  const now = clock.nowMs();
  const manager = vrm.expressionManager;

  if (manager && ensureBlink(vrm, state)) {
    const policy = options.blinkV2;
    if (
      policy &&
      shouldLeadBlink(policy.stateChanged, now, state.lastBlinkStartMs, state.blinkActive)
    ) {
      state.blinkActive = true;
      state.blinkStartMs = now;
      state.lastBlinkStartMs = now;
    }

    if (!state.blinkActive && now >= state.nextBlinkAtMs) {
      state.blinkActive = true;
      state.blinkStartMs = now;
      state.lastBlinkStartMs = now;
    }

    if (state.blinkActive) {
      const progress = (now - state.blinkStartMs) / BLINK_DURATION_MS;
      if (progress >= 1) {
        manager.setValue(VRMExpressionPresetName.Blink, 0);
        state.blinkActive = false;
        let gap = nextBlinkDelayMs(now);
        if (policy) {
          state.blinkCount += 1;
          gap = isDoubleBlink(state.blinkCount)
            ? DOUBLE_BLINK_GAP_MS
            : stretchedBlinkGap(gap, policy.listening);
        }
        state.nextBlinkAtMs = now + gap;
      } else {
        const neutral = blinkWeightAt(progress);
        const weight = policy ? blinkWeightWithHappy(neutral, policy.happy) : neutral;
        manager.setValue(VRMExpressionPresetName.Blink, weight);
      }
    }
  }

  if (options.omitBreath) {
    return;
  }

  ensureBreathBone(vrm, state);
  if (state.breathBone) {
    const pulse = options.speechChest ?? 0;
    state.breathBone.rotation.x = state.breathBaseX + breathOffsetAt(now) + pulse;
  }
}
