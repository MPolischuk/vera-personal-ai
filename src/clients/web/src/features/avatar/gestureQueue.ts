import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";
import {
  applyArmStep,
  copyDef,
  limitDef,
  mixDef,
  mkDef,
  restDef,
  type ArmDef,
  type ArmSide,
} from "./arms";
import { SHRUG_POSE, WAVE_POSE } from "./gestureCatalog";
import { addPostureOffset } from "./elegantPosture";
import { createMotionFrame, motionGesture, resetMotionFrame } from "./motionGestures";
import type { CameraShot } from "./cameraShot";
import {
  gestureArmOffsetAt,
  gestureHeadOffsetAt,
  gestureShrugOffsetAt,
} from "./applyGesture";
import {
  ARM_GESTURE_COOLDOWN_MS,
  FRAME_MS,
  GESTURE_RELEASE_MS,
  GESTURE_SCALE,
  MAX_WEIGHT_STEP,
  SAME_ARM_ONESHOT_MS,
} from "./avatarMotion";

/**
 * One-shot and loop gestures. One arm occupant and one head occupant.
 * A second one-shot for the same hand waits. P3 cancels lower priorities
 * into a 180 ms release. Arm weights are multiplied by `GESTURE_SCALE`.
 */

export type GesturePriority = 0 | 1 | 2 | 3;

export type GestureChannel = "head" | "arm";

export type GestureHands = "none" | "L" | "R" | "both";

export type GesturePhase = "attack" | "hold" | "release" | "pending";

export interface GestureHead {
  pitch: number;
  yaw: number;
  roll: number;
}

export interface GestureRequest {
  name: string;
  priority: GesturePriority;
  channel: GestureChannel;
  hands: GestureHands;
  oneshot: boolean;
  /** Arm layer weight before `GESTURE_SCALE`. Head cues use `intensity`. */
  weight: number;
  durationMs: number;
  intensity?: number;
  pose?: ArmDef | null;
  head?: GestureHead | null;
  /** Reactions to a message start right after the current arm releases. */
  skipCooldown?: boolean;
}

export interface QueuedGesture {
  name: string;
  priority: GesturePriority;
  channel: GestureChannel;
  hands: GestureHands;
  oneshot: boolean;
  weight: number;
  durationMs: number;
  intensity: number;
  phase: GesturePhase;
  envelope: number;
  startedAtMs: number;
  releaseAtMs: number;
  releaseFrom: number;
  pose: ArmDef | null;
  head: GestureHead | null;
  skipCooldown?: boolean;
}

export interface GestureQueue {
  arm: QueuedGesture | null;
  head: QueuedGesture | null;
  pending: QueuedGesture[];
  armCooldownUntilMs: number;
  oneshotStartedAt: Partial<Record<string, number>>;
  lastTickMs: number;
}

const HEAD_SHARE = 0.7;
const NECK_SHARE = 0.45;

const _left = mkDef();
const _right = mkDef();
const _limitedL = mkDef();
const _limitedR = mkDef();
const _appliedL = mkDef();
const _appliedR = mkDef();
const _stepL = mkDef();
const _stepR = mkDef();
const _motionL = mkDef();
const _motionR = mkDef();
const _motionFrame = createMotionFrame();
let _motionSmile = 0;

/** Smile the active procedural gesture asks for this frame (0..1). */
export function motionSmile(): number {
  return _motionSmile;
}
let _appliedReady = false;

export function resetAppliedArms(): void {
  restDef("L", 0, _appliedL);
  restDef("R", 0, _appliedR);
  _appliedReady = true;
}

function seedAppliedArms(): void {
  if (_appliedReady) {
    return;
  }
  resetAppliedArms();
}

