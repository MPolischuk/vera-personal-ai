import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";
import { Matrix4, Quaternion, Vector3 } from "three";
import type { CameraShot } from "./cameraShot";
import { idleLifeArmGain } from "./idleLife";

/**
 * Arm pose in left-arm space: +x out from the body, +y up, +z forward.
 * The right arm is the same pose with `mirror` on each quaternion.
 * Limits are the three sample points from the arm notes (elbow, mid-forearm, wrist).
 *
 * Palm convention (VRM normalized left hand: fingers +X, **true palm −Y**, back +Y):
 * `ArmDef.palm` is the world direction that hand-local **+Y (dorso / back)** should face.
 * It is the *opposite* of the true palm normal. Examples in left-arm space:
 *   - true palm UP    → `palm ≈ { y: -1 }`  (back faces down)
 *   - true palm DOWN  → `palm ≈ { y: +1 }`  (back faces up)
 *   - true palm to cam (+Z) → `palm ≈ { z: -1 }` (back faces away)
 *   - back to cam / palm on apron → `palm ≈ { z: +1 }`
 * Authors historically treated `palm` as the true-palm direction; the clasp fix and
 * this revision author against the dorsal convention above.
 */

export type ArmSide = "L" | "R";

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

export interface ArmDef {
  /** Unit direction, shoulder → elbow. */
  d: Vec3;
  /** Unit direction, elbow → wrist. */
  f: Vec3;
  /**
   * World direction for hand-local +Y (back of hand). Opposite the true palm (−Y).
   * See file header for palm-up / palm-down / toward-camera examples.
   */
  palm: Vec3;
  palmW: number;
  /** [flexion, ulnar deviation] in radians. */
  wrist: [number, number];
  /** Index, middle, ring, little. Each 0..1. */
  curl: [number, number, number, number];
  thumb: number;
  spread: number;
  /**
   * Extra thumb tuck, 0..1 (authored only by point / sidePoint). Blended
   * continuously so entering/leaving a point never snaps the finger pose.
   * Other gestures retain the original rest / clasp / fist calibration.
   */
  thumbClosed?: number;
}

export interface ArmPoints {
  elbow: Vec3;
  mid: Vec3;
  wrist: Vec3;
}

export interface SideChoice {
  side: ArmSide;
  /** Torso yaw toward the target, clamped to ±0.4 rad. */
  turn: number;
}

export interface SolveArmOptions {
  /** Bust leaves finger bones on the file pose. Waist and full write them. */
  fingers?: boolean;
}

/** Copied from the arm notes. Do not round. */
export const LIM = {
  flexMin: 0.04,
  flexMax: 2.45,
  innerX: 0.11,
  frontZ: 0.10,
  handFrontZ: 0.115,
  backZ: -0.11,
  shoulderX: 0.10,
  yLow: -0.62,
  yHigh: 0.16,
} as const;

export const CURL_OPEN: readonly [number, number, number, number] = [0.10, 0.14, 0.20, 0.28];
export const CURL_RELAX: readonly [number, number, number, number] = [0.28, 0.34, 0.42, 0.50];
export const CURL_POINT: readonly [number, number, number, number] = [0.0, 1.0, 1.0, 1.0];

/**
 * Formal wait. Elbows slightly out, forearms in, hands a little apart in front of
 * the apron, neutral wrist, relaxed fingers (curl grows index→pinky, slight spread).
 */
export const REST: ArmDef = {
  d: { x: 0.24, y: -0.9, z: 0.3 },
  f: { x: -0.3, y: -0.8, z: 0.5 },
  palm: { x: -0.15, y: -0.2, z: -0.85 },
  palmW: 0.65,
  wrist: [0.08, 0],
  curl: [0.38, 0.45, 0.52, 0.6],
  thumb: 0.3,
  spread: 0.3,
};

/** Right hand unless the target sits more than 0.09 rad to Vera's left. */
export const DOMINANT_SIDE: ArmSide = "R";

const SIDE_AZIMUTH = 0.09;
const TURN_GAIN = 0.2;
const TURN_MAX = 0.4;
const AIM_Z_MIN = -0.3;
const BISECT_STEPS = 9;
const PALM_ROLL_MAX = 1.35;
const SHOULDER_LIFT_MAX = 0.18;

/** Reference lengths until `measureArmRig` reads the VRM. Same unit system as `LIM`. */
const DEFAULT_LU = 0.25;
const DEFAULT_LF = 0.22;

const SEED_L = 1;
const SEED_R = 2;

