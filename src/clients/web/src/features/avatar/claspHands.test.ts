import { describe, expect, it } from "vitest";
import { VRMHumanBoneName as B, type VRM } from "@pixiv/three-vrm";
import { Object3D, Vector3 } from "three";
import { solveArm } from "./arms";
import { claspDef, CLASP_LOWER, CLASP_UPPER } from "./motionGestures";

/** Min gap between one hand's fingertips and the other hand's finger bones (m). */
export const CLASP_FINGER_MIN_M = 0.012;

/** How far the top palm may sit from the lower metacarpal centre in XZ (m). */
export const CLASP_META_XZ_MAX_M = 0.12;

/** Max vertical gap between top palm and lower meta — holding, not floating. */
export const CLASP_META_DY_MAX_M = 0.05;

/** Normalized VRM 1.0 arm + hand rig (same layout as handCurl.test). */
function fakeHandRig(): VRM {
  const nodes = new Map<string, Object3D>();
  const add = (name: string, parent: Object3D, x: number, y: number, z: number) => {
    const o = new Object3D();
    o.position.set(x, y, z);
    parent.add(o);
    nodes.set(name, o);
    return o;
  };
  const root = new Object3D();
  for (const [S, sx] of [
    ["Left", 1],
    ["Right", -1],
  ] as const) {
    const shoulder = add(`${S}Shoulder`, root, 0.05 * sx, 1.4, 0);
    const upper = add(`${S}UpperArm`, shoulder, 0.1 * sx, 0, 0);
    const lower = add(`${S}LowerArm`, upper, 0.25 * sx, 0, 0);
    const hand = add(`${S}Hand`, lower, 0.22 * sx, 0, 0);
    const z = { Index: 0.02, Middle: 0.005, Ring: -0.01, Little: -0.025 } as const;
    for (const f of ["Index", "Middle", "Ring", "Little"] as const) {
      const p = add(`${S}${f}Proximal`, hand, 0.08 * sx, 0, z[f]);
      const i = add(`${S}${f}Intermediate`, p, 0.03 * sx, 0, 0);
      const d = add(`${S}${f}Distal`, i, 0.02 * sx, 0, 0);
      add(`${S}${f}Tip`, d, 0.02 * sx, 0, 0);
    }
    const m = add(`${S}ThumbMetacarpal`, hand, 0.02 * sx, -0.01, 0.02);
    const tp = add(`${S}ThumbProximal`, m, 0.03 * 0.76 * sx, 0, 0.03 * 0.65);
    const td = add(`${S}ThumbDistal`, tp, 0.03 * 0.76 * sx, 0, 0.03 * 0.65);
    add(`${S}ThumbTip`, td, 0.025 * 0.76 * sx, 0, 0.025 * 0.65);
  }
  const byBone = (name: string) => nodes.get(name.charAt(0).toUpperCase() + name.slice(1)) ?? null;
  return { humanoid: { getNormalizedBoneNode: (n: B) => byBone(n) }, scene: root } as unknown as VRM;
}

const wp = (vrm: VRM, n: string) =>
  (vrm.humanoid!.getNormalizedBoneNode(n as B) as Object3D).getWorldPosition(new Vector3());

const FINGERS = ["Index", "Middle", "Ring", "Little", "Thumb"] as const;
const SEGMENTS = {
  Index: ["Proximal", "Intermediate", "Distal", "Tip"],
  Middle: ["Proximal", "Intermediate", "Distal", "Tip"],
  Ring: ["Proximal", "Intermediate", "Distal", "Tip"],
  Little: ["Proximal", "Intermediate", "Distal", "Tip"],
  Thumb: ["Metacarpal", "Proximal", "Distal", "Tip"],
} as const;

function applyClasp(vrm: VRM): void {
  for (const side of ["L", "R"] as const) {
    solveArm(vrm, claspDef(side), side, { fingers: true });
  }
  (vrm.scene as Object3D).updateMatrixWorld(true);
}

function minTipToOtherHand(vrm: VRM, tipSide: "left" | "right"): number {
  const other = tipSide === "left" ? "right" : "left";
  let min = Infinity;
  for (const f of FINGERS) {
    const tipNode =
      vrm.humanoid!.getNormalizedBoneNode(
        (f === "Thumb" ? `${tipSide}ThumbTip` : `${tipSide}${f}Tip`) as B,
      ) ??
      vrm.humanoid!.getNormalizedBoneNode(
        (f === "Thumb" ? `${tipSide}ThumbDistal` : `${tipSide}${f}Distal`) as B,
      );
    if (!tipNode) continue;
    const tip = tipNode.getWorldPosition(new Vector3());
    for (const of of FINGERS) {
      for (const seg of SEGMENTS[of]) {
        const n = of === "Thumb" ? `${other}Thumb${seg}` : `${other}${of}${seg}`;
        const bone = vrm.humanoid!.getNormalizedBoneNode(n as B);
        if (!bone) continue;
        min = Math.min(min, tip.distanceTo(bone.getWorldPosition(new Vector3())));
      }
    }
  }
  return min;
}

