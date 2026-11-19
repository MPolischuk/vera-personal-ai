import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import { Object3D, Vector3 } from "three";

export type LookDirectorState = "idle" | "listening" | "thinking" | "speaking";

export type LookAim = "camera" | "aside" | "custom";

export interface LookAngles {
  yaw: number;
  pitch: number;
}

export interface GazeClock {
  speakingSinceMs: number;
  asideFromMs: number;
  asideUntilMs: number;
}

export interface LookAtState {
  /** Set by `setLookTarget`. Null follows the state. */
  custom: Object3D | null;
  /** Head only. Eyes use the raw aim and are not smoothed with this. */
  smoothed: LookAngles;
  lastMs: number;
  gaze: GazeClock;
  asideTarget: Object3D;
  /** Idle saccade / wander offset added to a camera aim. Eyes take all, head `HEAD_FOLLOW`. */
  gazeOffset?: LookAngles | null;
}

export const LOOK_TAU_MS = 250;
/** Share of an idle gaze offset the head follows (the eyes lead). */
export const GAZE_HEAD_FOLLOW = 0.3;
export const LOOK_NECK_SHARE = 0.4;
export const LOOK_HEAD_SHARE = 0.6;
export const THINK_SACCADE_RAD = 0.15;
/** How long a thinking glance holds one side before flipping. */
export const THINK_SACCADE_HOLD_MS = 2_400;
export const SPEAK_ASIDE_AFTER_MS = 8_000;
export const BLINK_LEAD_GUARD_MS = 200;
export const DOUBLE_BLINK_GAP_MS = 80;
export const LISTENING_BLINK_STRETCH = 1.3;

const _head = new Vector3();
const _target = new Vector3();

function clamp(value: number, min: number, max: number): number {
  if (value < min) {
    return min;
  }
  if (value > max) {
    return max;
  }
  return value;
}

/** `clamp(atan2, ±1) * 0.8`. */
export function scaleLookYaw(atan2Yaw: number): number {
  return clamp(atan2Yaw, -1, 1) * 0.8;
}

/** `clamp(atan2, ±0.5) * 0.7`. */
export function scaleLookPitch(atan2Pitch: number): number {
  return clamp(atan2Pitch, -0.5, 0.5) * 0.7;
}

export function splitLook(angles: LookAngles): { neck: LookAngles; head: LookAngles } {
  return {
    neck: {
      yaw: angles.yaw * LOOK_NECK_SHARE,
      pitch: angles.pitch * LOOK_NECK_SHARE,
    },
    head: {
      yaw: angles.yaw * LOOK_HEAD_SHARE,
      pitch: angles.pitch * LOOK_HEAD_SHARE,
    },
  };
}

/** Head settle. Do not use this on the eyes. */
export function smoothLook(
  current: LookAngles,
  target: LookAngles,
  dtMs: number,
  tauMs = LOOK_TAU_MS,
): LookAngles {
  if (dtMs <= 0 || tauMs <= 0) {
    return { yaw: current.yaw, pitch: current.pitch };
  }
  const alpha = 1 - Math.exp(-dtMs / tauMs);
  return {
    yaw: current.yaw + (target.yaw - current.yaw) * alpha,
    pitch: current.pitch + (target.pitch - current.pitch) * alpha,
  };
}

/** Up and to the side, in 0.15 rad saccades. Not the camera. */
export function thinkingSaccade(nowMs: number): LookAngles {
  const side = Math.floor(nowMs / THINK_SACCADE_HOLD_MS) % 2 === 0 ? 1 : -1;
  return { yaw: side * THINK_SACCADE_RAD, pitch: -THINK_SACCADE_RAD };
}

function asideDurationMs(seed: number): number {
  const t = (Math.sin(seed * 12.9898) * 43758.5453) % 1;
  const unit = t < 0 ? t + 1 : t;
  return 400 + unit * 400;
}

export function createGazeClock(): GazeClock {
  return { speakingSinceMs: -1, asideFromMs: -1, asideUntilMs: -1 };
}

/**
 * After 8 s of continuous speaking, look away for 0.4–0.8 s, then return.
 * Leaving `speaking` clears the stretch.
 */
export function advanceSpeakingAside(
  clock: GazeClock,
  state: LookDirectorState,
  nowMs: number,
): boolean {
  if (state !== "speaking") {
    clock.speakingSinceMs = -1;
    clock.asideFromMs = -1;
    clock.asideUntilMs = -1;
    return false;
  }
  if (clock.speakingSinceMs < 0) {
    clock.speakingSinceMs = nowMs;
  }
  if (clock.asideFromMs < 0 && nowMs - clock.speakingSinceMs >= SPEAK_ASIDE_AFTER_MS) {
    clock.asideFromMs = nowMs;
    clock.asideUntilMs = nowMs + asideDurationMs(nowMs);
  }
  return clock.asideFromMs >= 0 && nowMs < clock.asideUntilMs;
}

export function resolveLookAim(
  state: LookDirectorState,
  hasCustom: boolean,
  speakingAside: boolean,
): LookAim {
  if (hasCustom) {
    return "custom";
  }
  if (state === "thinking" || speakingAside) {
    return "aside";
  }
  return "camera";
}

export function createLookAtState(): LookAtState {
  const asideTarget = new Object3D();
  asideTarget.name = "look-aside";
  return {
    custom: null,
    smoothed: { yaw: 0, pitch: 0 },
    lastMs: -1,
    gaze: createGazeClock(),
    asideTarget,
  };
}