const SPREAD_BY_FINGER = [-0.10, -0.02, 0.05, 0.13] as const;

interface FingerSegment {
  curl: number;
  left: VRMHumanBoneName;
  right: VRMHumanBoneName;
}

const FINGERS: readonly (readonly FingerSegment[])[] = [
  [
    { curl: 1.3, left: VRMHumanBoneName.LeftIndexProximal, right: VRMHumanBoneName.RightIndexProximal },
    { curl: 1.45, left: VRMHumanBoneName.LeftIndexIntermediate, right: VRMHumanBoneName.RightIndexIntermediate },
    { curl: 0.7, left: VRMHumanBoneName.LeftIndexDistal, right: VRMHumanBoneName.RightIndexDistal },
  ],
  [
    { curl: 1.3, left: VRMHumanBoneName.LeftMiddleProximal, right: VRMHumanBoneName.RightMiddleProximal },
    { curl: 1.45, left: VRMHumanBoneName.LeftMiddleIntermediate, right: VRMHumanBoneName.RightMiddleIntermediate },
    { curl: 0.7, left: VRMHumanBoneName.LeftMiddleDistal, right: VRMHumanBoneName.RightMiddleDistal },
  ],
  [
    { curl: 1.3, left: VRMHumanBoneName.LeftRingProximal, right: VRMHumanBoneName.RightRingProximal },
    { curl: 1.45, left: VRMHumanBoneName.LeftRingIntermediate, right: VRMHumanBoneName.RightRingIntermediate },
    { curl: 0.7, left: VRMHumanBoneName.LeftRingDistal, right: VRMHumanBoneName.RightRingDistal },
  ],
  [
    { curl: 1.3, left: VRMHumanBoneName.LeftLittleProximal, right: VRMHumanBoneName.RightLittleProximal },
    { curl: 1.45, left: VRMHumanBoneName.LeftLittleIntermediate, right: VRMHumanBoneName.RightLittleIntermediate },
    { curl: 0.7, left: VRMHumanBoneName.LeftLittleDistal, right: VRMHumanBoneName.RightLittleDistal },
  ],
];

const THUMB: readonly FingerSegment[] = [
  { curl: 0.7, left: VRMHumanBoneName.LeftThumbMetacarpal, right: VRMHumanBoneName.RightThumbMetacarpal },
  { curl: 1.35, left: VRMHumanBoneName.LeftThumbProximal, right: VRMHumanBoneName.RightThumbProximal },
  { curl: 0.9, left: VRMHumanBoneName.LeftThumbDistal, right: VRMHumanBoneName.RightThumbDistal },
];

interface ArmMetrics {
  lu: number;
  lf: number;
  shoulderX: number;
  innerX: number;
  frontZ: number;
  handFrontZ: number;
  backZ: number;
}

const metrics: ArmMetrics = {
  lu: DEFAULT_LU,
  lf: DEFAULT_LF,
  shoulderX: LIM.shoulderX,
  innerX: LIM.innerX,
  frontZ: LIM.frontZ,
  handFrontZ: LIM.handFrontZ,
  backZ: LIM.backZ,
};

const ZERO: Vec3 = { x: 0, y: 0, z: 0 };

const _v1 = new Vector3();
const _v2 = new Vector3();
const _v3 = new Vector3();
const _v4 = new Vector3();
const _v5 = new Vector3();
const _p0 = new Vector3();
const _p1 = new Vector3();
const _cross = new Vector3();
const _axisY = new Vector3();
const _axisZ = new Vector3();
const _nat = new Vector3(-0.25, -0.3, 1).normalize();
/**
 * Thumb axes in the normalized left-hand rest frame (measured on vera-maid: thumb
 * points (0.76, -0.04, 0.65), palm faces -Y). Metacarpal: opposition toward the
 * palm and across to the little finger. Proximal/distal: flexion across the palm.
 */