/** Palm centre ≈ halfway from wrist (Hand) toward the middle knuckle. */
function palmCenter(vrm: VRM, side: "left" | "right"): Vector3 {
  const hand = wp(vrm, `${side}Hand`);
  const knuckle = wp(vrm, `${side}MiddleProximal`);
  return hand.clone().lerp(knuckle, 0.5);
}

/** Midpoint of the lower (right) hand's metacarpal plate (wrist → knuckles). */
function lowerMetaCenter(vrm: VRM): Vector3 {
  const wrist = wp(vrm, "rightHand");
  const knuckle = wp(vrm, "rightMiddleProximal");
  return wrist.clone().lerp(knuckle, 0.45);
}

function xzDist(a: Vector3, b: Vector3): number {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

describe("clasp / stacked hands", () => {
  it("uses edge-on lower (back out) and palm-down upper with straight fingers", () => {
    // ArmDef.palm aligns hand local +Y (back of hand), opposite the true palm (-Y).
    // Lower: back facing the camera ≈ ArmDef.palm toward +Z.
    expect(CLASP_LOWER.palm.z).toBeGreaterThan(0.5);
    expect(Math.abs(CLASP_LOWER.palm.y)).toBeLessThan(0.45);
    // Upper: true palm down onto the lower hand ≈ ArmDef.palm toward +Y (back up).
    expect(CLASP_UPPER.palm.y).toBeGreaterThan(0.7);
    // Upper fingers nearly straight; lower lightly curled.
    expect(Math.max(...CLASP_UPPER.curl)).toBeLessThan(0.2);
    expect(Math.min(...CLASP_LOWER.curl)).toBeGreaterThan(0.15);
    // Thumb tucked on the upper hand.
    expect(CLASP_UPPER.thumb).toBeGreaterThan(0.4);
  });

  it("keeps each hand's fingertips clear of the other hand's finger bones", () => {
    const vrm = fakeHandRig();
    applyClasp(vrm);
    const leftClear = minTipToOtherHand(vrm, "left");
    const rightClear = minTipToOtherHand(vrm, "right");
    expect(leftClear, `L tips → R bones = ${leftClear}`).toBeGreaterThanOrEqual(CLASP_FINGER_MIN_M);
    expect(rightClear, `R tips → L bones = ${rightClear}`).toBeGreaterThanOrEqual(CLASP_FINGER_MIN_M);
  });

  it("places the left hand above the right (ref stack: L over R)", () => {
    const vrm = fakeHandRig();
    applyClasp(vrm);
    const l = palmCenter(vrm, "left");
    const r = palmCenter(vrm, "right");
    expect(l.y - r.y).toBeGreaterThan(0.006);
  });

  it("rests the top (left) palm over the lower (right) metacarpal back", () => {
    const vrm = fakeHandRig();
    applyClasp(vrm);
    const topPalm = palmCenter(vrm, "left");
    const meta = lowerMetaCenter(vrm);
    const wrist = wp(vrm, "rightHand");
    const knuckle = wp(vrm, "rightMiddleProximal");
    const tip = wp(vrm, "rightMiddleTip");
    const forearm = wp(vrm, "rightLowerArm").clone().lerp(wrist, 0.5);

    const axis = tip.clone().sub(wrist);
    const axisLenSq = axis.lengthSq() || 1;
    const t = topPalm.clone().sub(wrist).dot(axis) / axisLenSq;
    const tKnuckle = knuckle.clone().sub(wrist).dot(axis) / axisLenSq;

    const toMeta = xzDist(topPalm, meta);
    expect(toMeta, `top→meta xz ${toMeta}`).toBeLessThanOrEqual(CLASP_META_XZ_MAX_M);
    expect(toMeta).toBeLessThan(xzDist(topPalm, forearm));
    expect(t, `hand-axis t ${t}`).toBeGreaterThanOrEqual(0.1);
    expect(t, `hand-axis t ${t} (knuckle ${tKnuckle})`).toBeLessThanOrEqual(tKnuckle + 0.15);
    expect(topPalm.y).toBeGreaterThan(meta.y);
    expect(topPalm.y - meta.y).toBeLessThanOrEqual(CLASP_META_DY_MAX_M);
  });
});
