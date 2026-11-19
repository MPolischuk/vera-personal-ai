import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { PerspectiveCamera } from "three";
import { Box3, Vector3 } from "three";

export type CameraShot = "bust" | "waist" | "full";

export type CameraShotDirection = "in" | "out";

export const CAMERA_SHOTS: readonly CameraShot[] = ["bust", "waist", "full"];

export const DEFAULT_CAMERA_SHOT: CameraShot = "bust";

/** Matches the PerspectiveCamera FOV in `createAvatarScene`. */
export const CAMERA_VERTICAL_FOV_DEG = 28;

/** Timed dolly between discrete shots (smoothstep). */
export const CAMERA_SHOT_MOVE_MS = 320;

const BUST_DISTANCE = 1.45;
const WAIST_DISTANCE = 2.4;
const FULL_HEAD_PAD = 0.22;
const FULL_FEET_PAD = 0.12;
const FULL_MIN_SPAN = 1.45;
const FULL_MARGIN = 1.22;
export const FULL_MIN_Z = 3.4;
/** Farthest dolly; also sizes the photo wall so the full shot still fills it. */
export const FULL_MAX_Z = 6.8;

const FALLBACK_ANCHORS: BodyAnchors = {
  headY: 1.5,
  hipsY: 0.9,
  feetY: 0,
  visualTop: 1.72,
  visualBottom: -0.08,
};

export interface BodyAnchors {
  headY: number;
  hipsY: number;
  feetY: number;
  /** Mesh AABB top (hair, accessories). Falls back to head + pad. */
  visualTop?: number;
  /** Mesh AABB bottom (shoes). Falls back to feet − pad. */
  visualBottom?: number;
}

export interface CameraFraming {
  position: { x: number; y: number; z: number };
  lookAt: { x: number; y: number; z: number };
}

export interface CameraShotMotion {
  startPos: Vector3;
  startLook: Vector3;
  currentPos: Vector3;
  currentLook: Vector3;
  targetPos: Vector3;
  targetLook: Vector3;
  elapsedMs: number;
}

export function isCameraShot(value: string): value is CameraShot {
  return (CAMERA_SHOTS as readonly string[]).includes(value);
}

export function canZoomIn(shot: CameraShot): boolean {
  return shot !== "bust";
}

export function canZoomOut(shot: CameraShot): boolean {
  return shot !== "full";
}

export function stepCameraShot(
  shot: CameraShot,
  direction: CameraShotDirection,
): CameraShot {
  const index = CAMERA_SHOTS.indexOf(shot);
  const next = direction === "out" ? index + 1 : index - 1;
  const clamped = Math.max(0, Math.min(CAMERA_SHOTS.length - 1, next));
  return CAMERA_SHOTS[clamped] ?? DEFAULT_CAMERA_SHOT;
}

function worldBoneY(vrm: VRM, name: VRMHumanBoneName): number | null {
  const node = vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
  if (!node) {
    return null;
  }
  const world = new Vector3();
  node.getWorldPosition(world);
  return world.y;
}

/**
 * Head / hips / feet in world Y. Null when the VRM has no head or upper chest
 * (same fallback path as PT-16 bust framing).
 */
export function bodyAnchorsFromVrm(vrm: VRM): BodyAnchors | null {
  vrm.scene.updateMatrixWorld(true);
  const headY =
    worldBoneY(vrm, VRMHumanBoneName.Head) ??
    worldBoneY(vrm, VRMHumanBoneName.UpperChest);
  if (headY == null) {
    return null;
  }

  const hipsY =
    worldBoneY(vrm, VRMHumanBoneName.Hips) ??
    worldBoneY(vrm, VRMHumanBoneName.Spine) ??
    headY * 0.55;
  const leftFoot =
    worldBoneY(vrm, VRMHumanBoneName.LeftFoot) ??
    worldBoneY(vrm, VRMHumanBoneName.LeftToes);
  const rightFoot =
    worldBoneY(vrm, VRMHumanBoneName.RightFoot) ??
    worldBoneY(vrm, VRMHumanBoneName.RightToes);
  const feetY =
    leftFoot != null && rightFoot != null
      ? Math.min(leftFoot, rightFoot)
      : (leftFoot ?? rightFoot ?? 0);

  const box = new Box3().setFromObject(vrm.scene);
  const visualTop = Number.isFinite(box.max.y) ? box.max.y : undefined;
  const visualBottom = Number.isFinite(box.min.y) ? box.min.y : undefined;

  return { headY, hipsY, feetY, visualTop, visualBottom };
}

