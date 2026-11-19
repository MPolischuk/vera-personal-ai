import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";

export const AVATAR_GESTURE_WHITELIST = [
  "nod",
  "shake",
  "wave",
  "shrug",
] as const;

export type AvatarGestureName = (typeof AVATAR_GESTURE_WHITELIST)[number];

export interface AvatarGestureCue {
  name: string;
  intensity: number;
  durationMs: number;
  playId?: string;
}

export interface GestureMotionState {
  active: AvatarGestureName | null;
  intensity: number;
  startMs: number;
  durationMs: number;
  warnedMissing: Set<string>;
  waveFallbackHead: boolean;
}

/** Matches AvatarCues.CharacterGestureDurationMs. */
export const CHARACTER_GESTURE_DURATION_MS = 1100;

/** Matches AvatarCues.CharacterGestureIntensity. */
export const CHARACTER_GESTURE_INTENSITY = 0.7;

/** Peak radians at intensity=1 (D4). */
export const GESTURE_PEAK = {
  nodPitch: 0.4,
  shakeYaw: 0.45,
  waveRoll: 0.9,
  waveHeadYaw: 0.55,
  shrugPitch: 0.35,
} as const;

const HEAD_SHARE = 0.7;
const NECK_SHARE = 0.45;

export function isAvatarGestureName(value: string): value is AvatarGestureName {
  return (AVATAR_GESTURE_WHITELIST as readonly string[]).includes(value);
}

export function createGestureMotionState(): GestureMotionState {
  return {
    active: null,
    intensity: 0,
    startMs: 0,
    durationMs: 0,
    warnedMissing: new Set(),
    waveFallbackHead: false,
  };
}

export function beginGestureCue(
  state: GestureMotionState,
  cue: AvatarGestureCue | null,
  nowMs: number,
): void {
  if (!cue || !isAvatarGestureName(cue.name)) {
    state.active = null;
    state.intensity = 0;
    state.durationMs = 0;
    state.waveFallbackHead = false;
    return;
  }

  state.active = cue.name;
  state.intensity = cue.intensity;
  state.startMs = nowMs;
  state.durationMs = Math.max(0, cue.durationMs);
  state.waveFallbackHead = false;
}

export function isGestureMotionActive(
  state: GestureMotionState,
  nowMs: number,
): boolean {
  return (
    state.active !== null &&
    state.durationMs > 0 &&
    nowMs - state.startMs < state.durationMs
  );
}

/** One head cycle; extra is 0 at progress 0 and 1. */
export function gestureHeadOffsetAt(
  gesture: "nod" | "shake",
  progress: number,
  intensity: number,
): { pitch: number; yaw: number; roll: number } {
  const p = clamp01(progress);
  if (p === 0 || p === 1) {
    return { pitch: 0, yaw: 0, roll: 0 };
  }
  const envelope = Math.sin(p * Math.PI * 2);
  if (gesture === "nod") {
    return { pitch: envelope * GESTURE_PEAK.nodPitch * intensity, yaw: 0, roll: 0 };
  }
  return { pitch: 0, yaw: envelope * GESTURE_PEAK.shakeYaw * intensity, roll: 0 };
}

/** One arm (or head fallback) cycle. */
export function gestureArmOffsetAt(
  progress: number,
  intensity: number,
): { roll: number; headYaw: number } {
  const p = clamp01(progress);
  if (p === 0 || p === 1) {
    return { roll: 0, headYaw: 0 };
  }
  const envelope = Math.sin(p * Math.PI * 2);
  return {
    roll: envelope * GESTURE_PEAK.waveRoll * intensity,
    headYaw: envelope * GESTURE_PEAK.waveHeadYaw * intensity,
  };
}

/** One shrug cycle on shoulders. */
export function gestureShrugOffsetAt(progress: number, intensity: number): number {
  const p = clamp01(progress);
  if (p === 0 || p === 1) {
    return 0;
  }
  return Math.sin(p * Math.PI * 2) * GESTURE_PEAK.shrugPitch * intensity;
}

function clamp01(progress: number): number {
  if (progress <= 0) {
    return 0;
  }
  if (progress >= 1) {
    return 1;
  }
  return progress;
}

function warnOnce(state: GestureMotionState, key: string, message: string): void {
  if (state.warnedMissing.has(key)) {
    return;
  }
  state.warnedMissing.add(key);
  console.info(message);
}

function bone(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

function addHeadNeck(
  vrm: VRM,
  state: GestureMotionState,
  offset: { pitch: number; yaw: number; roll: number },
): void {
  const head = bone(vrm, VRMHumanBoneName.Head);
  const neck = bone(vrm, VRMHumanBoneName.Neck);
  if (!head && !neck) {
    warnOnce(state, "head-neck", "[avatar] gesture head/neck bones missing; skipping");
    return;
  }
  if (head) {
    head.rotation.x += offset.pitch * HEAD_SHARE;
    head.rotation.y += offset.yaw * HEAD_SHARE;
    head.rotation.z += offset.roll * HEAD_SHARE;
  }
  if (neck) {
    neck.rotation.x += offset.pitch * NECK_SHARE;
    neck.rotation.y += offset.yaw * NECK_SHARE;
    neck.rotation.z += offset.roll * NECK_SHARE;
  }
}

/**
 * Adds procedural gesture offsets after pose. Does not write Chest (breath owns it).
 * At progress ≥ 1 the extra is 0 (D6).
 */
export function updateGestureMotion(
  vrm: VRM,
  state: GestureMotionState,
  nowMs: number,
): void {
  if (!state.active || state.durationMs <= 0) {
    return;
  }

  const progress = (nowMs - state.startMs) / state.durationMs;
  if (progress >= 1) {
    return;
  }

  const intensity = state.intensity;

  if (state.active === "nod" || state.active === "shake") {
    addHeadNeck(vrm, state, gestureHeadOffsetAt(state.active, progress, intensity));
    return;
  }

  if (state.active === "shrug") {
    const pitch = gestureShrugOffsetAt(progress, intensity);
    const left = bone(vrm, VRMHumanBoneName.LeftShoulder);
    const right = bone(vrm, VRMHumanBoneName.RightShoulder);
    if (!left && !right) {
      warnOnce(state, "shoulders", "[avatar] gesture shoulder bones missing; skipping shrug");
      return;
    }
    if (left) {
      left.rotation.x += pitch;
    }
    if (right) {
      right.rotation.x += pitch;
    }
    return;
  }

  // wave
  const arm = gestureArmOffsetAt(progress, intensity);
  const lower = bone(vrm, VRMHumanBoneName.RightLowerArm);
  const upper = bone(vrm, VRMHumanBoneName.RightUpperArm);
  if (lower) {
    lower.rotation.z += arm.roll;
  } else if (upper) {
    upper.rotation.z += arm.roll;
  } else if (!state.waveFallbackHead) {
    state.waveFallbackHead = true;
    warnOnce(
      state,
      "wave-arm",
      "[avatar] wave arm bones missing; falling back to head tilt",
    );
  }

  // Bust framing (~1.5 m) crops most of the arm; always add a head yaw so wave reads.
  addHeadNeck(vrm, state, { pitch: 0, yaw: arm.headYaw, roll: 0 });
}
