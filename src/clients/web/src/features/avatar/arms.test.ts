import { describe, expect, it } from "vitest";
import { VRMHumanBoneName, type VRM } from "@pixiv/three-vrm";
import { Object3D, Quaternion } from "three";
import { FRAME_MS } from "./avatarMotion";
import {
  EXPLAIN_POSE,
  EXPLAIN_R_POSE,
  HANDS_POSE,
  HOLD_POSE,
  THINK_POSE,
  pointPose,
} from "./gestureCatalog";
import {
  createGestureQueue,
  enqueue,
  releaseChannel,
  sampleArm,
  tick,
  type GestureHands,
  type GestureRequest,
} from "./gestureQueue";
import {
  CURL_POINT,
  DOMINANT_SIDE,
  LIM,
  REST,
  armPoints,
  chooseSide,
  limitDef,
  mirror,
  mixDef,
  mkDef,
  openDef,
  pointDef,
  postureArmWeight,
  solveArm,
  applyArmStep,
  copyDef,
  MAX_ARM_STEP_RAD,
  violation,
  type ArmDef,
  type ArmSide,
  type Vec3,
} from "./arms";

function mulberry32(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function randomUnit(rng: () => number): Vec3 {
  const z = rng() * 2 - 1;
  const angle = rng() * Math.PI * 2;
  const radius = Math.sqrt(Math.max(0, 1 - z * z));
  return { x: radius * Math.cos(angle), y: radius * Math.sin(angle), z };
}

function rotateAround(v: Vec3, axis: Vec3, angle: number): Vec3 {
  const cos = Math.cos(angle);
  const sin = Math.sin(angle);
  const dot = v.x * axis.x + v.y * axis.y + v.z * axis.z;
  const cx = axis.y * v.z - axis.z * v.y;
  const cy = axis.z * v.x - axis.x * v.z;
  const cz = axis.x * v.y - axis.y * v.x;
  return {
    x: v.x * cos + cx * sin + axis.x * dot * (1 - cos),
    y: v.y * cos + cy * sin + axis.y * dot * (1 - cos),
    z: v.z * cos + cz * sin + axis.z * dot * (1 - cos),
  };
}

function randomDef(rng: () => number): ArmDef {
  const d = randomUnit(rng);
  const rawAxis = randomUnit(rng);
  const dot = rawAxis.x * d.x + rawAxis.y * d.y + rawAxis.z * d.z;
  let axis = {
    x: rawAxis.x - d.x * dot,
    y: rawAxis.y - d.y * dot,
    z: rawAxis.z - d.z * dot,
  };
  const len = Math.hypot(axis.x, axis.y, axis.z);
  if (len < 1e-6) {
    axis = { x: -d.y, y: d.x, z: 0 };
  } else {
    axis = { x: axis.x / len, y: axis.y / len, z: axis.z / len };
  }
  const flex = LIM.flexMin + rng() * (LIM.flexMax - LIM.flexMin);
  return mkDef({
    d,
    f: rotateAround(d, axis, flex),
    curl: [rng(), rng(), rng(), rng()],
    thumb: rng(),
    spread: rng(),
    wrist: [(rng() - 0.5) * 0.4, (rng() - 0.5) * 0.3],
    palmW: rng(),
    palm: randomUnit(rng),
  });
}

describe("arm limits", () => {
  it("keeps rest, open, and point at zero violations", () => {
    expect(violation(limitDef(REST))).toBe(0);
    expect(violation(REST)).toBe(0);
    const wrist = armPoints(REST).wrist;
    expect(wrist.x).toBeGreaterThan(0);
    expect(wrist.x).toBeLessThan(0.1);
    expect(wrist.y).toBeLessThan(-0.3);
    expect(wrist.z).toBeGreaterThan(LIM.handFrontZ);
    expect(REST.spread).toBeCloseTo(0.3);
    expect(violation(limitDef(openDef()))).toBe(0);
    const point = pointDef({ x: 0, y: 0, z: 1 });
    // Forearm aims forward; upper arm stays near the ribs (not a T-pose).
    expect(point.f.z).toBeGreaterThan(0.7);
    expect(point.d.y).toBeLessThan(-0.5);
    expect(point.curl).toEqual([...CURL_POINT]);
    expect(violation(limitDef(point))).toBe(0);
  });

  it("reports zero violations for 10000 seeded mixes", () => {
    const rng = mulberry32(40606);
    for (let i = 0; i < 10_000; i += 1) {
      const mixed = mixDef(randomDef(rng), randomDef(rng), rng());
      expect(violation(limitDef(mixed))).toBe(0);
    }
  });

  it("pulls a hand behind the back plane up to backZ", () => {
    const behind = mkDef({
      d: { x: 0.05, y: -0.15, z: -1 },
      f: { x: 0.02, y: -0.45, z: -0.9 },
    });
    expect(armPoints(behind).wrist.z).toBeLessThan(LIM.backZ);
    const limited = limitDef(behind);
    expect(violation(limited)).toBe(0);
    expect(armPoints(limited).wrist.z).toBeGreaterThanOrEqual(LIM.backZ);
  });

  it("does not leave an opposite mix behind the back plane", () => {
    const left = mkDef({
      d: { x: 1, y: 0, z: -0.15 },
      f: { x: 0.85, y: -0.4, z: -0.1 },
    });
    const right = mkDef({
      d: { x: -1, y: 0, z: -0.15 },
      f: { x: -0.85, y: -0.4, z: -0.1 },
    });
    const mixed = mixDef(left, right, 0.5);
    expect(armPoints(mixed).elbow.z).toBeLessThan(LIM.backZ);
    const limited = limitDef(mixed);
    const points = armPoints(limited);
    expect(violation(limited)).toBe(0);
    expect(points.elbow.z).toBeGreaterThanOrEqual(LIM.backZ);
    expect(points.mid.z).toBeGreaterThanOrEqual(LIM.backZ);
    expect(points.wrist.z).toBeGreaterThanOrEqual(LIM.backZ);
  });
});

describe("chooseSide", () => {
  it("picks the hand on the target side across a 9x5 grid and stays stable", () => {
    for (let col = 0; col < 9; col += 1) {
      for (let row = 0; row < 5; row += 1) {
        const target = {
          x: -0.4 + col * 0.1,
          y: -0.2 + row * 0.1,
          z: 0.35,
        };
        const azimuth = Math.atan2(target.x, target.z);
        const expected = azimuth > 0.09 ? "L" : "R";
        const first = chooseSide(target);
        const second = chooseSide({ ...target });
        expect(first.side).toBe(expected);
        expect(second.side).toBe(first.side);
        expect(Math.abs(first.turn)).toBeLessThanOrEqual(0.4);
      }
    }
  });
});

describe("mirror", () => {
  it("flips y and z and keeps x and w", () => {
    const source = new Quaternion(0.2, 0.3, -0.4, 0.8).normalize();
    const mirrored = mirror(source);
    expect(mirrored.x).toBeCloseTo(source.x);
    expect(mirrored.y).toBeCloseTo(-source.y);
    expect(mirrored.z).toBeCloseTo(-source.z);
    expect(mirrored.w).toBeCloseTo(source.w);
  });
});

describe("postureArmWeight", () => {
  it("stays at 0 while motion v2 is off and uses the framing gain when on", () => {
    expect(postureArmWeight(false, "bust")).toBe(0);
    expect(postureArmWeight(false, "full")).toBe(0);
    expect(postureArmWeight(true, "bust")).toBe(1);
    expect(postureArmWeight(true, "waist")).toBe(1.45);
    expect(postureArmWeight(true, "full")).toBe(1.6);
  });
});

const QUATERNION_STEP_RAD = MAX_ARM_STEP_RAD;
const CANCEL_FRACTIONS = [0.2, 0.5, 0.8] as const;

interface CatalogCase {
  name: string;
  pose: ArmDef;
  hands: GestureHands;
}

interface StepReport {
  radians: number;
  bone: string;
  violations: number;
}

function catalogRequest(item: CatalogCase): GestureRequest {
  return {
    name: item.name,
    priority: 1,
    channel: "arm",
    hands: item.hands,
    oneshot: false,
    weight: 1,
    durationMs: 0,
    pose: item.pose,
  };
}

function coveredSides(hands: GestureHands): ArmSide[] {
  if (hands === "both") {
    return ["L", "R"];
  }
  if (hands === "L" || hands === "R") {
    return [hands];
  }
  return [];
}

function boneName(side: ArmSide, part: "upper" | "lower"): VRMHumanBoneName {
  if (side === "L") {
    return part === "upper"
      ? VRMHumanBoneName.LeftUpperArm
      : VRMHumanBoneName.LeftLowerArm;
  }
  return part === "upper"
    ? VRMHumanBoneName.RightUpperArm
    : VRMHumanBoneName.RightLowerArm;
}

function armRig(): {
  vrm: VRM;
  read: (side: ArmSide, part: "upper" | "lower") => Quaternion;
} {
  const nodes = new Map<string, Object3D>();
  const vrm = {
    humanoid: {
      getNormalizedBoneNode(name: string): Object3D {
        let node = nodes.get(name);
        if (!node) {
          node = new Object3D();
          nodes.set(name, node);
        }
        return node;
      },
    },
  } as unknown as VRM;
  return {
    vrm,
    read(side, part) {
      const name = boneName(side, part);
      const node = nodes.get(name);
      if (!node) {
        throw new Error(`Missing bone ${name}`);
      }
      return node.quaternion.clone();
    },
  };
}

function attackLengthMs(request: GestureRequest): number {
  const queue = createGestureQueue(0);
  enqueue(queue, request, 0);
  let now = 0;
  for (let frame = 0; frame < 240; frame += 1) {
    now += FRAME_MS;
    tick(queue, now);
    if (queue.arm?.phase === "hold") {
      return now;
    }
  }
  throw new Error(`Attack did not finish for ${request.name}`);
}

function worstQuaternionStep(item: CatalogCase, cancelFraction: number | null): StepReport {
  const request = catalogRequest(item);
  const cancelAt = cancelFraction === null ? null : attackLengthMs(request) * cancelFraction;
  const queue = createGestureQueue(0);
  enqueue(queue, request, 0);
  const { vrm, read } = armRig();
  const sides = coveredSides(item.hands);
  const applied = new Map<ArmSide, ArmDef>();
  const previous = new Map<string, Quaternion>();
  let radians = 0;
  let bone = "";
  let violations = 0;

  const sample = (now: number) => {
    for (const side of sides) {
      const desired = limitDef(sampleArm(queue, side, now / 1000));
      if (violation(desired) > 0) {
        violations += 1;
      }
      let prior = applied.get(side);
      if (!prior) {
        prior = copyDef(desired);
        applied.set(side, prior);
        solveArm(vrm, prior, side, { fingers: false });
      } else {
        applyArmStep(vrm, prior, desired, side, prior, { fingers: false });
        if (violation(prior) > 0) {
          violations += 1;
        }
      }
      for (const part of ["upper", "lower"] as const) {
        const key = `${side}:${part}`;
        const current = read(side, part);
        const before = previous.get(key);
        if (before) {
          const step = before.angleTo(current);
          if (step > radians) {
            radians = step;
            bone = key;
          }
        }
        previous.set(key, current);
      }
    }
  };

  sample(0);
  let now = 0;
  let heldFrames = 0;
  for (let frame = 0; frame < 240; frame += 1) {
    now += FRAME_MS;
    if (
      cancelAt !== null &&
      queue.arm &&
      queue.arm.phase !== "release" &&
      now >= cancelAt
    ) {
      releaseChannel(queue, "arm", item.name, now);
    }
    tick(queue, now);
    sample(now);
    if (cancelAt !== null && !queue.arm) {
      break;
    }
    if (cancelAt === null && queue.arm?.phase === "hold") {
      heldFrames += 1;
      if (heldFrames >= 3) {
        break;
      }
    }
  }

  return { radians, bone, violations };
}

describe("catalog quaternion continuity", () => {
  const catalog: CatalogCase[] = [
    { name: "explain", pose: EXPLAIN_POSE, hands: "both" },
    { name: "explainR", pose: EXPLAIN_R_POSE, hands: "R" },
    { name: "think", pose: THINK_POSE, hands: DOMINANT_SIDE },
    { name: "hold", pose: HOLD_POSE, hands: DOMINANT_SIDE },
    { name: "hands", pose: HANDS_POSE, hands: "both" },
    { name: "point-left", pose: pointPose({ x: -0.6, y: 0, z: 0.8 }).pose, hands: DOMINANT_SIDE },
    { name: "point-right", pose: pointPose({ x: 0.6, y: 0, z: 0.8 }).pose, hands: DOMINANT_SIDE },
    { name: "point-down", pose: pointPose({ x: 0, y: -0.7, z: 0.5 }).pose, hands: DOMINANT_SIDE },
    { name: "point-up", pose: pointPose({ x: 0, y: 0.7, z: 0.5 }).pose, hands: DOMINANT_SIDE },
  ];

  it("stays under 0.05 rad at 60 Hz, including cancels at 20, 50, and 80 percent", () => {
    expect(MAX_ARM_STEP_RAD).toBe(0.05);
    for (const item of catalog) {
      const full = worstQuaternionStep(item, null);
      expect(full.violations, item.name).toBe(0);
      expect(full.radians, `${item.name} ${full.bone}`).toBeGreaterThan(0);
      expect(full.radians, `${item.name} ${full.bone}`).toBeLessThan(QUATERNION_STEP_RAD);
      for (const fraction of CANCEL_FRACTIONS) {
        const cancelled = worstQuaternionStep(item, fraction);
        expect(cancelled.violations, `${item.name} @${fraction}`).toBe(0);
        expect(cancelled.radians, `${item.name} @${fraction} ${cancelled.bone}`).toBeGreaterThan(0);
        expect(cancelled.radians, `${item.name} @${fraction} ${cancelled.bone}`).toBeLessThan(
          QUATERNION_STEP_RAD,
        );
      }
    }
  });
});
