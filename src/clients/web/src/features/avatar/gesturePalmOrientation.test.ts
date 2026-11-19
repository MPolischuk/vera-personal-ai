import { describe, expect, it } from "vitest";
import { VRMHumanBoneName as B, type VRM } from "@pixiv/three-vrm";
import { Object3D, Quaternion, Vector3 } from "three";
import { solveArm, pointDef, restDef, copyDef, mkDef, mixDef, limitDef, type ArmDef, type ArmSide } from "./arms";
import { HOLD_POSE, HANDS_POSE, WAVE_POSE, SHRUG_POSE, THINK_POSE, EXPLAIN_POSE, pointPose, DEFAULT_POINT_DIR } from "./gestureCatalog";
import { MOTION_POSES, MOTION_GESTURES, claspDef, CLASP_LOWER, CLASP_UPPER } from "./motionGestures";

/** Min true-palm · worldUp for "palm up" poses. */
const PALM_UP_DOT = 0.45;
/** Min true-palm · cameraForward (+Z) for wave. */
const PALM_CAM_DOT = 0.45;
/** Min |true-palm · ±X| for clap (palms face each other / inward). */
const CLAP_IN_DOT = 0.45;

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
  for (const [S, sx] of [["Left", 1], ["Right", -1]] as const) {
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

function truePalmNormal(vrm: VRM, side: "left" | "right"): Vector3 {
  const hand = vrm.humanoid!.getNormalizedBoneNode(`${side}Hand` as B) as Object3D;
  const q = hand.getWorldQuaternion(new Quaternion());
  // VRM normalized: true palm = local −Y
  return new Vector3(0, -1, 0).applyQuaternion(q).normalize();
}

function apply(vrm: VRM, def: ArmDef, side: ArmSide): void {
  solveArm(vrm, def, side, { fingers: true });
  (vrm.scene as Object3D).updateMatrixWorld(true);
}

describe("gesture palm normals (dorsal ArmDef.palm convention)", () => {
  it("hold / hands / openPalms / openHand / shrug have palm UP", () => {
    const cases: Array<[string, ArmDef, ArmSide]> = [
      ["hold", HOLD_POSE, "R"],
      ["hands", HANDS_POSE, "L"],
      ["handsR", HANDS_POSE, "R"],
      ["openPalms", MOTION_POSES.OPEN_PALMS_POSE, "L"],
      ["openHand", MOTION_POSES.OPEN_HAND_POSE, "R"],
      ["shrug", SHRUG_POSE, "L"],
    ];
    for (const [name, def, side] of cases) {
      const vrm = fakeHandRig();
      apply(vrm, def, side);
      const palm = truePalmNormal(vrm, side === "L" ? "left" : "right");
      expect(palm.y, `${name} palm.y=${palm.y}`).toBeGreaterThan(PALM_UP_DOT);
    }
  });

  it("wave palm faces the camera (+Z)", () => {
    const vrm = fakeHandRig();
    apply(vrm, WAVE_POSE, "R");
    const palm = truePalmNormal(vrm, "right");
    expect(palm.z, `wave palm.z=${palm.z}`).toBeGreaterThan(PALM_CAM_DOT);
  });

  it("clap palms face each other (inward)", () => {
    const vrm = fakeHandRig();
    apply(vrm, MOTION_POSES.CLAP_POSE, "L");
    apply(vrm, MOTION_POSES.CLAP_POSE, "R");
    const l = truePalmNormal(vrm, "left");
    const r = truePalmNormal(vrm, "right");
    // Left palm should point toward −X (in), right toward +X (in).
    expect(-l.x, `L palm inward ${l.x}`).toBeGreaterThan(CLAP_IN_DOT);
    expect(r.x, `R palm inward ${r.x}`).toBeGreaterThan(CLAP_IN_DOT);
  });

  it("point extends index and curls the other fingers; palm not up", () => {
    const pose = pointPose(DEFAULT_POINT_DIR).pose;
    expect(pose.curl[0]!).toBeLessThan(0.05);
    expect(Math.min(pose.curl[1]!, pose.curl[2]!, pose.curl[3]!)).toBeGreaterThan(0.95);
    expect(pose.thumb).toBeGreaterThanOrEqual(0.95);
    // Forearm forward (not a sideways T-pose); elbow dropped.
    expect(pose.f.z).toBeGreaterThan(0.7);
    expect(pose.d.y).toBeLessThan(-0.5);
    expect(Math.abs(pose.d.x)).toBeLessThan(0.55);
    // Dorsal +Y ⇒ true palm down (authored palm.y > 0).
    expect(pose.palm.y).toBeGreaterThan(0.5);
    const sidePoint = pointDef({ x: 0.36, y: -0.04, z: 0.93 });
    expect(sidePoint.curl[0]!).toBeLessThan(0.05);
    expect(sidePoint.f.z).toBeGreaterThan(0.55);
    expect(sidePoint.palm.y).toBeGreaterThan(0.5);
  });

  it("approved clasp palm signs stay intact (L dorsal up, R dorsal to cam)", () => {
    expect(CLASP_UPPER.palm.y).toBeGreaterThan(0.7);
    expect(CLASP_LOWER.palm.z).toBeGreaterThan(0.5);
    const vrm = fakeHandRig();
    apply(vrm, claspDef("L"), "L");
    apply(vrm, claspDef("R"), "R");
    // Upper true palm mostly down.
    const up = truePalmNormal(vrm, "left");
    expect(up.y).toBeLessThan(-0.35);
  });
});

describe("point / sidePoint finger geometry", () => {
  const wp = (vrm: VRM, n: string) =>
    (vrm.humanoid!.getNormalizedBoneNode(n as B) as Object3D).getWorldPosition(new Vector3());

  it("keeps only the index extended and tucks the thumb over the middle finger", () => {
    for (const [label, def] of [
      ["point", pointPose(DEFAULT_POINT_DIR).pose],
      ["sidePoint", pointDef({ x: 0.36, y: -0.04, z: 0.93 })],
    ] as const) {
      expect(def.thumb, `${label} thumb`).toBe(1);
      expect(def.curl[0]!, `${label} index`).toBeLessThan(0.05);
      expect(Math.min(def.curl[1]!, def.curl[2]!, def.curl[3]!), `${label} curl`).toBeGreaterThanOrEqual(0.95);

      const vrm = fakeHandRig();
      apply(vrm, def, "R");
      const tip = wp(vrm, "rightThumbTip");
      const midP = wp(vrm, "rightMiddleProximal");
      const midI = wp(vrm, "rightMiddleIntermediate");
      const midT = wp(vrm, "rightMiddleTip");
      const idxT = wp(vrm, "rightIndexTip");
      const hand = wp(vrm, "rightHand");

      // Thumb tip sits by the middle phalanx — not sticking out like the index.
      const toMid = Math.min(tip.distanceTo(midP), tip.distanceTo(midI));
      expect(toMid, `${label} thumb→middle ${toMid}`).toBeLessThan(0.1);
      expect(tip.distanceTo(hand), `${label} thumb out`).toBeLessThan(idxT.distanceTo(hand) * 0.7);
      // Closed thumb is nearer the palm than an open thumb on the same pose.
      const openRig = fakeHandRig();
      const open = pointDef(DEFAULT_POINT_DIR);
      open.thumb = 0.15;
      apply(openRig, open, "R");
      const openTip = wp(openRig, "rightThumbTip");
      const openHand = wp(openRig, "rightHand");
      expect(tip.distanceTo(hand), `${label} closed vs open`).toBeLessThan(openTip.distanceTo(openHand));
      // Index clearly longer than the curled middle tip.
      expect(idxT.distanceTo(hand)).toBeGreaterThan(midT.distanceTo(hand) * 1.5);
    }
  });
});

// Frozen calibration from 6feeee0, before the global thumb curl increase.
// Check bone rotations, not just flags: the approved clasp must not change.
describe("point-only thumb calibration regression", () => {
  const dir = new Vector3(0.76, -0.04, 0.65).normalize();
  const axes = [new Vector3(0.2, -0.75, -0.6), new Vector3(0, -0.6, -0.8), new Vector3(-0.1, -0.5, -0.85)]
    .map(v => new Vector3().crossVectors(dir, v.normalize()).normalize());
  const segments = ["Metacarpal", "Proximal", "Distal"] as const;
  const beforeCurl = [0.75, 0.55, 0.75];
  const closedCurl = [1.15, 1.05, 1.1];

  function checkThumb(vrm: VRM, def: ArmDef, side: ArmSide, tuck = 0): void {
    apply(vrm, def, side);
    const amount = Math.max(0, Math.min(1, def.thumb));
    segments.forEach((seg, i) => {
      const oldAngle = beforeCurl[i]! * (0.15 + amount);
      const newAngle = closedCurl[i]! * (0.12 + 1.05 * amount);
      const expected = new Quaternion().setFromAxisAngle(axes[i]!, oldAngle + tuck * (newAngle - oldAngle));
      if (side === "R") { expected.y *= -1; expected.z *= -1; }
      const name = `${side === "L" ? "left" : "right"}Thumb${seg}` as B;
      const actual = vrm.humanoid!.getNormalizedBoneNode(name)!.quaternion;
      actual.toArray().forEach((v, n) => expect(v, name).toBeCloseTo(expected.toArray()[n]!, 12));
    });
  }

  it("keeps all non-point gesture thumbs at pre-change rotations, even on reused outputs", () => {
    const vrm = fakeHandRig();
    const out = pointDef(DEFAULT_POINT_DIR);
    for (const side of ["L", "R"] as const) {
      for (const t of [0, 0.23, 0.5, 0.77, 1]) {
        checkThumb(vrm, restDef(side, t * 5, out), side);
        const poses = [HOLD_POSE, HANDS_POSE, WAVE_POSE, SHRUG_POSE, THINK_POSE, EXPLAIN_POSE,
          claspDef(side), ...Object.entries(MOTION_POSES).filter(([k]) => k !== "SIDE_POINT_POSE").map(([, v]) => v)];
        for (const pose of poses) {
          pointDef(DEFAULT_POINT_DIR, out);
          copyDef(pose, out);
          expect(out.thumbClosed ?? 0).toBe(0);
          checkThumb(vrm, out, side);
        }
        for (const [name, gesture] of Object.entries(MOTION_GESTURES)) {
          if (!gesture.pose || name === "sidePoint") continue;
          pointDef(DEFAULT_POINT_DIR, out);
          const pose = gesture.pose(side, t, t * gesture.durationMs / 1000, out);
          if (!pose) continue;
          expect(pose.thumbClosed ?? 0, name).toBe(0);
          checkThumb(vrm, pose, side);
          checkThumb(vrm, limitDef(mixDef(restDef(side, t), pose, 0.6)), side);
        }
      }
    }
    expect(CLASP_UPPER.thumb).toBe(0.95);
    expect(CLASP_LOWER.thumb).toBe(0.58);
    checkThumb(vrm, mkDef(undefined, pointDef(DEFAULT_POINT_DIR)), "R");
  });

  it("preserves the closed point / sidePoint thumb at full reach", () => {
    for (const side of ["L", "R"] as const) {
      for (const pose of [pointPose(DEFAULT_POINT_DIR).pose, MOTION_POSES.SIDE_POINT_POSE]) {
        expect(pose.thumbClosed).toBe(1);
        checkThumb(fakeHandRig(), pose, side, 1);
      }
    }
  });

  it("blends the extra tuck continuously in both directions, including aliased output", () => {
    const rest = restDef("R", 0);
    const point = pointDef(DEFAULT_POINT_DIR);
    for (const t of [0, 0.01, 0.34, 0.35, 0.5, 0.65, 0.66, 0.99, 1]) {
      const entering = mixDef(rest, point, t);
      const leaving = mixDef(point, rest, 1 - t);
      expect(entering.thumbClosed ?? 0).toBeCloseTo(t, 12);
      expect(leaving.thumbClosed ?? 0).toBeCloseTo(t, 12);
      checkThumb(fakeHandRig(), entering, "R", t);
      const aliased = copyDef(point);
      mixDef(aliased, rest, 1 - t, aliased);
      checkThumb(fakeHandRig(), aliased, "R", t);
    }
  });
});
