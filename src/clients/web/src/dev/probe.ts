// DEV-ONLY probe: finger/thumb axes and curl direction vs palm normal on the real VRM.
import { Vector3 } from "three";
import { loadVrm } from "../features/avatar/loadVrm";
import { solveArm, REST, mkDef } from "../features/avatar/arms";
const r2 = (v: Vector3) => v.toArray().map((x) => +x.toFixed(2));
export async function runProbe(url: string) {
  const r: any = await (loadVrm as any)(url);
  const vrm = r.vrm ?? r;
  const h = vrm.humanoid;
  const w = (n: string) => h.getNormalizedBoneNode(n)?.getWorldPosition(new Vector3()) ?? null;
  const out: any = { meta: vrm.meta?.metaVersion };
  h.resetNormalizedPose(); vrm.scene.updateMatrixWorld(true);
  // T-pose segment directions (normalized space, identity rotations)
  for (const S of ["left", "right"]) {
    const seg: any = {};
    for (const f of ["Thumb", "Index", "Middle", "Ring", "Little"]) {
      const names = f === "Thumb" ? ["ThumbMetacarpal", "ThumbProximal", "ThumbDistal"] : [`${f}Proximal`, `${f}Intermediate`, `${f}Distal`];
      const p = names.map((n) => w(`${S}${n}`));
      seg[f] = { d01: p[0] && p[1] ? r2(p[1].clone().sub(p[0]).normalize()) : null, d12: p[1] && p[2] ? r2(p[2].clone().sub(p[1]).normalize()) : null };
    }
    out[`tpose_${S}`] = seg;
  }
  for (const side of ["L", "R"] as const) {
    const S = side === "L" ? "left" : "right";
    for (const [tag, def] of [["flat", mkDef({ curl: [0, 0, 0, 0], thumb: 0, spread: 0 })], ["rest", REST], ["fist", mkDef({ curl: [1, 1, 1, 1], thumb: 1 })]] as const) {
      h.resetNormalizedPose();
      solveArm(vrm, def as any, side, { fingers: true });
      vrm.scene.updateMatrixWorld(true);
      const hand = w(`${S}Hand`)!, idx = w(`${S}IndexProximal`)!, lit = w(`${S}LittleProximal`)!, mid = w(`${S}MiddleProximal`)!, tip = w(`${S}MiddleDistal`)!;
      const across = (side === "L" ? lit.clone().sub(idx) : idx.clone().sub(lit)).normalize();
      const along = mid.clone().sub(hand).normalize();
      const palmN = new Vector3().crossVectors(across, along).normalize();
      const f = tip.clone().sub(mid).normalize();
      const th = w(`${S}ThumbDistal`)!.clone().sub(w(`${S}ThumbProximal`)!).normalize();
      out[`${side}_${tag}`] = { tipDotPalm: +f.dot(palmN).toFixed(2), tipDotAlong: +f.dot(along).toFixed(2), thumbDotPalm: +th.dot(palmN).toFixed(2), thumbDotAcross: +th.dot(across).toFixed(2), palmN: r2(palmN) };
    }
  }
  return out;
}