const THUMB_DIR = new Vector3(0.76, -0.04, 0.65).normalize();
const THUMB_AXES: readonly Vector3[] = [
  new Vector3().crossVectors(THUMB_DIR, new Vector3(0.2, -0.75, -0.6).normalize()).normalize(),
  new Vector3().crossVectors(THUMB_DIR, new Vector3(0, -0.6, -0.8).normalize()).normalize(),
  new Vector3().crossVectors(THUMB_DIR, new Vector3(-0.1, -0.5, -0.85).normalize()).normalize(),
];
/** Per-segment thumb flex (rad) at thumb = 1 (rest / clasp / most poses). */
const THUMB_CURL = [0.75, 0.55, 0.75] as const;
/** Stronger tuck for point/sidePoint (`ArmDef.thumbClosed`). */
const THUMB_CURL_CLOSED = [1.15, 1.05, 1.1] as const;
const _up = new Vector3(0, 1, 0);
const _qAim = new Quaternion();
const _qLift = new Quaternion();
const _qUpper = new Quaternion();
const _qLower = new Quaternion();
const _qHand = new Quaternion();
const _qFore = new Quaternion();
const _qInv = new Quaternion();
const _qTmp = new Quaternion();
const _basis = new Matrix4();
const _worldA = new Vector3();
const _worldB = new Vector3();

const _safe = createArmDef();
const _bad = createArmDef();
const _mid = createArmDef();
const _restL = createArmDef();
const _restR = createArmDef();
const _limitedL = createArmDef();
const _limitedR = createArmDef();
const _stepGoal = createArmDef();
const _stepMix = createArmDef();
const _stepPick = createArmDef();
const _savedUpper = new Quaternion();
const _savedLower = new Quaternion();

/** Upper arm and forearm may not turn more than this between frames at 60 Hz. */
export const MAX_ARM_STEP_RAD = 0.05;
const ARM_STEP_SEARCH = 10;

function createArmDef(): ArmDef {
  return {
    d: { x: 0, y: -1, z: 0 },
    f: { x: 0, y: -1, z: 0 },
    palm: { x: 0, y: -1, z: 0 },
    palmW: 0,
    wrist: [0, 0],
    curl: [0, 0, 0, 0],
    thumb: 0,
    spread: 0,
  };
}

function setVec(out: Vec3, x: number, y: number, z: number): void {
  out.x = x;
  out.y = y;
  out.z = z;
}

function copyVec(out: Vec3, src: Vec3): void {
  out.x = src.x;
  out.y = src.y;
  out.z = src.z;
}

function normalizeVec(v: Vec3): void {
  const len = Math.hypot(v.x, v.y, v.z);
  if (len < 1e-8) {
    v.x = 0;
    v.y = -1;
    v.z = 0;
    return;
  }
  v.x /= len;
  v.y /= len;
  v.z /= len;
}

