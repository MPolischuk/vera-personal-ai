import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import { Euler, Quaternion, Vector3, type Object3D } from "three";

/**
 * Elegant standing posture, ported from vera-video `src/pose.js` (POSE.E/L/R).
 * Radians in each bone's local frame of the VRM normalized rig. Left values;
 * the right side mirrors (y, z flip) unless overridden. Weight sits on the
 * right leg; the left leg is soft, slightly forward, foot turned out (soft T).
 *
 * Weight shifts keep both feet planted: the authored soft-T stance stays the
 * base, hips drift laterally with a roll, the free knee softens, spine and
 * shoulders counter-rotate, then a two-bone IK snaps each foot back to its
 * plant target (world XZ + ground Y).
 */
type Triple = readonly [number, number, number];

export const POSTURE: Readonly<Record<string, Triple>> = {
  hips: [0, 0.05, -0.022],
  spine: [-0.035, -0.03, 0.02],
  chest: [-0.04, -0.02, 0.012],
  upperChest: [-0.03, 0, 0],
  neck: [-0.03, 0, 0],
  head: [0.05, 0, 0],
  leftShoulder: [0, 0.1, -0.07],
  rightShoulder: [0, -0.1, 0.07],
  leftUpperLeg: [-0.14, 0.05, -0.06],
  leftLowerLeg: [0.2, 0, 0],
  leftFoot: [-0.02, 0.3, 0],
  rightUpperLeg: [0, -0.03, 0],
  rightLowerLeg: [0, 0, 0],
  rightFoot: [0, -0.16, 0],
};

/** Slow breathing-scale sway on the hips (rad, Hz). Not a weight shift. */
export const POSTURE_SWAY = { roll: 0.004, yaw: 0.005, hz: 0.11 } as const;

/**
 * Weight-shift amplitudes at |weight| = 1. Hips move over the stance foot;
 * the free knee softens; torso counters. Feet are re-planted by IK afterward.
 */
export const WEIGHT_SHIFT = {
  /** Hip world-X drift toward the stance side (m). */
  hipLateralM: 0.028,
  /** Extra hip roll toward the stance side (rad, local Z). */
  hipRoll: 0.055,
  /** Extra hip yaw (rad). */
  hipYaw: 0.018,
  /** Soften the free lower leg (rad, local X). */
  freeKnee: 0.16,
  /** Free upper leg eases back a touch (rad, local X). */
  freeUpper: 0.05,
  /** Stance upper leg settles under the hip (rad, local X). */
  stanceUpper: -0.03,
  /** Spine / chest counter-roll (rad). */
  spineRoll: 0.03,
  chestRoll: 0.02,
  /** Shoulder counter (local Z). */
  shoulder: 0.025,
} as const;

/** Max horizontal foot drift we accept across a full weight flip (m). */
export const FOOT_PLANT_MAX_M = 0.008;

/** Bone whose posture mirrors to the other side when the weight shifts (legacy). */
const SWAP: Readonly<Record<string, string>> = {
  leftUpperLeg: "rightUpperLeg",
  rightUpperLeg: "leftUpperLeg",
  leftLowerLeg: "rightLowerLeg",
  rightLowerLeg: "leftLowerLeg",
  leftFoot: "rightFoot",
  rightFoot: "leftFoot",
};

/** Weight on the left leg: the authored stance mirrored (y, z flip; legs swap). */
export function mirroredPosture(name: string): Triple {
  if (name === "leftShoulder" || name === "rightShoulder") {
    return POSTURE[name];
  }
  const source = POSTURE[SWAP[name] ?? name];
  return [source[0], -source[1], -source[2]];
}

/** Extra upright attention (typing): chest and chin lift a little. */
export const STRAIGHTEN: Readonly<Record<string, Triple>> = {
  spine: [0.025, 0, 0],
  chest: [0.02, 0, 0],
  neck: [0.01, 0, 0],
  head: [-0.03, 0, 0],
};

interface Slot {
  name: string;
  bone: Object3D;
  /** Pose the other layers left before our premultiply (restored next frame). */
  last: Quaternion;
  base: Triple;
  sway: boolean;
}

