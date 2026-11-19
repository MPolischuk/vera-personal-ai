import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";

export type AvatarPoseState = "idle" | "listening" | "thinking" | "speaking";

export interface HeadPoseOffset {
  /** Pitch (radians). Positive looks down. */
  pitch: number;
  /** Yaw (radians). */
  yaw: number;
  /** Roll (radians). */
  roll: number;
}

interface BonePose {
  bone: Object3D | null;
  baseX: number;
  baseY: number;
  baseZ: number;
  share: number;
}

export interface PoseMotionState {
  head: BonePose;
  neck: BonePose;
  current: HeadPoseOffset;
  target: HeadPoseOffset;
}

/**
 * Offsets sized to read at bust framing (~1.5 m). PT-18 D5: Head/Neck only, no chest.
 * G4 follow-up: first values (~6°) were not distinguishable from idle.
 */
const OFFSETS: Record<AvatarPoseState, HeadPoseOffset> = {
  idle: { pitch: 0, yaw: 0, roll: 0 },
  listening: { pitch: 0.16, yaw: 0.32, roll: -0.12 },
  thinking: { pitch: -0.32, yaw: -0.26, roll: 0.16 },
  speaking: { pitch: 0.14, yaw: 0.05, roll: 0 },
};

/** Exponential settle time constant τ (ms). Same wall-clock at any refresh rate. */
export const POSE_SETTLE_MS = 180;
const HEAD_SHARE = 0.7;
const NECK_SHARE = 0.45;

export function poseOffsetForState(state: AvatarPoseState): HeadPoseOffset {
  return OFFSETS[state];
}

function emptyBone(share: number): BonePose {
  return { bone: null, baseX: 0, baseY: 0, baseZ: 0, share };
}

export function createPoseMotionState(): PoseMotionState {
  return {
    head: emptyBone(HEAD_SHARE),
    neck: emptyBone(NECK_SHARE),
    current: { ...OFFSETS.idle },
    target: { ...OFFSETS.idle },
  };
}

function bindBone(
  vrm: VRM,
  slot: BonePose,
  name: VRMHumanBoneName,
): void {
  if (slot.bone) {
    return;
  }
  const bone = vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
  if (!bone) {
    return;
  }
  slot.bone = bone;
  slot.baseX = bone.rotation.x;
  slot.baseY = bone.rotation.y;
  slot.baseZ = bone.rotation.z;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

export function setPoseTarget(state: PoseMotionState, visualState: AvatarPoseState): void {
  state.target = { ...poseOffsetForState(visualState) };
}

/** Advances `current` toward `target` with a refresh-rate-independent factor. */
export function advancePoseMotion(state: PoseMotionState, deltaSeconds: number): void {
  const tauSeconds = POSE_SETTLE_MS / 1000;
  const t = 1 - Math.exp(-deltaSeconds / tauSeconds);
  state.current = {
    pitch: lerp(state.current.pitch, state.target.pitch, t),
    yaw: lerp(state.current.yaw, state.target.yaw, t),
    roll: lerp(state.current.roll, state.target.roll, t),
  };
}

function applyBone(slot: BonePose, offset: HeadPoseOffset): void {
  if (!slot.bone) {
    return;
  }
  slot.bone.rotation.x = slot.baseX + offset.pitch * slot.share;
  slot.bone.rotation.y = slot.baseY + offset.yaw * slot.share;
  slot.bone.rotation.z = slot.baseZ + offset.roll * slot.share;
}

/**
 * Applies a procedural head/neck offset. Does not write chest (breath owns it).
 */
export function updatePoseMotion(
  vrm: VRM,
  state: PoseMotionState,
  deltaSeconds: number,
): void {
  bindBone(vrm, state.head, VRMHumanBoneName.Head);
  bindBone(vrm, state.neck, VRMHumanBoneName.Neck);
  if (!state.head.bone && !state.neck.bone) {
    return;
  }

  advancePoseMotion(state, deltaSeconds);

  applyBone(state.neck, state.current);
  applyBone(state.head, state.current);
}
