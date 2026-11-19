import { CURL_RELAX, copyDef, mixDef, mkDef, pointDef, type ArmDef, type ArmSide } from "./arms";
import { containPose } from "./gestureCatalog";
import type { GestureChannel, GestureHands, GestureHead } from "./gestureQueue";

/**
 * Procedural maid gestures and idle fidgets (no clips). Each entry owns a
 * time-varying arm pose per side plus optional body, head and smile overlays.
 * Arm frame: x outward, y up, z forward (left arm; the right one mirrors).
 */

export type BodyBone =
  | "hips"
  | "spine"
  | "chest"
  | "upperChest"
  | "neck"
  | "leftShoulder"
  | "rightShoulder"
  | "leftUpperLeg"
  | "rightUpperLeg"
  | "leftLowerLeg"
  | "rightLowerLeg";

export interface BodyOffset {
  bone: BodyBone;
  x: number;
  y: number;
  z: number;
}

export interface MotionFrame {
  body: BodyOffset[];
  head: GestureHead;
  /** 0..1 added to `happy` on top of the face blend. */
  smile: number;
}

export interface MotionGesture {
  channel: GestureChannel;
  hands: GestureHands;
  durationMs: number;
  /** Arm pose at progress p (0..1) and elapsed seconds t. Null = rest arm. */
  pose?: (side: ArmSide, p: number, t: number, out: ArmDef) => ArmDef | null;
  frame?: (p: number, t: number, out: MotionFrame) => void;
}

/** Containment scale per authored pose (1 = reached as designed). */
export const MOTION_POSE_SCALE: number[] = [];
export const MOTION_POSE_DESIRED: ArmDef[] = [];
const contained = (def: ArmDef): ArmDef => {
  const result = containPose(def);
  MOTION_POSE_SCALE.push(result.scale);
  MOTION_POSE_DESIRED.push(def);
  return result.pose;
};

/** Bell 0→1→0 over the gesture, flat in the middle. */
export function bell(p: number, edge = 0.25): number {
  if (p <= 0 || p >= 1) {
    return 0;
  }
  const a = Math.min(1, p / edge);
  const b = Math.min(1, (1 - p) / edge);
  const s = Math.min(a, b);
  return s * s * (3 - 2 * s);
}

const smooth = (x: number) => {
  const c = Math.max(0, Math.min(1, x));
  return c * c * (3 - 2 * c);
};

const curl = (a: number, b: number, c: number, d: number): [number, number, number, number] => [a, b, c, d];

/** Directions from target elbow and wrist points (arm frame, shoulder at x=0.1). */
function aim(elbow: [number, number, number], wrist: [number, number, number]): Pick<ArmDef, "d" | "f"> {
  const n = (x: number, y: number, z: number) => {
    const l = Math.hypot(x, y, z) || 1;
    return { x: x / l, y: y / l, z: z / l };
  };
  return {
    d: n(elbow[0] - 0.1, elbow[1], elbow[2]),
    f: n(wrist[0] - elbow[0], wrist[1] - elbow[1], wrist[2] - elbow[2]),
  };
}

// ---------------------------------------------------------------- poses
/**
 * Stacked polite hands (maid rest / sad / bow), matching the apron clasp ref:
 * arms relaxed down, elbows in, wrists at waist/hip. Character's RIGHT = lower
 * (edge-on, back out); LEFT = upper (palm on the lower metacarpal back, fingers
 * straight together pointing down-in). Both defs authored in left-arm frame;
 * {@link claspDef} maps LOWER→R (mirrored) and UPPER→L.
 */
export const CLASP_LOWER = contained(mkDef({
  // Right hand (after mirror): waist line, edge-on / back toward camera, soft curl.
  // ArmDef.palm aligns hand local +Y (back of hand); +Z ⇒ back out to the camera.
  ...aim([0.13, -0.30, 0.08], [0.05, -0.42, 0.16]),
  palm: { x: 0.5, y: 0.1, z: 0.85 },
  palmW: 1,
  wrist: [-0.02, 0.1],
  curl: curl(0.36, 0.42, 0.5, 0.58),
  thumb: 0.58,
  spread: 0.03,
}));