interface LegChain {
  upper: Object3D;
  lower: Object3D;
  foot: Object3D;
  /** World plant target captured from the authored stance. */
  plant: Vector3;
}

export interface PostureState {
  slots: Slot[] | null;
  legs: { L: LegChain; R: LegChain } | null;
  hipsBone: Object3D | null;
  hipsLastPos: Vector3;
  /** 1 = weight on the right leg (authored), -1 = left. Set by the idle director. */
  weight: number;
  /** 0..1 attentive straighten. Also halves the stance asymmetry. */
  straighten: number;
}

export function createPostureState(): PostureState {
  return {
    slots: null,
    legs: null,
    hipsBone: null,
    hipsLastPos: new Vector3(),
    weight: 1,
    straighten: 0,
  };
}

const _e = new Euler();
const _q = new Quaternion();
const _sway = new Quaternion();
const _extra = new Quaternion();
const _v1 = new Vector3();
const _v2 = new Vector3();
const _v3 = new Vector3();
const _v4 = new Vector3();
const _hipPos = new Vector3();
const _kneePos = new Vector3();
const _footPos = new Vector3();
const _target = new Vector3();
const _pole = new Vector3();
const _upperDir = new Vector3();
const _bendAxis = new Vector3();
const _qA = new Quaternion();

/** Per-frame body offsets from gestures (bow, sigh, lean...). Consumed by `updatePosture`. */
const pendingExtras = new Map<string, [number, number, number]>();

export function addPostureOffset(name: string, x: number, y: number, z: number): void {
  const entry = pendingExtras.get(name);
  if (entry) {
    entry[0] += x;
    entry[1] += y;
    entry[2] += z;
    return;
  }
  pendingExtras.set(name, [x, y, z]);
}

export function clearPostureOffsets(): void {
  pendingExtras.clear();
}

function boneOf(vrm: VRM, name: string): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name as VRMHumanBoneName) ?? null;
}

function bind(vrm: VRM, state: PostureState): void {
  const slots: Slot[] = [];
  for (const name of Object.keys(POSTURE)) {
    const bone = boneOf(vrm, name);
    if (!bone) {
      continue;
    }
    slots.push({
      name,
      bone,
      last: bone.quaternion.clone(),
      base: POSTURE[name],
      sway: name === "hips",
    });
  }
  state.slots = slots;
  state.hipsBone = boneOf(vrm, "hips");
  if (state.hipsBone) {
    state.hipsLastPos.copy(state.hipsBone.position);
  }

  const leftUpper = boneOf(vrm, "leftUpperLeg");
  const leftLower = boneOf(vrm, "leftLowerLeg");
  const leftFoot = boneOf(vrm, "leftFoot");
  const rightUpper = boneOf(vrm, "rightUpperLeg");
  const rightLower = boneOf(vrm, "rightLowerLeg");
  const rightFoot = boneOf(vrm, "rightFoot");
  if (!(leftUpper && leftLower && leftFoot && rightUpper && rightLower && rightFoot)) {
    return;
  }
  // Snapshot plant targets from the authored soft-T (weight = +1), then restore.
  const saved = slots.map((slot) => slot.bone.quaternion.clone());
  for (let i = 0; i < slots.length; i += 1) {
    const slot = slots[i]!;
    const [x, y, z] = slot.base;
    slot.bone.quaternion.copy(saved[i]!);
    slot.bone.quaternion.premultiply(_q.setFromEuler(_e.set(x, y, z)));
  }
  vrm.scene.updateMatrixWorld(true);
  state.legs = {
    L: { upper: leftUpper, lower: leftLower, foot: leftFoot, plant: leftFoot.getWorldPosition(new Vector3()) },
    R: { upper: rightUpper, lower: rightLower, foot: rightFoot, plant: rightFoot.getWorldPosition(new Vector3()) },
  };
  for (let i = 0; i < slots.length; i += 1) {
    slots[i]!.bone.quaternion.copy(saved[i]!);
    slots[i]!.last.copy(saved[i]!);
  }
}

/**
 * Puts every posture bone back to what the other layers wrote last frame.
 * Call before any bone writer: partial writers (breath sets only chest x,
 * idleLife adds to head) then never see, and never accumulate, the posture.
 */
