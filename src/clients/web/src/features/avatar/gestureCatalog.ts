import {
  CURL_OPEN,
  REST,
  copyDef,
  mixDef,
  mkDef,
  pointDef,
  violation,
  type ArmDef,
  type Vec3,
} from "./arms";

/**
 * New gestures live here. `nod` is not redefined: the hub cue still uses
 * `applyGesture`. Peak poses are pulled toward `REST` until every sample
 * on that blend has `violation === 0`.
 */

export const THINK_HEAD = { pitch: 0.1, yaw: 0, roll: 0.1 } as const;

export interface CatalogScale {
  explain: number;
  think: number;
  hold: number;
  hands: number;
}

const DESIRED_EXPLAIN = mkDef({
  d: { x: 0.22, y: -0.94, z: 0.28 },
  f: { x: 0.04, y: -0.55, z: 0.78 },
  palm: { x: -0.2, y: 1, z: 0.15 },
  palmW: 1,
  wrist: [0.08, 0.04],
  curl: [CURL_OPEN[0], CURL_OPEN[1], CURL_OPEN[2], CURL_OPEN[3]],
  thumb: 0.2,
  spread: 0.5,
});

/**
 * Hand to the chin: elbow down and forward, forearm up and in, back of the
 * loosely curled hand forward, wrist flexed so the knuckles sit under the chin.
 */
const DESIRED_THINK = mkDef({
  d: { x: 0.1, y: -0.71, z: 0.7 },
  f: { x: -0.42, y: 0.85, z: 0.45 },
  palm: { x: -0.25, y: 0.2, z: -0.95 },
  palmW: 0.9,
  wrist: [0.35, 0],
  curl: [0.5, 0.6, 0.68, 0.76],
  thumb: 0.5,
  spread: 0.1,
});

/**
 * Presenting hand for a tool icon: elbow by the waist, forearm forward and a bit
 * out at lower-chest height, palm UP (as if holding something), fingers softly
 * cupped, thumb relaxed. ArmDef.palm is dorsal: y:-1 ⇒ true palm up.
 */
const DESIRED_HOLD = mkDef({
  d: { x: 0.2, y: -0.95, z: 0.16 },
  f: { x: 0.14, y: 0.42, z: 0.9 },
  palm: { x: -0.05, y: -1, z: 0.05 },
  palmW: 1,
  wrist: [0.05, 0],
  curl: [0.2, 0.25, 0.31, 0.38],
  thumb: 0.25,
  spread: 0.3,
});

/**
 * Ready / greeting present: both hands offer forward at mid-chest, palms UP,
 * fingers softly open. Played once when the avatar finishes loading (`hands`
 * rising edge in the director). Dorsal palm y:-1 ⇒ true palm up.
 */
const DESIRED_HANDS = mkDef({
  d: { x: 0.28, y: -0.88, z: 0.28 },
  f: { x: 0.08, y: 0.15, z: 0.98 },
  palm: { x: 0.05, y: -0.98, z: 0.15 },
  palmW: 1,
  wrist: [0.06, 0.04],
  curl: [0.12, 0.16, 0.22, 0.28],
  thumb: 0.2,
  spread: 0.45,
});

function blendsClean(pose: ArmDef): boolean {
  if (violation(pose) > 0) {
    return false;
  }
  for (let step = 0; step <= 10; step += 1) {
    if (violation(mixDef(REST, pose, step / 10)) > 0) {
      return false;
    }
  }
  return true;
}

/** Largest blend of `desired` over `REST` that stays inside `limitDef`. */
export function containPose(desired: ArmDef): { pose: ArmDef; scale: number } {
  if (blendsClean(desired)) {
    return { pose: copyDef(desired), scale: 1 };
  }
  let low = 0;
  let high = 1;
  for (let step = 0; step < 16; step += 1) {
    const mid = (low + high) / 2;
    if (blendsClean(mixDef(REST, desired, mid))) {
      low = mid;
    } else {
      high = mid;
    }
  }
  return { pose: mixDef(REST, desired, low), scale: low };
}

/**
 * Classic wave: forearm up beside the shoulder, open palm facing the camera,
 * fingers together / slightly spread. Side-to-side swing is layered in
 * `applyLegacyArm`. Dorsal palm z:-1 ⇒ true palm toward +Z (camera).
 */
const DESIRED_WAVE = mkDef({
  // Hand up beside the head: open palm to camera, fingers up.
  d: { x: 0.42, y: -0.5, z: 0.45 },
  f: { x: 0.08, y: 0.98, z: 0.18 },
  palm: { x: 0.0, y: 0.05, z: -1 },
  palmW: 1,
  wrist: [0.0, 0],
  curl: [0.06, 0.08, 0.12, 0.18],
  thumb: 0.18,
  spread: 0.22,
});

/**
 * Natural shrug arms: elbows close to the ribs, forearms out a bit with palms
 * UP. Shoulder lift + head tilt are layered in `applyLegacyArm`. Dorsal y:-1.
 */
const DESIRED_SHRUG = mkDef({
  d: { x: 0.18, y: -0.96, z: 0.18 },
  f: { x: 0.55, y: -0.05, z: 0.83 },
  palm: { x: 0.1, y: -0.98, z: 0.1 },
  palmW: 1,
  wrist: [0.04, 0.08],
  curl: [0.1, 0.14, 0.2, 0.28],
  thumb: 0.18,
  spread: 0.4,
});

const explain = containPose(DESIRED_EXPLAIN);
const think = containPose(DESIRED_THINK);
const hold = containPose(DESIRED_HOLD);
const hands = containPose(DESIRED_HANDS);
const wave = containPose(DESIRED_WAVE);
const shrug = containPose(DESIRED_SHRUG);

/** Arm poses for one-shots that only had a procedural overlay before. */
export const WAVE_POSE: ArmDef = wave.pose;
export const SHRUG_POSE: ArmDef = shrug.pose;
export const ONESHOT_SCALE = { wave: wave.scale, shrug: shrug.scale } as const;

/** Default point aim when nothing is targeted: forward and a bit out to the dominant side (arm frame). */
export const DEFAULT_POINT_DIR: Vec3 = { x: 0.22, y: -0.05, z: 0.97 };

export const EXPLAIN_POSE: ArmDef = explain.pose;
/** `explainR` is the same pose on one hand. */
export const EXPLAIN_R_POSE: ArmDef = explain.pose;
export const THINK_POSE: ArmDef = think.pose;
export const HOLD_POSE: ArmDef = hold.pose;
export const HANDS_POSE: ArmDef = hands.pose;

export const CATALOG_SCALE: CatalogScale = {
  explain: explain.scale,
  think: think.scale,
  hold: hold.scale,
  hands: hands.scale,
};

export type CatalogArmName = "explain" | "explainR" | "think" | "hold" | "hands" | "point";

export function catalogPose(name: CatalogArmName): ArmDef | null {
  switch (name) {
    case "explain":
    case "explainR":
      return EXPLAIN_POSE;
    case "think":
      return THINK_POSE;
    case "hold":
      return HOLD_POSE;
    case "hands":
      return HANDS_POSE;
    default:
      return null;
  }
}

/** `point` at a direction. Scale is how far the legal pose sits toward `pointDef`. */
export function pointPose(dir: Vec3): { pose: ArmDef; scale: number } {
  return containPose(pointDef(dir));
}
