import { describe, expect, it } from "vitest";
import { VRMHumanBoneName as B, type VRM } from "@pixiv/three-vrm";
import { Object3D, Vector3 } from "three";
import { mkDef, solveArm, type ArmSide } from "./arms";

/** Normalized VRM 1.0 arm + hand rig in T-pose (left +X, right -X, palm -Y), like vera-maid. */
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

const wp = (vrm: VRM, n: string) =>
  (vrm.humanoid!.getNormalizedBoneNode(n as B) as Object3D).getWorldPosition(new Vector3());

function palmFrame(vrm: VRM, side: ArmSide) {
  const S = side === "L" ? "left" : "right";
  const hand = wp(vrm, `${S}Hand`);
  const idx = wp(vrm, `${S}IndexProximal`);
  const lit = wp(vrm, `${S}LittleProximal`);
  const mid = wp(vrm, `${S}MiddleProximal`);
  const across = (side === "L" ? lit.clone().sub(idx) : idx.clone().sub(lit)).normalize();
  const along = mid.clone().sub(hand).normalize();
  // Palm normal (-Y at rest for both hands).
  return { palmN: new Vector3().crossVectors(across, along).normalize(), S };
}

describe("finger curl direction", () => {
  for (const side of ["L", "R"] as const) {
    it(`closes ${side} fingers into the palm`, () => {
      const vrm = fakeHandRig();
      solveArm(vrm, mkDef({ curl: [1, 1, 1, 1], thumb: 1 }), side, { fingers: true });
      (vrm.scene as Object3D).updateMatrixWorld(true);
      const { palmN, S } = palmFrame(vrm, side);
      for (const f of ["Index", "Middle", "Ring", "Little"]) {
        // Knuckle → distal joint direction: the closed finger folds into the palm side.
        const dir = wp(vrm, `${S}${f}Distal`).sub(wp(vrm, `${S}${f}Proximal`)).normalize();
        expect(dir.dot(palmN), `${S}${f}`).toBeGreaterThan(0.8);
      }
      const thumbDir = wp(vrm, `${S}ThumbDistal`).sub(wp(vrm, `${S}ThumbProximal`)).normalize();
      expect(thumbDir.dot(palmN), `${S}Thumb`).toBeGreaterThan(0.3);
    });
  }
});