export function undoPosture(state: PostureState): void {
  for (const slot of state.slots ?? []) {
    slot.bone.quaternion.copy(slot.last);
  }
  if (state.hipsBone) {
    state.hipsBone.position.copy(state.hipsLastPos);
  }
}

/** Authored soft-T plus weight-shift offsets. Feet stay on the authored values. */
function postureEuler(name: string, weight: number, straight: number): Triple {
  const base = POSTURE[name];
  const w = Math.max(-1, Math.min(1, weight));
  const onRight = w; // +1 right stance, -1 left
  const amount = Math.abs(w);
  const asym = 1 - 0.5 * straight;
  let [x, y, z] = base;

  if (name === "hips") {
    y += WEIGHT_SHIFT.hipYaw * onRight * asym;
    z += WEIGHT_SHIFT.hipRoll * onRight * asym;
  } else if (name === "spine") {
    z -= WEIGHT_SHIFT.spineRoll * onRight * asym;
  } else if (name === "chest") {
    z -= WEIGHT_SHIFT.chestRoll * onRight * asym;
  } else if (name === "leftShoulder") {
    z += WEIGHT_SHIFT.shoulder * onRight * asym;
  } else if (name === "rightShoulder") {
    z += WEIGHT_SHIFT.shoulder * onRight * asym;
  } else if (name === "leftUpperLeg") {
    // Free when weight on right (onRight>0).
    x += (onRight > 0 ? WEIGHT_SHIFT.freeUpper : WEIGHT_SHIFT.stanceUpper) * amount * asym;
  } else if (name === "rightUpperLeg") {
    x += (onRight < 0 ? WEIGHT_SHIFT.freeUpper : WEIGHT_SHIFT.stanceUpper) * amount * asym;
  } else if (name === "leftLowerLeg") {
    x += (onRight > 0 ? WEIGHT_SHIFT.freeKnee : -0.06) * amount * asym;
  } else if (name === "rightLowerLeg") {
    x += (onRight < 0 ? WEIGHT_SHIFT.freeKnee : -0.06) * amount * asym;
  }
  // Feet: always the authored soft-T (never mirrored) so the plant targets stay valid.
  return [x, y, z];
}

/**
 * Two-bone IK: keep the foot at `plant` (world). Knee bends forward (+Z pole).
 * Upper/lower local rotations are rewritten; foot rotation is left alone.
 */
export function plantLeg(upper: Object3D, lower: Object3D, foot: Object3D, plant: Vector3): void {
  upper.getWorldPosition(_hipPos);
  // Bone lengths from the current bind-ish hierarchy.
  lower.getWorldPosition(_kneePos);
  foot.getWorldPosition(_footPos);
  const lu = _hipPos.distanceTo(_kneePos);
  const ll = _kneePos.distanceTo(_footPos);
  if (lu < 1e-5 || ll < 1e-5) {
    return;
  }

  _target.copy(plant);
  // Keep the plant height if the target drifted vertically from root motion.
  _target.y = plant.y;
  let dist = _hipPos.distanceTo(_target);
  const maxReach = (lu + ll) * 0.999;
  const minReach = Math.abs(lu - ll) * 1.001;
  if (dist > maxReach) {
    _target.sub(_hipPos).setLength(maxReach).add(_hipPos);
    dist = maxReach;
  } else if (dist < minReach) {
    _target.sub(_hipPos).setLength(Math.max(minReach, 1e-4)).add(_hipPos);
    dist = _hipPos.distanceTo(_target);
  }

  // Law of cosines: angle at the hip between upper and the hip→target axis.
  const cosKnee = Math.max(-1, Math.min(1, (lu * lu + ll * ll - dist * dist) / (2 * lu * ll)));
  const kneeFlex = Math.PI - Math.acos(cosKnee); // 0 = straight

  // Pole: bend the knee forward in character space (+Z).
  _pole.set(0, 0, 1);
  _upperDir.subVectors(_target, _hipPos).normalize();
  _bendAxis.crossVectors(_upperDir, _pole);
  if (_bendAxis.lengthSq() < 1e-8) {
    _bendAxis.set(1, 0, 0);
  } else {
    _bendAxis.normalize();
  }
  // Midpoint (knee) lies off the hip→target axis by the usual two-bone offset.
  const cosHip = Math.max(-1, Math.min(1, (lu * lu + dist * dist - ll * ll) / (2 * lu * dist)));
  const hipAng = Math.acos(cosHip);
  _v1.copy(_upperDir).applyAxisAngle(_bendAxis, hipAng).multiplyScalar(lu).add(_hipPos); // desired knee

  // Rotate upper so its child (lower) aims at the desired knee.
  aimBone(upper, lower, _v1);
  // Rotate lower so its child (foot) aims at the plant.
  aimBone(lower, foot, _target);
  void kneeFlex;
}