/** Hook for a later point. Nothing calls this yet. */
export function setLookTarget(state: LookAtState, target: Object3D | null): void {
  state.custom = target;
}

export function shouldLeadBlink(
  stateChanged: boolean,
  nowMs: number,
  lastBlinkStartMs: number,
  blinkActive: boolean,
): boolean {
  if (!stateChanged || blinkActive) {
    return false;
  }
  if (lastBlinkStartMs < 0) {
    return true;
  }
  return nowMs - lastBlinkStartMs >= BLINK_LEAD_GUARD_MS;
}

/** `neutral * (1 - 0.8 * happy)`. happy = 1 leaves a fifth of the blink. */
export function blinkWeightWithHappy(neutral: number, happy: number): number {
  const h = happy < 0 ? 0 : happy > 1 ? 1 : happy;
  return neutral * (1 - 0.8 * h);
}

export function stretchedBlinkGap(baseMs: number, listening: boolean): number {
  return listening ? baseMs * LISTENING_BLINK_STRETCH : baseMs;
}

/** One completed blink out of every six is the first half of a double. */
export function isDoubleBlink(completedCount: number): boolean {
  return completedCount > 0 && completedCount % 6 === 0;
}

function bone(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

function desiredLook(
  vrm: VRM,
  state: LookAtState,
  aim: LookAim,
  directorState: LookDirectorState,
  camera: Object3D,
  nowMs: number,
): LookAngles {
  if (aim === "aside") {
    if (directorState === "thinking") {
      return thinkingSaccade(nowMs);
    }
    return { yaw: THINK_SACCADE_RAD, pitch: 0 };
  }
  const target = aim === "custom" && state.custom ? state.custom : camera;
  const head = bone(vrm, VRMHumanBoneName.Head);
  if (!head) {
    return { yaw: 0, pitch: 0 };
  }
  head.getWorldPosition(_head);
  target.getWorldPosition(_target);
  const dx = _target.x - _head.x;
  const dy = _target.y - _head.y;
  const dz = _target.z - _head.z;
  return {
    yaw: scaleLookYaw(Math.atan2(dx, dz)),
    pitch: scaleLookPitch(Math.atan2(-dy, Math.hypot(dx, dz))),
  };
}

function placeAside(dummy: Object3D, origin: Object3D, angles: LookAngles): void {
  origin.getWorldPosition(_head);
  const dist = 1.5;
  const horiz = Math.cos(angles.pitch);
  dummy.position.set(
    _head.x + Math.sin(angles.yaw) * horiz * dist,
    _head.y - Math.sin(angles.pitch) * dist,
    _head.z + Math.cos(angles.yaw) * horiz * dist,
  );
  dummy.updateMatrixWorld(true);
}

function addLookBones(vrm: VRM, parts: { neck: LookAngles; head: LookAngles }): void {
  const head = bone(vrm, VRMHumanBoneName.Head);
  const neck = bone(vrm, VRMHumanBoneName.Neck);
  if (head) {
    head.rotation.x += parts.head.pitch;
    head.rotation.y += parts.head.yaw;
  }
  if (neck) {
    neck.rotation.x += parts.neck.pitch;
    neck.rotation.y += parts.neck.yaw;
  }
}

/**
 * Eyes snap to the aim. The head eases with τ = 250 ms, then splits
 * neck 0.4 / head 0.6. A missing `vrm.lookAt` skips the eyes only.
 */
export function updateLookAt(
  vrm: VRM,
  state: LookAtState,
  directorState: LookDirectorState,
  camera: Object3D,
  nowMs: number,
): void {
  const aside = advanceSpeakingAside(state.gaze, directorState, nowMs);
  const aim = resolveLookAim(directorState, state.custom !== null, aside);
  const desired = desiredLook(vrm, state, aim, directorState, camera, nowMs);
  const offset = aim === "camera" ? state.gazeOffset : null;
  const offsetOn = Boolean(offset && Math.abs(offset.yaw) + Math.abs(offset.pitch) > 1e-3);
  const headDesired = offsetOn && offset
    ? { yaw: desired.yaw + offset.yaw * GAZE_HEAD_FOLLOW, pitch: desired.pitch + offset.pitch * GAZE_HEAD_FOLLOW }
    : desired;
  const dt = state.lastMs < 0 ? 0 : Math.max(0, nowMs - state.lastMs);
  state.lastMs = nowMs;
  state.smoothed = smoothLook(state.smoothed, headDesired, dt);
  addLookBones(vrm, splitLook(state.smoothed));

  if (!vrm.lookAt) {
    return;
  }
  if (aim === "camera" && offsetOn && offset) {
    const head = bone(vrm, VRMHumanBoneName.Head) ?? camera;
    head.getWorldPosition(_head);
    camera.getWorldPosition(_target);
    const dx = _target.x - _head.x;
    const dy = _target.y - _head.y;
    const dz = _target.z - _head.z;
    placeAside(state.asideTarget, head, {
      yaw: Math.atan2(dx, dz) + offset.yaw,
      pitch: Math.atan2(-dy, Math.hypot(dx, dz)) + offset.pitch,
    });
    vrm.lookAt.target = state.asideTarget;
    return;
  }
  if (aim === "camera") {
    vrm.lookAt.target = camera;
    return;
  }
  if (aim === "custom" && state.custom) {
    vrm.lookAt.target = state.custom;
    return;
  }
  const origin = bone(vrm, VRMHumanBoneName.Head) ?? camera;
  placeAside(state.asideTarget, origin, desired);
  vrm.lookAt.target = state.asideTarget;
}