/** Left hand on top of {@link CLASP_LOWER}. Authored in left-arm frame (applied as L). */
export const CLASP_UPPER = contained(mkDef({
  // Left hand lies on the lower metacarpal back. ArmDef.palm +Y ⇒ true palm down
  // (local -Y). Fingers straight & closed, diagonal down-in; thumb tucked.
  // Authored at containPose scale 1 (no REST mix) so curl/wrist stay as written.
  ...aim([0.14, -0.24, 0.10], [0.025, -0.385, 0.20]),
  palm: { x: 0.22, y: 0.93, z: 0.28 },
  palmW: 1,
  wrist: [0.03, -0.2],
  curl: curl(0.08, 0.10, 0.12, 0.14),
  thumb: 0.95,
  spread: 0.0,
}));

/** @deprecated Prefer {@link claspDef}; kept as the lower hand for old imports. */
export const CLASP_POSE = CLASP_LOWER;

export function claspDef(side: ArmSide, out?: ArmDef): ArmDef {
  // Ref: character's right = lower, left = upper.
  return copyDef(side === "R" ? CLASP_LOWER : CLASP_UPPER, out);
}

export const HAND_CHEST_POSE = contained(mkDef({
  ...aim([0.16, -0.22, 0.17], [0.06, -0.15, 0.26]),
  palm: { x: -0.2, y: 0.05, z: -1 },
  palmW: 0.9,
  wrist: [-0.05, 0],
  curl: curl(0.12, 0.16, 0.22, 0.3),
  thumb: 0.3,
  spread: 0.25,
}));

const HAIR_TUCK_POSE = contained(mkDef({
  d: { x: 0.62, y: -0.28, z: 0.55 },
  f: { x: -0.3, y: 0.92, z: 0.02 },
  palm: { x: -0.95, y: 0, z: -0.25 },
  palmW: 0.9,
  wrist: [0.25, 0],
  curl: curl(0.18, 0.25, 0.5, 0.6),
  thumb: 0.4,
  spread: 0.15,
}));

const HAIR_TUCK_BACK = contained(mkDef({
  ...aim([0.3, -0.08, 0.15], [0.2, 0.13, 0.13]),
  palm: { x: -0.95, y: 0, z: -0.1 },
  palmW: 0.9,
  wrist: [0.15, 0],
  curl: curl(0.25, 0.3, 0.55, 0.65),
  thumb: 0.45,
  spread: 0.1,
}));

/** Palms flat on the apron (true palm into fabric ≈ −Z → dorsal +Z). */
const APRON_HIGH = contained(mkDef({
  d: { x: 0.12, y: -0.94, z: 0.3 },
  f: { x: -0.22, y: -0.55, z: 0.8 },
  palm: { x: -0.1, y: 0.15, z: 0.98 },
  palmW: 1,
  wrist: [-0.06, 0.02],
  curl: curl(0.12, 0.16, 0.22, 0.28),
  thumb: 0.22,
  spread: 0.12,
}));

const APRON_LOW = contained(mkDef({
  ...aim([0.14, -0.26, 0.07], [0.08, -0.45, 0.16]),
  palm: { x: -0.1, y: 0.1, z: 0.98 },
  palmW: 1,
  wrist: [-0.08, 0.02],
  curl: curl(0.14, 0.18, 0.24, 0.3),
  thumb: 0.22,
  spread: 0.1,
}));

/** Left forearm across the belly, right fingers on the left wristband. */
const WRIST_HOLD_L = contained(mkDef({
  ...aim([0.13, -0.22, 0.12], [0.02, -0.27, 0.3]),
  palm: { x: 0, y: -1, z: -0.2 },
  palmW: 0.8,
  wrist: [0.1, 0],
  curl: CURL_RELAX.slice() as [number, number, number, number],
  thumb: 0.3,
  spread: 0.2,
}));

