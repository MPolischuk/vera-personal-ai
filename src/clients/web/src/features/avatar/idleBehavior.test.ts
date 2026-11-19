import { describe, expect, it } from "vitest";
import { advanceIdleBehavior, boredomFor, createIdleBehavior, IDLE_TIMING } from "./idleBehavior";
import { IDLE_FIDGETS } from "./motionGestures";

const base = { idle: true, typing: false, armFree: true, headFree: true };

function run(ms: number, input = base, step = 50, seed = 3) {
  const s = createIdleBehavior(0, seed);
  const fidgets: Array<[number, string]> = [];
  const heads: Array<[number, string]> = [];
  const shifts: number[] = [];
  let lastSign = 1;
  let maxGaze = 0;
  for (let t = 0; t <= ms; t += step) {
    const o = advanceIdleBehavior(s, t, input);
    if (o.fidget) fidgets.push([t, o.fidget]);
    if (o.headCue) heads.push([t, o.headCue]);
    if (Math.sign(s.weightTarget) !== lastSign) { shifts.push(t); lastSign = Math.sign(s.weightTarget); }
    maxGaze = Math.max(maxGaze, Math.abs(o.gaze.yaw));
  }
  return { s, fidgets, heads, shifts, maxGaze };
}

describe("idleBehavior", () => {
  it("shifts weight inside the idle timing window", () => {
    const { shifts } = run(240_000);
    expect(shifts.length).toBeGreaterThanOrEqual(5);
    shifts.forEach((t, i) => {
      const gap = t - (shifts[i - 1] ?? 0);
      expect(gap).toBeGreaterThanOrEqual(IDLE_TIMING.shiftMinMs * 0.7 - 100);
      expect(gap).toBeLessThanOrEqual(IDLE_TIMING.shiftMaxMs + 100);
    });
  });

  it("ramps boredom and fidgets more often later", () => {
    expect(boredomFor(0)).toBe(0);
    expect(boredomFor(IDLE_TIMING.boredFullMs)).toBe(1);
    const { fidgets } = run(420_000);
    const early = fidgets.filter(([t]) => t < 120_000).length;
    const late = fidgets.filter(([t]) => t >= 300_000).length;
    expect(late / 120).toBeGreaterThan(early / 120);
    for (const [, name] of fidgets) expect([...IDLE_FIDGETS, "yawn"]).toContain(name);
  });

  it("yawns only after the threshold, with a gap", () => {
    const { fidgets } = run(600_000);
    const yawns = fidgets.filter(([, n]) => n === "yawn").map(([t]) => t);
    expect(yawns.length).toBeGreaterThan(0);
    expect(yawns[0]).toBeGreaterThanOrEqual(IDLE_TIMING.yawnAfterMs);
    yawns.slice(1).forEach((t, i) => expect(t - yawns[i]!).toBeGreaterThanOrEqual(IDLE_TIMING.yawnGapMs));
  });

  it("emits sighs or neck stretches as head cues", () => {
    const { heads } = run(300_000);
    expect(heads.length).toBeGreaterThan(0);
    for (const [, n] of heads) expect(["sigh", "neckStretch"]).toContain(n);
  });

  it("straightens and stops fidgeting on typing", () => {
    const s = createIdleBehavior(0, 3);
    for (let t = 0; t <= 60_000; t += 50) advanceIdleBehavior(s, t, base);
    const edge = advanceIdleBehavior(s, 60_050, { ...base, typing: true });
    expect(edge.attention).toBe(true);
    let last = edge;
    for (let t = 60_100; t <= 80_000; t += 50) {
      last = advanceIdleBehavior(s, t, { ...base, typing: true });
      expect(last.fidget).toBeNull();
      expect(last.attention).toBe(false);
    }
    expect(last.straighten).toBeGreaterThan(0.95);
    expect(last.gaze).toEqual({ yaw: 0, pitch: 0 });
    expect(last.smile).toBeGreaterThan(0);
  });

  it("is deterministic per seed and quiet when not idle", () => {
    expect(run(100_000).fidgets).toEqual(run(100_000).fidgets);
    const busy = run(200_000, { ...base, idle: false });
    expect(busy.fidgets).toEqual([]);
    expect(busy.maxGaze).toBe(0);
  });
});