function clamp(value: number, min: number, max: number): number {
  if (value < min) {
    return min;
  }
  if (value > max) {
    return max;
  }
  return value;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function smoothstep(edge0: number, edge1: number, x: number): number {
  const span = edge1 - edge0;
  const p = span === 0 ? 1 : clamp((x - edge0) / span, 0, 1);
  return p * p * (3 - 2 * p);
}

/** Deterministic zero-mean noise. `seed` differs per arm. */
export function restNoise(t: number, seed: number): number {
  return (
    0.5 * Math.sin(0.71 * t + 1.7 * seed) +
    0.3 * Math.sin(1.37 * t + 3.1 * seed) +
    0.2 * Math.sin(2.23 * t + 5.3 * seed)
  );
}

export function copyDef(src: ArmDef, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  copyVec(dst.d, src.d);
  copyVec(dst.f, src.f);
  copyVec(dst.palm, src.palm);
  dst.palmW = src.palmW;
  dst.wrist[0] = src.wrist[0];
  dst.wrist[1] = src.wrist[1];
  dst.curl[0] = src.curl[0];
  dst.curl[1] = src.curl[1];
  dst.curl[2] = src.curl[2];
  dst.curl[3] = src.curl[3];
  dst.thumb = src.thumb;
  dst.spread = src.spread;
  dst.thumbClosed = src.thumbClosed;
  return dst;
}

export function mkDef(partial?: Partial<ArmDef>, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  const d = partial?.d ?? REST.d;
  const f = partial?.f ?? REST.f;
  const palm = partial?.palm ?? REST.palm;
  const wrist = partial?.wrist ?? REST.wrist;
  const curl = partial?.curl ?? REST.curl;
  setVec(dst.d, d.x, d.y, d.z);
  setVec(dst.f, f.x, f.y, f.z);
  normalizeVec(dst.d);
  normalizeVec(dst.f);
  setVec(dst.palm, palm.x, palm.y, palm.z);
  dst.palmW = partial?.palmW ?? REST.palmW;
  dst.wrist[0] = wrist[0];
  dst.wrist[1] = wrist[1];
  dst.curl[0] = curl[0];
  dst.curl[1] = curl[1];
  dst.curl[2] = curl[2];
  dst.curl[3] = curl[3];
  dst.thumb = partial?.thumb ?? REST.thumb;
  dst.spread = partial?.spread ?? REST.spread;
  dst.thumbClosed = partial?.thumbClosed;
  return dst;
}

function slerpVec(a: Vec3, b: Vec3, t: number, out: Vec3): void {
  _v1.set(a.x, a.y, a.z).normalize();
  _v2.set(b.x, b.y, b.z).normalize();
  const dot = clamp(_v1.dot(_v2), -1, 1);
  if (dot > 0.9995) {
    out.x = _v1.x + (_v2.x - _v1.x) * t;
    out.y = _v1.y + (_v2.y - _v1.y) * t;
    out.z = _v1.z + (_v2.z - _v1.z) * t;
    normalizeVec(out);
    return;
  }
  const theta = Math.acos(dot);
  const sinTheta = Math.sin(theta);
  if (sinTheta < 1e-5) {
    copyVec(out, a);
    normalizeVec(out);
    return;
  }
  const w1 = Math.sin((1 - t) * theta) / sinTheta;
  const w2 = Math.sin(t * theta) / sinTheta;
  out.x = _v1.x * w1 + _v2.x * w2;
  out.y = _v1.y * w1 + _v2.y * w2;
  out.z = _v1.z * w1 + _v2.z * w2;
  normalizeVec(out);
}

export function mixDef(a: ArmDef, b: ArmDef, t: number, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  slerpVec(a.d, b.d, t, dst.d);
  slerpVec(a.f, b.f, t, dst.f);
  slerpVec(a.palm, b.palm, t, dst.palm);
  dst.palmW = lerp(a.palmW, b.palmW, t);
  dst.wrist[0] = lerp(a.wrist[0], b.wrist[0], t);
  dst.wrist[1] = lerp(a.wrist[1], b.wrist[1], t);
  dst.curl[0] = lerp(a.curl[0], b.curl[0], t);
  dst.curl[1] = lerp(a.curl[1], b.curl[1], t);
  dst.curl[2] = lerp(a.curl[2], b.curl[2], t);
  dst.curl[3] = lerp(a.curl[3], b.curl[3], t);
  dst.thumb = lerp(a.thumb, b.thumb, t);
  dst.spread = lerp(a.spread, b.spread, t);
  dst.thumbClosed = lerp(a.thumbClosed ?? 0, b.thumbClosed ?? 0, t) || undefined;
  return dst;
}

function readDirections(def: ArmDef): void {
  _v1.set(def.d.x, def.d.y, def.d.z).normalize();
  _v2.set(def.f.x, def.f.y, def.f.z).normalize();
}

function flexAngle(): number {
  return Math.acos(clamp(_v1.dot(_v2), -1, 1));
}

function writePoints(elbow: Vec3, mid: Vec3, wrist: Vec3): void {
  const lu = metrics.lu;
  const lf = metrics.lf;
  const sx = metrics.shoulderX;
  elbow.x = sx + _v1.x * lu;
  elbow.y = _v1.y * lu;
  elbow.z = _v1.z * lu;
  const half = lf * 0.5;
  mid.x = elbow.x + _v2.x * half;
  mid.y = elbow.y + _v2.y * half;
  mid.z = elbow.z + _v2.z * half;
  wrist.x = elbow.x + _v2.x * lf;
  wrist.y = elbow.y + _v2.y * lf;
  wrist.z = elbow.z + _v2.z * lf;
}

function pointPenalty(p: Vec3, front: number): number {
  let penalty = 0;
  if (p.z < metrics.backZ) {
    penalty += metrics.backZ - p.z;
  }
  if (p.x < 0) {
    penalty += -p.x;
  }
  if (p.y >= LIM.yLow && p.y <= LIM.yHigh && Math.abs(p.x) < metrics.innerX && p.z < front) {
    penalty += front - p.z;
  }
  return penalty;
}

/** Positive when a sample enters the torso, crosses the midline, or passes the back plane. */
export function violation(def: ArmDef): number {
  readDirections(def);
  const flex = flexAngle();
  let penalty = 0;
  if (flex < LIM.flexMin) {
    penalty += LIM.flexMin - flex;
  } else if (flex > LIM.flexMax) {
    penalty += flex - LIM.flexMax;
  }
  writePoints(_v3, _v4, _v5);
  penalty += pointPenalty(_v3, metrics.frontZ);
  penalty += pointPenalty(_v4, metrics.frontZ);
  penalty += pointPenalty(_v5, metrics.handFrontZ);
  return penalty;
}

export function armPoints(def: ArmDef, out?: ArmPoints): ArmPoints {
  const dst = out ?? {
    elbow: { x: 0, y: 0, z: 0 },
    mid: { x: 0, y: 0, z: 0 },
    wrist: { x: 0, y: 0, z: 0 },
  };
  readDirections(def);
  writePoints(dst.elbow, dst.mid, dst.wrist);
  return dst;
}

export function limitDef(def: ArmDef, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  if (violation(def) <= 0) {
    return copyDef(def, dst);
  }
  copyDef(REST, _safe);
  normalizeVec(_safe.d);
  normalizeVec(_safe.f);
  copyDef(def, _bad);
  for (let step = 0; step < BISECT_STEPS; step += 1) {
    mixDef(_safe, _bad, 0.5, _mid);
    if (violation(_mid) <= 0) {
      copyDef(_mid, _safe);
    } else {
      copyDef(_mid, _bad);
    }
  }
  return copyDef(_safe, dst);
}

/** `(x, −y, −z, w)`. */
export function mirror(q: Quaternion, out?: Quaternion): Quaternion {
  const dst = out ?? new Quaternion();
  dst.set(q.x, -q.y, -q.z, q.w);
  return dst;
}

function mirrorInPlace(q: Quaternion): void {
  q.y = -q.y;
  q.z = -q.z;
}

/**
 * Side is a pure function of the target. Call it once when a gesture starts.
 * `+x` is Vera's left. Azimuth above 0.09 rad selects the left hand.
 */
export function chooseSide(target: Vec3, chest: Vec3 = ZERO, bodyYaw = 0): SideChoice {
  _v1.set(target.x - chest.x, target.y - chest.y, target.z - chest.z);
  if (bodyYaw !== 0) {
    _v1.applyAxisAngle(_up, -bodyYaw);
  }
  const az = Math.atan2(_v1.x, _v1.z);
  const side: ArmSide = az > SIDE_AZIMUTH ? "L" : DOMINANT_SIDE;
  const turn = Math.sign(az) * Math.min(Math.abs(az) * TURN_GAIN, TURN_MAX);
  return { side, turn };
}

/** Aim behind the body is clipped so the arm stays at the side. */
export function clipAim(dir: Vec3, out: Vec3): Vec3 {
  out.x = dir.x;
  out.y = dir.y;
  out.z = dir.z < AIM_Z_MIN ? AIM_Z_MIN : dir.z;
  normalizeVec(out);
  return out;
}

const _down: Vec3 = { x: 0, y: -1, z: 0 };
const _aim: Vec3 = { x: 0, y: 0, z: 1 };

export function openDef(out?: ArmDef): ArmDef {
  return mkDef({ curl: [CURL_OPEN[0], CURL_OPEN[1], CURL_OPEN[2], CURL_OPEN[3]] }, out);
}

/**
 * Natural point: upper arm stays near the ribs (not a T-pose), forearm aims
 * forward-and-slightly-out along `dir`, elbow softly bent. Index fully
 * extended; middle/ring/little firmly curled; thumb over the middle finger;
 * true palm down/in (dorsal +Y).
 */
export function pointDef(dir: Vec3, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  clipAim(dir, _aim);
  // Bias aim slightly down so the hand sits at mid-chest, not shoulder height.
  slerpVec(_aim, _down, 0.08, _aim);
  // Elbow near the ribs: upper arm mostly down, enough toward aim to clear the torso.
  slerpVec(REST.d, _aim, 0.38, dst.d);
  // Forearm locks onto the aim — clear forward point, not a sideways T-pose.
  setVec(dst.f, _aim.x, _aim.y, _aim.z);
  // True palm down/in → dorsal +Y up.
  setVec(dst.palm, -0.2, 0.95, 0.2);
  dst.palmW = 1;
  dst.wrist[0] = 0.0;
  dst.wrist[1] = 0.06;
  dst.curl[0] = CURL_POINT[0];
  dst.curl[1] = CURL_POINT[1];
  dst.curl[2] = CURL_POINT[2];
  dst.curl[3] = CURL_POINT[3];
  // Fully closed: tucked over the curled middle / into the palm — only index reads.
  dst.thumb = 1;
  dst.spread = 0;
  dst.thumbClosed = 1;
  return dst;
}

export function restDef(side: ArmSide, t: number, out?: ArmDef): ArmDef {
  const dst = out ?? createArmDef();
  const n = restNoise(t, side === "L" ? SEED_L : SEED_R);
  setVec(dst.d, REST.d.x + 0.012 * n, REST.d.y, REST.d.z + 0.01 * n);
  setVec(dst.f, REST.f.x, REST.f.y + 0.02 * n, REST.f.z);
  normalizeVec(dst.d);
  normalizeVec(dst.f);
  copyVec(dst.palm, REST.palm);
  dst.palmW = REST.palmW;
  dst.wrist[0] = REST.wrist[0] + 0.03 * n;
  dst.wrist[1] = REST.wrist[1];
  const seed = side === "L" ? SEED_L : SEED_R;
  for (let finger = 0; finger < 4; finger += 1) {
    // Slow, independent per finger so the hand never reads as a rigid block.
    dst.curl[finger] = REST.curl[finger]! + 0.02 * n + 0.035 * restNoise(0.45 * t, seed * 7 + finger * 3.3);
  }
  dst.thumb = REST.thumb;
  dst.spread = REST.spread;
  dst.thumbClosed = undefined;
  return dst;
}

/** Posture-layer weight. Framing gain when motion v2 is on; otherwise the solver stays off. */
export function postureArmWeight(motionV2: boolean, shot: CameraShot): number {
  if (!motionV2) {
    return 0;
  }
  return idleLifeArmGain(shot);
}

function bone(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

function aimUpper(out: Quaternion): void {
  const flex = flexAngle();
  const blend = smoothstep(0.06, 0.36, flex);
  _v3.copy(_v2).addScaledVector(_v1, -_v2.dot(_v1));
  if (_v3.lengthSq() > 1e-8) {
    _v3.normalize();
  } else {
    _v3.set(0, 0, 1);
  }
  _v4.copy(_nat).addScaledVector(_v1, -_nat.dot(_v1));
  if (_v4.lengthSq() > 1e-8) {
    _v4.normalize();
  } else {
    _v4.copy(_v3);
  }
  _axisZ.copy(_v4).lerp(_v3, blend).normalize();
  _axisY.crossVectors(_axisZ, _v1);
  if (_axisY.lengthSq() < 1e-8) {
    _axisY.set(0, 1, 0);
  } else {
    _axisY.normalize();
  }
  _axisZ.crossVectors(_v1, _axisY).normalize();
  _basis.makeBasis(_v1, _axisY, _axisZ);
  out.setFromRotationMatrix(_basis);
}

function signedAngleAround(from: Vector3, to: Vector3, axis: Vector3): number {
  _p0.copy(from).addScaledVector(axis, -from.dot(axis));
  _p1.copy(to).addScaledVector(axis, -to.dot(axis));
  if (_p0.lengthSq() < 1e-8 || _p1.lengthSq() < 1e-8) {
    return 0;
  }
  _p0.normalize();
  _p1.normalize();
  const dot = clamp(_p0.dot(_p1), -1, 1);
  _cross.crossVectors(_p0, _p1);
  const angle = Math.acos(dot);
  return _cross.dot(axis) < 0 ? -angle : angle;
}

function writeBone(target: Object3D | null, q: Quaternion): void {
  if (!target) {
    return;
  }
  target.quaternion.copy(q);
}

function writeFingers(vrm: VRM, def: ArmDef, side: ArmSide): void {
  for (let finger = 0; finger < FINGERS.length; finger += 1) {
    const chain = FINGERS[finger]!;
    const curlAmount = clamp(def.curl[finger] ?? 0, 0, 1);
    const splay = (SPREAD_BY_FINGER[finger] ?? 0) * def.spread * (1 - 0.6 * curlAmount);
    for (let segment = 0; segment < chain.length; segment += 1) {
      const part = chain[segment]!;
      const node = bone(vrm, side === "L" ? part.left : part.right);
      if (!node) {
        continue;
      }
      // VRM normalized left hand: fingers +X, palm -Y. Flexing toward the palm is -Z.
      // Distal joints follow the proximal ones less at low curl (relaxed hand).
      const segAmount = segment === 2 ? curlAmount * (0.55 + 0.45 * curlAmount) : curlAmount;
      _qTmp.setFromAxisAngle(_axisZ.set(0, 0, 1), -part.curl * segAmount);
      if (segment === 0 && splay !== 0) {
        _qHand.setFromAxisAngle(_axisY.set(0, 1, 0), splay);
        _qTmp.multiply(_qHand);
      }
      if (side === "R") {
        mirrorInPlace(_qTmp);
      }
      node.quaternion.copy(_qTmp);
    }
  }

  const thumbAmount = clamp(def.thumb, 0, 1);
  const tuck = clamp(def.thumbClosed ?? 0, 0, 1);
  for (let segment = 0; segment < THUMB.length; segment += 1) {
    const part = THUMB[segment]!;
    const node = bone(vrm, side === "L" ? part.left : part.right);
    if (!node) {
      continue;
    }
    const relaxed = (THUMB_CURL[segment] ?? part.curl) * (0.15 + thumbAmount);
    const closed = (THUMB_CURL_CLOSED[segment] ?? part.curl) * (0.12 + 1.05 * thumbAmount);
    _qTmp.setFromAxisAngle(THUMB_AXES[segment]!, lerp(relaxed, closed, tuck));
    if (side === "R") {
      mirrorInPlace(_qTmp);
    }
    node.quaternion.copy(_qTmp);
  }
}

export function solveArm(vrm: VRM, def: ArmDef, side: ArmSide, options?: SolveArmOptions): void {
  if (!vrm.humanoid) {
    return;
  }
  readDirections(def);
  aimUpper(_qAim);
  const phi = clamp(flexAngle(), LIM.flexMin, LIM.flexMax);
  _qLower.setFromAxisAngle(_axisY.set(0, 1, 0), -phi);

  const lift = clamp((_v1.y + 0.35) * 0.22, 0, SHOULDER_LIFT_MAX);
  _qLift.setFromAxisAngle(_axisZ.set(0, 0, 1), lift);
  _qInv.copy(_qLift).invert();
  _qUpper.copy(_qAim).premultiply(_qInv);

  _qFore.copy(_qAim).multiply(_qLower);
  _qHand.identity();
  if (def.palmW !== 0) {
    _v3.set(def.palm.x, def.palm.y, def.palm.z);
    if (_v3.lengthSq() > 1e-8) {
      _v3.normalize();
      _v4.set(0, 1, 0).applyQuaternion(_qFore);
      _v5.set(1, 0, 0).applyQuaternion(_qFore);
      const roll = clamp(signedAngleAround(_v4, _v3, _v5), -PALM_ROLL_MAX, PALM_ROLL_MAX) * def.palmW;
      _qHand.setFromAxisAngle(_axisY.set(1, 0, 0), roll);
    }
  }
  _qTmp.setFromAxisAngle(_axisZ.set(0, 0, 1), def.wrist[0]);
  _qHand.multiply(_qTmp);
  _qTmp.setFromAxisAngle(_axisY.set(0, 1, 0), def.wrist[1]);
  _qHand.multiply(_qTmp);

  if (side === "R") {
    mirrorInPlace(_qLift);
    mirrorInPlace(_qUpper);
    mirrorInPlace(_qLower);
    mirrorInPlace(_qHand);
  }

  const shoulderName = side === "L" ? VRMHumanBoneName.LeftShoulder : VRMHumanBoneName.RightShoulder;
  const upperName = side === "L" ? VRMHumanBoneName.LeftUpperArm : VRMHumanBoneName.RightUpperArm;
  const lowerName = side === "L" ? VRMHumanBoneName.LeftLowerArm : VRMHumanBoneName.RightLowerArm;
  const handName = side === "L" ? VRMHumanBoneName.LeftHand : VRMHumanBoneName.RightHand;
  const shoulder = bone(vrm, shoulderName);
  const upper = bone(vrm, upperName);
  if (shoulder) {
    writeBone(shoulder, _qLift);
    writeBone(upper, _qUpper);
  } else if (side === "R") {
    mirrorInPlace(_qAim);
    writeBone(upper, _qAim);
  } else {
    writeBone(upper, _qAim);
  }
  writeBone(bone(vrm, lowerName), _qLower);
  writeBone(bone(vrm, handName), _qHand);
  if (options?.fingers !== false) {
    writeFingers(vrm, def, side);
  }
}

function armBoneName(side: ArmSide, part: "upper" | "lower"): VRMHumanBoneName {
  if (side === "L") {
    return part === "upper" ? VRMHumanBoneName.LeftUpperArm : VRMHumanBoneName.LeftLowerArm;
  }
  return part === "upper" ? VRMHumanBoneName.RightUpperArm : VRMHumanBoneName.RightLowerArm;
}

function captureArmStep(vrm: VRM, side: ArmSide): void {
  const upper = bone(vrm, armBoneName(side, "upper"));
  const lower = bone(vrm, armBoneName(side, "lower"));
  if (upper) {
    _savedUpper.copy(upper.quaternion);
  } else {
    _savedUpper.identity();
  }
  if (lower) {
    _savedLower.copy(lower.quaternion);
  } else {
    _savedLower.identity();
  }
}

function armStepRadians(vrm: VRM, side: ArmSide): number {
  const upper = bone(vrm, armBoneName(side, "upper"));
  const lower = bone(vrm, armBoneName(side, "lower"));
  const upperStep = upper ? _savedUpper.angleTo(upper.quaternion) : 0;
  const lowerStep = lower ? _savedLower.angleTo(lower.quaternion) : 0;
  return Math.max(upperStep, lowerStep);
}

function poseArmStep(vrm: VRM, def: ArmDef, side: ArmSide, options?: SolveArmOptions): number {
  solveArm(vrm, def, side, options);
  return armStepRadians(vrm, side);
}

/**
 * Writes the furthest pose from `previous` toward `desired` that keeps the
 * upper arm and forearm under {@link MAX_ARM_STEP_RAD}. `out` may alias
 * `previous`. The weight envelope is unchanged: a long reach just takes more frames.
 */
export function applyArmStep(
  vrm: VRM,
  previous: ArmDef,
  desired: ArmDef,
  side: ArmSide,
  out: ArmDef,
  options?: SolveArmOptions,
): ArmDef {
  const goal = limitDef(desired, _stepGoal);
  solveArm(vrm, previous, side, options);
  captureArmStep(vrm, side);
  if (poseArmStep(vrm, goal, side, options) < MAX_ARM_STEP_RAD) {
    return copyDef(goal, out);
  }

  let low = 0;
  let high = 1;
  for (let step = 0; step < ARM_STEP_SEARCH; step += 1) {
    const mid = (low + high) / 2;
    mixDef(previous, goal, mid, _stepMix);
    limitDef(_stepMix, _stepPick);
    if (poseArmStep(vrm, _stepPick, side, options) < MAX_ARM_STEP_RAD) {
      low = mid;
    } else {
      high = mid;
    }
  }

  mixDef(previous, goal, low, _stepMix);
  limitDef(_stepMix, _stepPick);
  solveArm(vrm, _stepPick, side, options);
  return copyDef(_stepPick, out);
}

function worldDistance(a: Object3D, b: Object3D): number {
  a.getWorldPosition(_worldA);
  b.getWorldPosition(_worldB);
  return _worldA.distanceTo(_worldB);
}

/** Measures `Lu`, `Lf`, and torso width. Scales the four spatial limits. No-op if a bone is missing. */
export function measureArmRig(vrm: VRM): void {
  const humanoid = vrm.humanoid;
  if (!humanoid) {
    return;
  }
  const upper = humanoid.getNormalizedBoneNode(VRMHumanBoneName.LeftUpperArm);
  const lower = humanoid.getNormalizedBoneNode(VRMHumanBoneName.LeftLowerArm);
  const hand = humanoid.getNormalizedBoneNode(VRMHumanBoneName.LeftHand);
  const rightUpper = humanoid.getNormalizedBoneNode(VRMHumanBoneName.RightUpperArm);
  if (!upper || !lower || !hand || !rightUpper) {
    return;
  }
  vrm.scene.updateMatrixWorld(true);
  const lu = worldDistance(upper, lower);
  const lf = worldDistance(lower, hand);
  const width = worldDistance(upper, rightUpper);
  if (lu > 1e-4) {
    metrics.lu = lu;
  }
  if (lf > 1e-4) {
    metrics.lf = lf;
  }
  const referenceWidth = LIM.shoulderX * 2;
  if (width > 1e-4 && referenceWidth > 0) {
    const scale = width / referenceWidth;
    metrics.shoulderX = width * 0.5;
    metrics.innerX = LIM.innerX * scale;
    metrics.frontZ = LIM.frontZ * scale;
    metrics.handFrontZ = LIM.handFrontZ * scale;
    metrics.backZ = LIM.backZ * scale;
  }
}

/** Rest pose plus zero-mean noise, then the body limit. One writer for both arms. */
export function applyRestArms(vrm: VRM, timeSec: number, shot: CameraShot): void {
  restDef("L", timeSec, _restL);
  restDef("R", timeSec, _restR);
  limitDef(_restL, _limitedL);
  limitDef(_restR, _limitedR);
  void shot; // fingers are written in every shot so framing changes never pop
  const fingers = true;
  solveArm(vrm, _limitedL, "L", { fingers });
  solveArm(vrm, _limitedR, "R", { fingers });
}