function distanceForVerticalSpan(span: number): number {
  const halfTan = Math.tan((CAMERA_VERTICAL_FOV_DEG * Math.PI) / 360);
  return span / (2 * halfTan);
}

export function framingForShot(
  shot: CameraShot,
  anchors: BodyAnchors | null,
): CameraFraming {
  if (shot === "bust") {
    if (!anchors) {
      return {
        position: { x: 0, y: 1.35, z: 1.55 },
        lookAt: { x: 0, y: 1.25, z: 0 },
      };
    }
    const lookY = anchors.headY - 0.05;
    return {
      position: { x: 0, y: lookY + 0.05, z: BUST_DISTANCE },
      lookAt: { x: 0, y: lookY, z: 0 },
    };
  }

  const body = anchors ?? FALLBACK_ANCHORS;
  if (shot === "waist") {
    const lookY = (body.headY + body.hipsY) / 2;
    return {
      position: { x: 0, y: lookY, z: WAIST_DISTANCE },
      lookAt: { x: 0, y: lookY, z: 0 },
    };
  }

  const top = body.visualTop ?? body.headY + FULL_HEAD_PAD;
  const bottom = body.visualBottom ?? body.feetY - FULL_FEET_PAD;
  const lookY = (top + bottom) / 2;
  const span = Math.max(top - bottom, FULL_MIN_SPAN) * FULL_MARGIN;
  const z = Math.min(
    FULL_MAX_Z,
    Math.max(FULL_MIN_Z, distanceForVerticalSpan(span)),
  );
  return {
    position: { x: 0, y: lookY, z },
    lookAt: { x: 0, y: lookY, z: 0 },
  };
}

export function framingFromVrm(vrm: VRM, shot: CameraShot): CameraFraming {
  return framingForShot(shot, bodyAnchorsFromVrm(vrm));
}

export function applyCameraFraming(
  camera: PerspectiveCamera,
  framing: CameraFraming,
): void {
  camera.position.set(framing.position.x, framing.position.y, framing.position.z);
  camera.lookAt(framing.lookAt.x, framing.lookAt.y, framing.lookAt.z);
}

function framingVectors(framing: CameraFraming): {
  pos: Vector3;
  look: Vector3;
} {
  return {
    pos: new Vector3(framing.position.x, framing.position.y, framing.position.z),
    look: new Vector3(framing.lookAt.x, framing.lookAt.y, framing.lookAt.z),
  };
}

export function createCameraShotMotion(framing: CameraFraming): CameraShotMotion {
  const { pos, look } = framingVectors(framing);
  return {
    startPos: pos.clone(),
    startLook: look.clone(),
    currentPos: pos.clone(),
    currentLook: look.clone(),
    targetPos: pos.clone(),
    targetLook: look.clone(),
    elapsedMs: CAMERA_SHOT_MOVE_MS,
  };
}

export function retargetCameraShot(
  motion: CameraShotMotion,
  framing: CameraFraming,
  snap: boolean,
): void {
  const { pos, look } = framingVectors(framing);
  motion.targetPos.copy(pos);
  motion.targetLook.copy(look);
  if (snap) {
    motion.startPos.copy(pos);
    motion.startLook.copy(look);
    motion.currentPos.copy(pos);
    motion.currentLook.copy(look);
    motion.elapsedMs = CAMERA_SHOT_MOVE_MS;
    return;
  }
  motion.startPos.copy(motion.currentPos);
  motion.startLook.copy(motion.currentLook);
  motion.elapsedMs = 0;
}

function smoothstep(t: number): number {
  const x = Math.min(1, Math.max(0, t));
  return x * x * (3 - 2 * x);
}

export function updateCameraShotMotion(
  camera: PerspectiveCamera,
  motion: CameraShotMotion,
  deltaSeconds: number,
): void {
  motion.elapsedMs += Math.max(0, deltaSeconds) * 1000;
  const t = smoothstep(motion.elapsedMs / CAMERA_SHOT_MOVE_MS);
  motion.currentPos.lerpVectors(motion.startPos, motion.targetPos, t);
  motion.currentLook.lerpVectors(motion.startLook, motion.targetLook, t);
  camera.position.copy(motion.currentPos);
  camera.lookAt(motion.currentLook);
}