const WRIST_GRAB_R = contained(mkDef({
  ...aim([0.14, -0.2, 0.15], [0.015, -0.24, 0.3]),
  palm: { x: -0.4, y: -0.9, z: 0 },
  palmW: 0.8,
  wrist: [0.25, 0],
  curl: curl(0.55, 0.6, 0.7, 0.75),
  thumb: 0.7,
  spread: 0.05,
}));

/** Hand in front of the mouth (yawn, laugh). */
const MOUTH_POSE = contained(mkDef({
  ...aim([0.13, -0.2, 0.16], [0.035, -0.035, 0.26]),
  palm: { x: -0.15, y: 0, z: -1 },
  palmW: 0.9,
  wrist: [0.05, 0],
  curl: curl(0.15, 0.2, 0.28, 0.36),
  thumb: 0.35,
  spread: 0.1,
}));

/** Meet at chest: dorsal +X ⇒ true palm faces inward (toward the other hand). */
const CLAP_POSE = contained(mkDef({
  ...aim([0.14, -0.2, 0.18], [0.02, -0.08, 0.32]),
  palm: { x: 1, y: 0.05, z: 0.05 },
  palmW: 1,
  wrist: [0.04, 0],
  curl: curl(0.08, 0.1, 0.14, 0.2),
  thumb: 0.2,
  spread: 0.08,
}));

const CLAP_OPEN = contained(mkDef({
  ...aim([0.18, -0.22, 0.2], [0.08, -0.1, 0.34]),
  palm: { x: 1, y: 0.05, z: 0.05 },
  palmW: 1,
  wrist: [0.04, 0],
  curl: curl(0.08, 0.1, 0.14, 0.2),
  thumb: 0.2,
  spread: 0.12,
}));

const FIST_POSE = contained(mkDef({
  d: { x: 0.22, y: -0.9, z: 0.36 },
  f: { x: -0.1, y: 0.62, z: 0.78 },
  palm: { x: -1, y: 0, z: 0 },
  palmW: 1,
  wrist: [0, 0],
  curl: curl(0.95, 0.95, 0.95, 0.95),
  thumb: 0.95,
  spread: 0,
}));

const COUNT_POSE = contained(mkDef({
  d: { x: 0.2, y: -0.86, z: 0.46 },
  f: { x: -0.25, y: 0.62, z: 0.74 },
  palm: { x: -0.1, y: 0, z: -1 },
  palmW: 0.9,
  wrist: [-0.1, 0],
  curl: curl(0.9, 0.9, 0.9, 0.9),
  thumb: 0.85,
  spread: 0.3,
}));

/** Speaking "options": palms UP and slightly outward. Dorsal y:-1. */
const OPEN_PALMS_POSE = contained(mkDef({
  d: { x: 0.32, y: -0.9, z: 0.28 },
  f: { x: 0.28, y: 0.05, z: 0.96 },
  palm: { x: 0.12, y: -0.98, z: 0.12 },
  palmW: 1,
  wrist: [0.04, 0.06],
  curl: curl(0.1, 0.14, 0.2, 0.28),
  thumb: 0.18,
  spread: 0.5,
}));

/** Closing offer toward camera: palm UP, relaxed fingers. Dorsal y:-1. */
const OPEN_HAND_POSE = contained(mkDef({
  d: { x: 0.22, y: -0.92, z: 0.32 },
  f: { x: 0.05, y: 0.2, z: 0.98 },
  palm: { x: -0.05, y: -0.98, z: 0.15 },
  palmW: 1,
  wrist: [0.04, 0],
  curl: curl(0.12, 0.16, 0.22, 0.3),
  thumb: 0.22,
  spread: 0.4,
}));

