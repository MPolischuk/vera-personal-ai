import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Camera, Object3D } from "three";
import { Object3D as ThreeObject3D, Vector3 } from "three";
import type { CameraShot } from "./cameraShot";

/**
 * Procedural waiting extras on top of blink/breath (PT-34).
 * Additive after restPose, before gesture. Does not write Chest/Spine (breath owns chest).
 */

export type IdleLifeAction = "glance" | "headTilt";

export type IdleLifeVisualState = "idle" | "listening" | "thinking" | "speaking";

/** Peak radians at intensity 1, bust framing (D4/D7). */
export const IDLE_LIFE_PEAK = {
  glanceYaw: 0.12,
  headTiltRoll: 0.1,
  swayYaw: 0.025,
  swayRoll: 0.015,
} as const;

export const IDLE_LIFE_DURATION_MS = {
  glance: 1_400,
  headTilt: 1_400,
} as const;

const SMALL_GAP_MIN_MS = 14_000;
const SMALL_GAP_MAX_MS = 28_000;
const SWAY_PERIOD_MS = 5_200;

const HEAD_SHARE = 0.7;
const NECK_SHARE = 0.45;

const SMALL_ACTIONS: readonly IdleLifeAction[] = ["glance", "headTilt"];

const ARM_GAIN: Record<CameraShot, number> = {
  bust: 1.0,
  waist: 1.45,
  full: 1.6,
};

const _right = new Vector3();
const _up = new Vector3();
const _forward = new Vector3();
const _dummyPos = new Vector3();

export interface IdleLifeHeadOffset {
  pitch: number;
  yaw: number;
  roll: number;
}

export interface IdleLifeArmOffset {
  leftUpperZ: number;
  rightUpperZ: number;
  leftLowerX: number;
  rightLowerX: number;
}

export interface IdleLifeShoulderOffset {
  leftZ: number;
  rightZ: number;
}

export interface IdleLifeState {
  active: IdleLifeAction | null;
  startMs: number;
  durationMs: number;
  nextActionAtMs: number;
  /** +1 / −1 for glance yaw and head-tilt side. */
  actionSign: 1 | -1;
  lookAtDummy: Object3D | null;
  warnedMissing: Set<string>;
}

export interface AdvanceIdleLifeOptions {
  visualState: IdleLifeVisualState;
  shot: CameraShot;
  suppressed: boolean;
}

export interface UpdateIdleLifeOptions extends AdvanceIdleLifeOptions {
  camera: Camera;
}

export interface IdleLifeFrame {
  head: IdleLifeHeadOffset;
  arms: IdleLifeArmOffset;
  shoulders: IdleLifeShoulderOffset;
}

const ZERO_HEAD: IdleLifeHeadOffset = { pitch: 0, yaw: 0, roll: 0 };
const ZERO_ARMS: IdleLifeArmOffset = {
  leftUpperZ: 0,
  rightUpperZ: 0,
  leftLowerX: 0,
  rightLowerX: 0,
};
const ZERO_SHOULDERS: IdleLifeShoulderOffset = { leftZ: 0, rightZ: 0 };

function clamp01(progress: number): number {
  if (progress <= 0) {
    return 0;
  }
  if (progress >= 1) {
    return 1;
  }
  return progress;
}

/** sin(2πp); 0 at progress 0 and 1. */
function cycleEnvelope(progress: number): number {
  const p = clamp01(progress);
  if (p === 0 || p === 1) {
    return 0;
  }
  return Math.sin(p * Math.PI * 2);
}

function unitFromSeed(seed: number): number {
  const t = (Math.sin(seed * 12.9898) * 43758.5453) % 1;
  return t < 0 ? t + 1 : t;
}

function nextSmallGapMs(seed: number): number {
  return SMALL_GAP_MIN_MS + unitFromSeed(seed) * (SMALL_GAP_MAX_MS - SMALL_GAP_MIN_MS);
}

function pickSign(seed: number): 1 | -1 {
  return unitFromSeed(seed) < 0.5 ? -1 : 1;
}