const _qParent = new Quaternion();
const _qDeltaLocal = new Quaternion();

/** Rotate `bone` so `child` moves onto `worldTarget`. */
function aimBone(bone: Object3D, child: Object3D, worldTarget: Vector3): void {
  bone.updateWorldMatrix(true, false);
  child.getWorldPosition(_v2);
  bone.getWorldPosition(_v3);
  _v1.subVectors(_v2, _v3);
  if (_v1.lengthSq() < 1e-10) {
    return;
  }
  _v1.normalize();
  _v4.subVectors(worldTarget, _v3);
  if (_v4.lengthSq() < 1e-10) {
    return;
  }
  _v4.normalize();
  _qA.setFromUnitVectors(_v1, _v4);
  const parent = bone.parent;
  if (parent) {
    parent.getWorldQuaternion(_qParent);
    // q_local' = q_parent^{-1} * Δ * q_parent * q_local
    _qDeltaLocal.copy(_qParent).invert().multiply(_qA).multiply(_qParent);
    bone.quaternion.premultiply(_qDeltaLocal);
  } else {
    bone.quaternion.premultiply(_qA);
  }
}

/**
 * Premultiplies the posture (stance + weight shift, straighten, gesture
 * extras) onto what the other layers wrote this frame, then re-plants the
 * feet. Call last.
 */
export function updatePosture(vrm: VRM, state: PostureState, nowMs: number): void {
  if (!state.slots) {
    bind(vrm, state);
  }
  const slots = state.slots!;
  const phase = (nowMs / 1000) * POSTURE_SWAY.hz * Math.PI * 2;
  const w = Math.max(-1, Math.min(1, state.weight));
  const straight = Math.max(0, Math.min(1, state.straighten));

  for (const slot of slots) {
    const q = slot.bone.quaternion;
    slot.last.copy(q);
    const [x0, y0, z0] = postureEuler(slot.name, w, straight);
    const up = STRAIGHTEN[slot.name];
    const extra = pendingExtras.get(slot.name);
    let x = x0;
    let y = y0;
    let z = z0;
    if (up) {
      x += up[0] * straight;
    }
    _q.setFromEuler(_e.set(x, y, z));
    if (extra) {
      _q.multiply(_extra.setFromEuler(_e.set(extra[0], extra[1], extra[2])));
    }
    if (slot.sway) {
      _q.multiply(
        _sway.setFromEuler(
          _e.set(0, Math.sin(phase * 0.7) * POSTURE_SWAY.yaw, Math.sin(phase) * POSTURE_SWAY.roll),
        ),
      );
    }
    q.copy(_q.multiply(q));
  }

  if (state.hipsBone) {
    state.hipsLastPos.copy(state.hipsBone.position);
    // +weight → shift hips toward Vera's right (−X in VRM: +X is her left).
    state.hipsBone.position.x = state.hipsLastPos.x - WEIGHT_SHIFT.hipLateralM * w * (1 - 0.5 * straight);
  }

  if (state.legs) {
    vrm.scene.updateMatrixWorld(true);
    // IK rewrites upper/lower only; feet keep the authored soft-T from postureEuler.
    plantLeg(state.legs.L.upper, state.legs.L.lower, state.legs.L.foot, state.legs.L.plant);
    plantLeg(state.legs.R.upper, state.legs.R.lower, state.legs.R.foot, state.legs.R.plant);
  }

  pendingExtras.clear();
}

/** Horizontal distance a foot travels between two weight samples (for tests). */
export function footHorizontalDrift(a: Vector3, b: Vector3): number {
  return Math.hypot(a.x - b.x, a.z - b.z);
}