export function createGestureQueue(nowMs = 0): GestureQueue {
  return {
    arm: null,
    head: null,
    pending: [],
    armCooldownUntilMs: 0,
    oneshotStartedAt: {},
    lastTickMs: nowMs,
  };
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

function covers(hands: GestureHands, side: ArmSide): boolean {
  return hands === "both" || hands === side;
}

function overlaps(a: GestureHands, b: GestureHands): boolean {
  if (a === "none" || b === "none") {
    return false;
  }
  if (a === "both" || b === "both") {
    return true;
  }
  return a === b;
}

function toLive(request: GestureRequest, phase: GesturePhase, nowMs: number): QueuedGesture {
  return {
    name: request.name,
    priority: request.priority,
    channel: request.channel,
    hands: request.hands,
    oneshot: request.oneshot,
    weight: request.weight,
    durationMs: request.durationMs,
    intensity: request.intensity ?? 1,
    phase,
    envelope: 0,
    startedAtMs: nowMs,
    releaseAtMs: 0,
    releaseFrom: 0,
    pose: request.pose ? copyDef(request.pose) : null,
    head: request.head ? { ...request.head } : null,
    skipCooldown: request.skipCooldown,
  };
}

function beginRelease(gesture: QueuedGesture, nowMs: number): void {
  if (gesture.phase === "release" || gesture.phase === "pending") {
    return;
  }
  gesture.phase = "release";
  gesture.releaseAtMs = nowMs;
  gesture.releaseFrom = gesture.envelope;
}

function slotOf(queue: GestureQueue, channel: GestureChannel): QueuedGesture | null {
  return channel === "arm" ? queue.arm : queue.head;
}

function clearSlot(queue: GestureQueue, channel: GestureChannel, nowMs: number): void {
  if (channel === "arm") {
    queue.arm = null;
    queue.armCooldownUntilMs = nowMs + ARM_GESTURE_COOLDOWN_MS;
    return;
  }
  queue.head = null;
}

/** Drop held gestures under `priority` into the 180 ms release. */
export function cancelBelow(queue: GestureQueue, priority: GesturePriority, nowMs: number): void {
  if (queue.arm && queue.arm.priority < priority) {
    beginRelease(queue.arm, nowMs);
  }
  if (queue.head && queue.head.priority < priority) {
    beginRelease(queue.head, nowMs);
  }
  queue.pending = queue.pending.filter((item) => item.priority >= priority);
}

/** Release one name on one channel and drop a matching pending request. */
export function releaseChannel(
  queue: GestureQueue,
  channel: GestureChannel,
  name: string,
  nowMs: number,
): void {
  const slot = slotOf(queue, channel);
  if (slot && slot.name === name) {
    beginRelease(slot, nowMs);
  }
  queue.pending = queue.pending.filter(
    (item) => item.channel !== channel || item.name !== name,
  );
}

function cancelOverlapping(
  queue: GestureQueue,
  request: GestureRequest,
  priority: GesturePriority,
  nowMs: number,
): void {
  if (
    request.channel === "arm" &&
    queue.arm &&
    queue.arm.priority === priority &&
    overlaps(queue.arm.hands, request.hands)
  ) {
    beginRelease(queue.arm, nowMs);
  }
  if (request.channel === "head" && queue.head && queue.head.priority === priority) {
    beginRelease(queue.head, nowMs);
  }
  queue.pending = queue.pending.filter((item) => {
    if (item.priority !== priority) {
      return true;
    }
    if (request.channel === "head") {
      return item.channel !== "head";
    }
    return !(item.channel === "arm" && overlaps(item.hands, request.hands));
  });
}

function canStart(queue: GestureQueue, gesture: QueuedGesture, nowMs: number): boolean {
  if (gesture.channel === "head") {
    return queue.head === null;
  }
  if (queue.arm !== null) {
    return false;
  }
  if (gesture.priority < 3 && !gesture.skipCooldown && nowMs < queue.armCooldownUntilMs) {
    return false;
  }
  if (gesture.oneshot && gesture.priority < 3) {
    const last = queue.oneshotStartedAt[gesture.name];
    if (last !== undefined && nowMs - last < SAME_ARM_ONESHOT_MS) {
      return false;
    }
  }
  return true;
}

function start(queue: GestureQueue, gesture: QueuedGesture, nowMs: number): void {
  gesture.phase = "attack";
  gesture.envelope = 0;
  gesture.startedAtMs = nowMs;
  gesture.releaseAtMs = 0;
  gesture.releaseFrom = 0;
  if (gesture.channel === "arm") {
    queue.arm = gesture;
    if (gesture.oneshot) {
      queue.oneshotStartedAt[gesture.name] = nowMs;
    }
    return;
  }
  queue.head = gesture;
}

function promote(queue: GestureQueue, nowMs: number): void {
  const index = queue.pending.findIndex((item) => canStart(queue, item, nowMs));
  if (index < 0) {
    return;
  }
  for (let earlier = 0; earlier < index; earlier += 1) {
    const blocked = queue.pending[earlier];
    if (
      blocked &&
      blocked.channel === "arm" &&
      blocked.priority < 3 &&
      !blocked.skipCooldown &&
      nowMs < queue.armCooldownUntilMs
    ) {
      return;
    }
  }
  const [next] = queue.pending.splice(index, 1);
  if (!next) {
    return;
  }
  start(queue, next, nowMs);
}

export function enqueue(queue: GestureQueue, request: GestureRequest, nowMs: number): boolean {
  const slot = slotOf(queue, request.channel);
  if (slot && slot.name === request.name && slot.phase !== "release") {
    return true;
  }
  if (
    queue.pending.some((item) => item.name === request.name && item.channel === request.channel)
  ) {
    return true;
  }

  if (request.priority >= 3) {
    cancelBelow(queue, 3, nowMs);
  } else if (request.priority === 2) {
    cancelOverlapping(queue, request, 1, nowMs);
  }

  const pending = toLive(request, "pending", nowMs);
  if (request.priority >= 3) {
    queue.pending.unshift(pending);
  } else {
    queue.pending.push(pending);
  }
  promote(queue, nowMs);
  return true;
}

function maxEnvelopeStep(gesture: QueuedGesture, dt: number): number {
  if (gesture.channel === "head") {
    return Number.POSITIVE_INFINITY;
  }
  const scaled = Math.max(gesture.weight * GESTURE_SCALE, 1e-4);
  return (MAX_WEIGHT_STEP * (dt / FRAME_MS)) / scaled;
}

function approach(current: number, desired: number, maxStep: number): number {
  const delta = desired - current;
  if (Math.abs(delta) <= maxStep) {
    return desired;
  }
  return current + Math.sign(delta) * maxStep;
}

function advance(queue: GestureQueue, channel: GestureChannel, nowMs: number, dt: number): void {
  const gesture = slotOf(queue, channel);
  if (!gesture || gesture.phase === "pending") {
    return;
  }

  if (
    gesture.phase !== "release" &&
    gesture.durationMs > 0 &&
    nowMs - gesture.startedAtMs >= gesture.durationMs
  ) {
    beginRelease(gesture, nowMs);
  }

  if (gesture.phase === "release") {
    const elapsed = nowMs - gesture.releaseAtMs;
    const desired = gesture.releaseFrom * (1 - clamp01(elapsed / GESTURE_RELEASE_MS));
    gesture.envelope = approach(gesture.envelope, desired, maxEnvelopeStep(gesture, dt));
    if (gesture.envelope <= 1e-4 || elapsed >= GESTURE_RELEASE_MS) {
      gesture.envelope = 0;
      clearSlot(queue, channel, nowMs);
    }
    return;
  }

  const desired = clamp01((nowMs - gesture.startedAtMs) / GESTURE_RELEASE_MS);
  gesture.envelope = approach(gesture.envelope, desired, maxEnvelopeStep(gesture, dt));
  if (gesture.envelope >= 1 - 1e-4) {
    gesture.envelope = 1;
    gesture.phase = "hold";
  }
}

export function tick(queue: GestureQueue, nowMs: number): void {
  const dt = Math.max(0, nowMs - queue.lastTickMs);
  queue.lastTickMs = nowMs;
  advance(queue, "arm", nowMs, dt);
  advance(queue, "head", nowMs, dt);
  promote(queue, nowMs);
}

function sidePulse(name: string, side: ArmSide, timeSec: number): number {
  if (name !== "explain" && name !== "explainR") {
    return 1;
  }
  const phase = side === "R" ? Math.PI : 0;
  return 0.85 + 0.15 * Math.sin(timeSec * 1.4 + phase);
}

/** Blend toward the catalog pose. 0 keeps the rest pose. */
export function armBlendWeight(queue: GestureQueue, side: ArmSide, timeSec = 0): number {
  const gesture = queue.arm;
  const hasPose = gesture
    ? Boolean(gesture.pose ?? oneshotPose(gesture.name) ?? motionGesture(gesture.name)?.pose)
    : false;
  if (!gesture || !hasPose || !covers(gesture.hands, side)) {
    return 0;
  }
  return gesture.envelope * gesture.weight * GESTURE_SCALE * sidePulse(gesture.name, side, timeSec);
}

/** Base arm pose under the procedural wave / shrug overlays. */
export function oneshotPose(name: string): ArmDef | null {
  if (name === "wave") {
    return WAVE_POSE;
  }
  if (name === "shrug") {
    return SHRUG_POSE;
  }
  return null;
}

export function sampleArm(
  queue: GestureQueue,
  side: ArmSide,
  timeSec: number,
  out?: ArmDef,
): ArmDef {
  const dst = restDef(side, timeSec, out);
  const gesture = queue.arm;
  let pose = gesture ? gesture.pose ?? oneshotPose(gesture.name) : null;
  const motion = gesture && !pose ? motionGesture(gesture.name) : undefined;
  if (gesture && motion?.pose && covers(gesture.hands, side)) {
    const nowMs = timeSec * 1000;
    const elapsed = Math.max(0, nowMs - gesture.startedAtMs) / 1000;
    pose = motion.pose(side, progressOf(gesture, nowMs), elapsed, side === "L" ? _motionL : _motionR);
  }
  if (!gesture || !pose || !covers(gesture.hands, side)) {
    return dst;
  }
  const weight = armBlendWeight(queue, side, timeSec);
  if (weight <= 0) {
    return dst;
  }
  return mixDef(dst, pose, Math.min(1, weight), dst);
}

function bone(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

function addHead(vrm: VRM, offset: GestureHead, envelope: number): void {
  const head = bone(vrm, VRMHumanBoneName.Head);
  const neck = bone(vrm, VRMHumanBoneName.Neck);
  if (head) {
    head.rotation.x += offset.pitch * HEAD_SHARE * envelope;
    head.rotation.y += offset.yaw * HEAD_SHARE * envelope;
    head.rotation.z += offset.roll * HEAD_SHARE * envelope;
  }
  if (neck) {
    neck.rotation.x += offset.pitch * NECK_SHARE * envelope;
    neck.rotation.y += offset.yaw * NECK_SHARE * envelope;
    neck.rotation.z += offset.roll * NECK_SHARE * envelope;
  }
}

function progressOf(gesture: QueuedGesture, nowMs: number): number {
  if (gesture.durationMs <= 0) {
    return 0;
  }
  return clamp01((nowMs - gesture.startedAtMs) / gesture.durationMs);
}

const SHRUG_LIFT_GAIN = 0.85;
const WAVE_CYCLES = 2.5;
const WAVE_SWING_RAD = 0.35;

function applyLegacyArm(vrm: VRM, gesture: QueuedGesture, nowMs: number): void {
  const fade = gesture.envelope;
  if (fade <= 0) {
    return;
  }
  const progress = progressOf(gesture, nowMs);
  if (gesture.name === "shrug") {
    const pitch = gestureShrugOffsetAt(progress, gesture.intensity) * fade;
    const left = bone(vrm, VRMHumanBoneName.LeftShoulder);
    const right = bone(vrm, VRMHumanBoneName.RightShoulder);
    // Normalized VRM: +Z lifts the left shoulder, -Z the right one (rotation.x only tipped them forward).
    const lift = Math.abs(pitch) * SHRUG_LIFT_GAIN;
    if (left) {
      left.rotation.z += lift;
    }
    if (right) {
      right.rotation.z -= lift;
    }
    // Quick readable shrug: slight head tilt + soft pitch-up (eyebrow-ish).
    const k = Math.abs(pitch) / 0.35;
    addHead(vrm, { pitch: -0.06 * k, yaw: 0, roll: 0.16 * k }, fade);
    return;
  }
  if (gesture.name !== "wave") {
    return;
  }
  const arm = gestureArmOffsetAt(progress, gesture.intensity);
  const lower = bone(vrm, VRMHumanBoneName.RightLowerArm);
  const upper = bone(vrm, VRMHumanBoneName.RightUpperArm);
  // Forearm is vertical in WAVE_POSE: side-to-side swing about its local X, a few cycles.
  const swing = Math.sin(progress * Math.PI * 2 * WAVE_CYCLES) * WAVE_SWING_RAD * Math.sin(progress * Math.PI) * fade;
  const hand = bone(vrm, VRMHumanBoneName.RightHand);
  if (lower) {
    lower.rotateX(swing);
  } else if (upper) {
    upper.rotateX(swing);
  }
  if (hand) {
    hand.rotateX(swing * 0.4);
  }
  // Small friendly head tilt only; the old 0.55 rad yaw turned the face away.
  addHead(vrm, { pitch: 0, yaw: 0, roll: (arm.headYaw / 0.55) * 0.08 }, fade);
}

function applyHeadGesture(vrm: VRM, gesture: QueuedGesture, nowMs: number): void {
  if (gesture.name === "nod" || gesture.name === "shake") {
    addHead(
      vrm,
      gestureHeadOffsetAt(gesture.name, progressOf(gesture, nowMs), gesture.intensity),
      gesture.envelope,
    );
    return;
  }
  if (gesture.head) {
    addHead(vrm, gesture.head, gesture.envelope);
  }
}

/** Body, head and smile overlays of a procedural gesture (see motionGestures). */
function applyMotionFrame(vrm: VRM, gesture: QueuedGesture | null, nowMs: number): void {
  if (!gesture || gesture.envelope <= 0) {
    return;
  }
  const motion = motionGesture(gesture.name);
  if (!motion?.frame) {
    return;
  }
  const elapsed = Math.max(0, nowMs - gesture.startedAtMs) / 1000;
  motion.frame(progressOf(gesture, nowMs), elapsed, resetMotionFrame(_motionFrame));
  const fade = gesture.envelope;
  for (const offset of _motionFrame.body) {
    addPostureOffset(offset.bone, offset.x * fade, offset.y * fade, offset.z * fade);
  }
  addHead(vrm, _motionFrame.head, fade);
  _motionSmile = Math.max(_motionSmile, _motionFrame.smile * fade);
}

/** Rest arms, then the queued pose. One writer for both arms while motion v2 is on. */
export function applyQueuedArms(
  vrm: VRM,
  queue: GestureQueue,
  nowMs: number,
  shot: CameraShot,
): void {
  const timeSec = nowMs / 1000;
  sampleArm(queue, "L", timeSec, _left);
  sampleArm(queue, "R", timeSec, _right);
  limitDef(_left, _limitedL);
  limitDef(_right, _limitedR);
  seedAppliedArms();
  void shot;
  const fingers = true; // every shot, so bust ↔ waist never pops
  applyArmStep(vrm, _appliedL, _limitedL, "L", _stepL, { fingers });
  copyDef(_stepL, _appliedL);
  applyArmStep(vrm, _appliedR, _limitedR, "R", _stepR, { fingers });
  copyDef(_stepR, _appliedR);

  if (queue.arm && !queue.arm.pose) {
    applyLegacyArm(vrm, queue.arm, nowMs);
  }
  _motionSmile = 0;
  applyMotionFrame(vrm, queue.arm, nowMs);
  applyMotionFrame(vrm, queue.head, nowMs);
  if (queue.head) {
    applyHeadGesture(vrm, queue.head, nowMs);
  } else if (queue.arm?.head) {
    addHead(vrm, queue.arm.head, queue.arm.envelope);
  }
}