function pickSmallAction(seed: number): IdleLifeAction {
  const index = Math.floor(unitFromSeed(seed) * SMALL_ACTIONS.length) % SMALL_ACTIONS.length;
  return SMALL_ACTIONS[index]!;
}

export function idleLifeArmGain(shot: CameraShot): number {
  return ARM_GAIN[shot];
}

export function idleLifeHeadOffsetAt(
  action: IdleLifeAction,
  progress: number,
  sign: 1 | -1 = 1,
): IdleLifeHeadOffset {
  const envelope = cycleEnvelope(progress);
  if (envelope === 0) {
    return { ...ZERO_HEAD };
  }
  if (action === "glance") {
    return { pitch: 0, yaw: envelope * IDLE_LIFE_PEAK.glanceYaw * sign, roll: 0 };
  }
  if (action === "headTilt") {
    return { pitch: 0, yaw: 0, roll: envelope * IDLE_LIFE_PEAK.headTiltRoll * sign };
  }
  return { ...ZERO_HEAD };
}

export function idleLifeArmOffsetAt(
  _action: IdleLifeAction,
  _progress: number,
  _sign: 1 | -1 = 1,
): IdleLifeArmOffset {
  return { ...ZERO_ARMS };
}

export function idleLifeShoulderOffsetAt(
  _action: IdleLifeAction,
  _progress: number,
): IdleLifeShoulderOffset {
  return { ...ZERO_SHOULDERS };
}

function swayOffsetAt(nowMs: number): IdleLifeHeadOffset {
  const phase = (nowMs / SWAY_PERIOD_MS) * Math.PI * 2;
  return {
    pitch: 0,
    yaw: Math.sin(phase) * IDLE_LIFE_PEAK.swayYaw,
    roll: Math.sin(phase * 0.85 + 0.7) * IDLE_LIFE_PEAK.swayRoll,
  };
}

export function createIdleLifeState(nowMs = 0): IdleLifeState {
  return {
    active: null,
    startMs: 0,
    durationMs: 0,
    nextActionAtMs: nowMs + nextSmallGapMs(nowMs + 1),
    actionSign: 1,
    lookAtDummy: null,
    warnedMissing: new Set(),
  };
}

function clearActive(state: IdleLifeState): void {
  state.active = null;
  state.durationMs = 0;
}

function startAction(state: IdleLifeState, action: IdleLifeAction, nowMs: number): void {
  state.active = action;
  state.startMs = nowMs;
  state.durationMs = IDLE_LIFE_DURATION_MS[action];
  state.actionSign = pickSign(nowMs + action.length);
  state.nextActionAtMs = nowMs + state.durationMs + nextSmallGapMs(nowMs + 17);
}

function maybeStartAction(
  state: IdleLifeState,
  nowMs: number,
  opts: AdvanceIdleLifeOptions,
): void {
  if (opts.suppressed || opts.visualState === "thinking" || opts.visualState === "speaking") {
    return;
  }
  if (opts.visualState !== "idle" && opts.visualState !== "listening") {
    return;
  }
  if (state.active !== null) {
    return;
  }
  if (nowMs < state.nextActionAtMs) {
    return;
  }

  startAction(state, pickSmallAction(nowMs + 5), nowMs);
}

/**
 * Advances the waiting scheduler and returns additive extras for this frame.
 * When suppressed, extras are 0 the same frame and no discrete action starts (D8).
 */