const SIDE_POINT_POSE = contained(pointDef({ x: 0.36, y: -0.04, z: 0.93 }));

export const MOTION_POSES = {
  CLASP_LOWER,
  CLASP_UPPER,
  HAND_CHEST_POSE,
  HAIR_TUCK_POSE,
  HAIR_TUCK_BACK,
  APRON_HIGH,
  APRON_LOW,
  WRIST_HOLD_L,
  WRIST_GRAB_R,
  MOUTH_POSE,
  CLAP_POSE,
  CLAP_OPEN,
  FIST_POSE,
  COUNT_POSE,
  OPEN_PALMS_POSE,
  OPEN_HAND_POSE,
  SIDE_POINT_POSE,
} as const;

// ---------------------------------------------------------------- helpers
function body(out: MotionFrame, bone: BodyBone, x: number, y = 0, z = 0): void {
  out.body.push({ bone, x, y, z });
}


// ---------------------------------------------------------------- registry
export const MOTION_GESTURES: Readonly<Record<string, MotionGesture>> = {
  // Idle fidgets
  hairTuck: {
    channel: "arm",
    hands: "R",
    durationMs: 2400,
    pose: (_s, p, _t, out) => mixDef(HAIR_TUCK_POSE, HAIR_TUCK_BACK, smooth((p - 0.35) / 0.35), out),
    frame: (p, _t, out) => {
      const k = bell(p);
      out.head.roll = -0.1 * k;
      out.head.pitch = 0.04 * k;
      out.head.yaw = 0.06 * k;
    },
  },
  apron: {
    channel: "arm",
    hands: "both",
    durationMs: 2800,
    pose: (side, p, _t, out) => {
      // One smooth slide down the thighs (both palms flat), not a frantic scrub.
      const lag = side === "R" ? 0.06 : 0;
      const s = smooth(Math.min(1, Math.max(0, (p - 0.12 - lag) / 0.7)));
      return mixDef(APRON_HIGH, APRON_LOW, s, out);
    },
    frame: (p, _t, out) => {
      const k = bell(p, 0.2);
      body(out, "spine", 0.04 * k);
      out.head.pitch = 0.14 * k;
    },
  },
  clasp: {
    channel: "arm",
    hands: "both",
    durationMs: 4200,
    pose: (side, _p, t, out) => {
      claspDef(side, out);
      out.curl[0] += 0.04 * Math.sin(t * 1.3 + (side === "R" ? 1 : 0));
      return out;
    },
  },
  rubFingers: {
    channel: "arm",
    hands: "both",
    durationMs: 3200,
    pose: (side, _p, t, out) => {
      // Stay on the approved clasp; only the upper (L) thumb rubs lightly.
      claspDef(side, out);
      if (side === "L") {
        out.thumb = Math.max(0.55, Math.min(1, out.thumb + 0.12 * Math.sin(t * 6.5)));
        out.wrist[1] += 0.03 * Math.sin(t * 6.5);
      }
      return out;
    },
    frame: (p, _t, out) => {
      out.head.pitch = 0.08 * bell(p);
    },
  },
  wristband: {
    channel: "arm",
    hands: "both",
    durationMs: 2600,
    pose: (side, _p, t, out) => {
      if (side === "L") {
        copyDef(WRIST_HOLD_L, out);
        out.wrist[1] += 0.12 * Math.sin(t * 3);
        return out;
      }
      copyDef(WRIST_GRAB_R, out);
      out.wrist[0] += 0.15 * Math.sin(t * 6);
      return out;
    },
    frame: (p, _t, out) => {
      const k = bell(p);
      out.head.pitch = 0.22 * k;
      out.head.roll = 0.05 * k;
    },
  },
  yawn: {
    channel: "arm",
    hands: "R",
    durationMs: 3400,
    pose: (_s, _p, _t, out) => copyDef(MOUTH_POSE, out),
    frame: (p, _t, out) => {
      const k = bell(p, 0.3);
      out.head.pitch = -0.16 * k;
      body(out, "chest", -0.05 * k);
      body(out, "leftShoulder", 0, 0, 0.05 * k);
      body(out, "rightShoulder", 0, 0, -0.05 * k);
    },
  },
  sigh: {
    channel: "head",
    hands: "none",
    durationMs: 2800,
    frame: (p, _t, out) => {
      const inhale = Math.sin(Math.min(1, p / 0.45) * Math.PI * 0.5);
      const exhale = smooth((p - 0.45) / 0.45);
      const k = inhale * (1 - exhale) + 0 * exhale;
      body(out, "chest", -0.05 * k);
      body(out, "leftShoulder", 0, 0, 0.06 * k - 0.03 * exhale * bell(p, 0.1));
      body(out, "rightShoulder", 0, 0, -0.06 * k + 0.03 * exhale * bell(p, 0.1));
      out.head.pitch = -0.06 * k + 0.08 * exhale * bell(p, 0.15);
    },
  },
  neckStretch: {
    channel: "head",
    hands: "none",
    durationMs: 3000,
    frame: (p, _t, out) => {
      const k = bell(p, 0.2);
      out.head.roll = 0.22 * Math.sin(p * Math.PI * 2) * k;
      out.head.pitch = 0.06 * k;
    },
  },
  // Maid gestures
  bow: {
    channel: "arm",
    hands: "both",
    durationMs: 2300,
    pose: (side, _p, _t, out) => claspDef(side, out),
    frame: (p, _t, out) => {
      const k = bell(p, 0.35);
      body(out, "spine", 0.22 * k);
      body(out, "chest", 0.12 * k);
      body(out, "upperChest", 0.06 * k);
      body(out, "neck", 0.04 * k);
      // Curtsy: a soft dip of both knees, right foot a touch behind.
      body(out, "leftUpperLeg", -0.06 * k);
      body(out, "leftLowerLeg", 0.12 * k);
      body(out, "rightUpperLeg", 0.1 * k);
      body(out, "rightLowerLeg", 0.16 * k);
      out.head.pitch = 0.12 * k;
      out.smile = 0.35 * k;
    },
  },
  handChest: {
    channel: "arm",
    hands: "R",
    durationMs: 2600,
    pose: (_s, _p, _t, out) => copyDef(HAND_CHEST_POSE, out),
    frame: (p, _t, out) => {
      const k = bell(p);
      out.head.roll = 0.09 * k;
      out.head.pitch = 0.05 * k;
      body(out, "chest", -0.02 * k);
      out.smile = 0.55 * k;
    },
  },
  // Reactions (phase B)
  tapFingers: {
    channel: "arm",
    hands: "both",
    durationMs: 3000,
    pose: (side, _p, t, out) => {
      claspDef(side, out);
      if (side === "R") {
        out.curl[0] = 0.25 + 0.3 * Math.max(0, Math.sin(t * 9));
        out.curl[1] = 0.3 + 0.3 * Math.max(0, Math.sin(t * 9 - 0.9));
      }
      return out;
    },
  },
  clap: {
    channel: "arm",
    hands: "both",
    durationMs: 1700,
    pose: (_s, p, _t, out) => {
      // Small light claps at chest: short travel between meet and a slight open.
      const open = 0.35 + 0.35 * Math.cos(p * Math.PI * 2 * 4);
      return mixDef(CLAP_POSE, CLAP_OPEN, open * (p > 0.88 ? 1 : 0.85), out);
    },
    frame: (p, _t, out) => {
      out.smile = 0.55 * bell(p);
    },
  },
  happyFist: {
    channel: "arm",
    hands: "R",
    durationMs: 1500,
    pose: (_s, p, _t, out) => {
      copyDef(FIST_POSE, out);
      const pump = Math.max(0, Math.sin(p * Math.PI * 2));
      out.f.y += 0.15 * pump;
      return out;
    },
    frame: (p, _t, out) => {
      out.smile = 0.65 * bell(p);
      out.head.pitch = -0.04 * bell(p);
    },
  },
  sadClasp: {
    channel: "arm",
    hands: "both",
    durationMs: 3200,
    pose: (side, _p, _t, out) => claspDef(side, out),
    frame: (p, _t, out) => {
      const k = bell(p);
      out.head.roll = 0.14 * k;
      out.head.pitch = 0.12 * k;
      body(out, "leftShoulder", 0, 0, -0.03 * k);
      body(out, "rightShoulder", 0, 0, 0.03 * k);
    },
  },
  laugh: {
    channel: "arm",
    hands: "R",
    durationMs: 2200,
    pose: (_s, _p, _t, out) => copyDef(MOUTH_POSE, out),
    frame: (p, t, out) => {
      const k = bell(p);
      const bounce = Math.abs(Math.sin(t * 11)) * k;
      body(out, "leftShoulder", 0, 0, 0.05 * bounce);
      body(out, "rightShoulder", 0, 0, -0.05 * bounce);
      body(out, "spine", 0.04 * k);
      out.head.pitch = 0.05 * k;
      out.head.roll = -0.06 * k;
      out.smile = 0.8 * k;
    },
  },
  surpriseStep: {
    channel: "arm",
    hands: "R",
    durationMs: 1900,
    pose: (_s, _p, _t, out) => copyDef(HAND_CHEST_POSE, out),
    frame: (p, _t, out) => {
      const k = bell(p, 0.15);
      body(out, "spine", -0.07 * k);
      body(out, "chest", -0.04 * k);
      body(out, "rightUpperLeg", 0.12 * k);
      body(out, "rightLowerLeg", 0.08 * k);
      out.head.pitch = -0.08 * k;
    },
  },
  count: {
    channel: "arm",
    hands: "R",
    durationMs: 3600,
    pose: (_s, p, _t, out) => {
      copyDef(COUNT_POSE, out);
      const shown = Math.min(4, 1 + Math.floor(p * 4.2));
      for (let i = 0; i < 4; i += 1) {
        out.curl[i] = i < shown ? 0.05 : 0.9;
      }
      return out;
    },
  },
  openPalms: {
    channel: "arm",
    hands: "both",
    durationMs: 2200,
    pose: (side, _p, t, out) => {
      copyDef(OPEN_PALMS_POSE, out);
      out.f.y += 0.06 * Math.sin(t * 2.2 + (side === "R" ? Math.PI : 0));
      return out;
    },
  },
  sidePoint: {
    channel: "arm",
    hands: "R",
    durationMs: 1800,
    pose: (_s, _p, _t, out) => copyDef(SIDE_POINT_POSE, out),
    frame: (p, _t, out) => {
      out.head.yaw = -0.12 * bell(p);
    },
  },
  openHand: {
    channel: "arm",
    hands: "R",
    durationMs: 1600,
    pose: (_s, _p, _t, out) => copyDef(OPEN_HAND_POSE, out),
    frame: (p, _t, out) => {
      out.head.pitch = 0.1 * Math.sin(p * Math.PI) * bell(p);
      out.smile = 0.3 * bell(p);
    },
  },
};

export const IDLE_FIDGETS = ["hairTuck", "apron", "clasp", "rubFingers", "wristband"] as const;

export function motionGesture(name: string): MotionGesture | undefined {
  return MOTION_GESTURES[name];
}

export function createMotionFrame(): MotionFrame {
  return { body: [], head: { pitch: 0, yaw: 0, roll: 0 }, smile: 0 };
}

export function resetMotionFrame(out: MotionFrame): MotionFrame {
  out.body.length = 0;
  out.head.pitch = 0;
  out.head.yaw = 0;
  out.head.roll = 0;
  out.smile = 0;
  return out;
}