export function advanceIdleLife(
  state: IdleLifeState,
  nowMs: number,
  opts: AdvanceIdleLifeOptions,
): IdleLifeFrame {
  if (opts.suppressed) {
    clearActive(state);
    return { head: { ...ZERO_HEAD }, arms: { ...ZERO_ARMS }, shoulders: { ...ZERO_SHOULDERS } };
  }

  if (state.active !== null && state.durationMs > 0) {
    const progress = (nowMs - state.startMs) / state.durationMs;
    if (progress >= 1) {
      clearActive(state);
    }
  }

  maybeStartAction(state, nowMs, opts);

  const thinkingOnly = opts.visualState === "thinking";
  const fullWait =
    opts.visualState === "idle" || opts.visualState === "listening";

  if (!fullWait && !thinkingOnly) {
    clearActive(state);
    return { head: { ...ZERO_HEAD }, arms: { ...ZERO_ARMS }, shoulders: { ...ZERO_SHOULDERS } };
  }

  const sway = swayOffsetAt(nowMs);
  let head: IdleLifeHeadOffset = { ...sway };

  if (fullWait && state.active !== null && state.durationMs > 0) {
    const progress = (nowMs - state.startMs) / state.durationMs;
    const actionHead = idleLifeHeadOffsetAt(state.active, progress, state.actionSign);
    head = {
      pitch: head.pitch + actionHead.pitch,
      yaw: head.yaw + actionHead.yaw,
      roll: head.roll + actionHead.roll,
    };
  }

  return { head, arms: { ...ZERO_ARMS }, shoulders: { ...ZERO_SHOULDERS } };
}

function warnOnce(state: IdleLifeState, key: string, message: string): void {
  if (state.warnedMissing.has(key)) {
    return;
  }
  state.warnedMissing.add(key);
  console.info(message);
}

function bone(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

function ensureLookAtDummy(state: IdleLifeState): Object3D {
  if (!state.lookAtDummy) {
    state.lookAtDummy = new ThreeObject3D();
  }
  return state.lookAtDummy;
}

function updateLookAtTarget(
  vrm: VRM,
  state: IdleLifeState,
  nowMs: number,
  opts: UpdateIdleLifeOptions,
  frame: IdleLifeFrame,
): void {
  if (!vrm.lookAt) {
    return;
  }

  const restore =
    opts.suppressed ||
    opts.visualState === "thinking" ||
    opts.visualState === "speaking" ||
    (opts.visualState !== "idle" && opts.visualState !== "listening");

  if (restore) {
    vrm.lookAt.target = opts.camera;
    return;
  }

  const dummy = ensureLookAtDummy(state);
  const camera = opts.camera;
  camera.getWorldDirection(_forward);
  _right.set(1, 0, 0).applyQuaternion(camera.quaternion).normalize();
  _up.set(0, 1, 0).applyQuaternion(camera.quaternion).normalize();

  const glanceYaw =
    state.active === "glance"
      ? frame.head.yaw / Math.max(IDLE_LIFE_PEAK.glanceYaw, 1e-6)
      : 0;
  const lateral = 0.22 * Math.max(-1, Math.min(1, glanceYaw || Math.sin(nowMs / 9_000) * 0.15));
  const lift = 0.08;

  _dummyPos
    .copy(camera.position)
    .addScaledVector(_forward, 1.35)
    .addScaledVector(_right, lateral)
    .addScaledVector(_up, lift);
  dummy.position.copy(_dummyPos);
  dummy.updateMatrixWorld(true);
  vrm.lookAt.target = dummy;
}

function applyHead(vrm: VRM, state: IdleLifeState, offset: IdleLifeHeadOffset): void {
  if (offset.pitch === 0 && offset.yaw === 0 && offset.roll === 0) {
    return;
  }
  const head = bone(vrm, VRMHumanBoneName.Head);
  const neck = bone(vrm, VRMHumanBoneName.Neck);
  if (!head && !neck) {
    warnOnce(state, "head-neck", "[avatar] idle life head/neck bones missing; skipping");
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
 * Adds waiting extras after restPose. Restores lookAt.target to the camera when suppressed.
 * Does not write arm or shoulder bones (those extras caused an unnatural arc on this VRM).
 */
export function updateIdleLife(
  vrm: VRM,
  state: IdleLifeState,
  nowMs: number,
  opts: UpdateIdleLifeOptions,
): void {
  const frame = advanceIdleLife(state, nowMs, opts);
  applyHead(vrm, state, frame.head);
  updateLookAtTarget(vrm, state, nowMs, opts, frame);
}
