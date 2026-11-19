import { describe, expect, it } from "vitest";
import { avatarMotionV2 } from "./avatarMotionFlag";
import {
  EXPLAIN_WEIGHT,
  FRAME_MS,
  GESTURE_SCALE,
  LISTENING_NOD_INTENSITY,
} from "./avatarMotion";
import { EXPLAIN_POSE, THINK_POSE, pointPose } from "./gestureCatalog";
import {
  createDirector,
  directGestures,
  requestState,
  type DirectorCues,
} from "./director";
import {
  armBlendWeight,
  cancelBelow,
  createGestureQueue,
  enqueue,
  releaseChannel,
  sampleArm,
  tick,
  type GestureQueue,
  type GestureRequest,
} from "./gestureQueue";
import type { Vec3 } from "./arms";

const QUIET: DirectorCues = {
  shot: "bust",
  hubGesture: null,
  turnError: false,
  ready: false,
};

function explainRequest(): GestureRequest {
  return {
    name: "explain",
    priority: 1,
    channel: "arm",
    hands: "both",
    oneshot: false,
    weight: EXPLAIN_WEIGHT,
    durationMs: 0,
    pose: EXPLAIN_POSE,
  };
}

function oneshot(
  name: string,
  priority: 0 | 1 | 2 | 3,
  hands: GestureRequest["hands"],
): GestureRequest {
  return {
    name,
    priority,
    channel: "arm",
    hands,
    oneshot: true,
    weight: 1,
    durationMs: 1000,
    pose: name === "point" ? null : EXPLAIN_POSE,
  };
}

function advance(queue: GestureQueue, nowMs: number, frames: number): number {
  let now = nowMs;
  for (let frame = 0; frame < frames; frame += 1) {
    now += FRAME_MS;
    tick(queue, now);
  }
  return now;
}

function holdExplain(queue: GestureQueue, nowMs = 0): number {
  enqueue(queue, explainRequest(), nowMs);
  return advance(queue, nowMs, 12);
}

function angle(a: Vec3, b: Vec3): number {
  const dot = a.x * b.x + a.y * b.y + a.z * b.z;
  const denom = Math.hypot(a.x, a.y, a.z) * Math.hypot(b.x, b.y, b.z);
  return Math.acos(Math.min(1, Math.max(-1, dot / denom)));
}

describe("gesture queue", () => {
  it("releases a held explain within 200 ms when P3 cancels it", () => {
    const queue = createGestureQueue(0);
    const heldAt = holdExplain(queue, 0);
    expect(queue.arm?.phase).toBe("hold");
    const before = armBlendWeight(queue, "R", 0);
    cancelBelow(queue, 3, heldAt);
    expect(queue.arm?.name).toBe("explain");
    expect(queue.arm?.phase).toBe("release");

    let now = heldAt;
    let previous = sampleArm(queue, "R", 0);
    let previousWeight = before;
    let sawDrop = false;
    for (let frame = 0; frame < 12; frame += 1) {
      now += FRAME_MS;
      tick(queue, now);
      if (!queue.arm) {
        break;
      }
      expect(queue.arm.phase).toBe("release");
      const weight = armBlendWeight(queue, "R", 0);
      expect(Math.abs(weight - previousWeight)).toBeLessThan(0.08);
      const next = sampleArm(queue, "R", 0);
      expect(angle(previous.f, next.f)).toBeLessThan(0.05);
      if (weight < previousWeight) {
        sawDrop = true;
      }
      previous = next;
      previousWeight = weight;
    }
    expect(sawDrop || queue.arm === null).toBe(true);
    expect(now - heldAt).toBeLessThanOrEqual(12 * FRAME_MS + 1e-6);
  });

  it("keeps a second one-shot of the same hand waiting", () => {
    const queue = createGestureQueue(0);
    enqueue(queue, oneshot("wave", 2, "R"), 0);
    const now = advance(queue, 0, 12);
    expect(queue.arm?.name).toBe("wave");
    expect(queue.arm?.phase).toBe("hold");
    enqueue(queue, oneshot("shrug", 2, "both"), now);
    expect(queue.arm?.name).toBe("wave");
    expect(queue.arm?.phase).toBe("hold");
    expect(queue.pending.some((item) => item.name === "shrug")).toBe(true);
    const holds = [queue.arm, queue.head].filter(
      (item) => item && item.phase !== "release" && item.phase !== "pending",
    );
    expect(holds).toHaveLength(1);
  });

  it("does not restart the same arm gesture before the 1500 ms cooldown", () => {
    const queue = createGestureQueue(0);
    let now = holdExplain(queue, 0);
    releaseChannel(queue, "arm", "explain", now);
    while (queue.arm && now < 5000) {
      now += FRAME_MS;
      tick(queue, now);
    }
    expect(queue.arm).toBeNull();
    const ended = now;
    enqueue(queue, explainRequest(), ended + 1499);
    tick(queue, ended + 1499);
    expect(queue.arm).toBeNull();
    enqueue(queue, explainRequest(), ended + 1500);
    tick(queue, ended + 1500);
    expect(queue.arm?.name).toBe("explain");
    expect(queue.arm?.phase).not.toBe("release");
  });

  it("waits 4 s before the same arm one-shot starts again", () => {
    const queue = createGestureQueue(0);
    enqueue(queue, oneshot("shrug", 2, "both"), 0);
    let now = advance(queue, 0, 80);
    while (queue.arm && now < 8000) {
      now += FRAME_MS;
      tick(queue, now);
    }
    const ended = now;
    enqueue(queue, oneshot("shrug", 2, "both"), ended + 1500);
    tick(queue, ended + 1500);
    expect(queue.arm?.name).not.toBe("shrug");
    enqueue(queue, oneshot("shrug", 2, "both"), ended + 4000);
    tick(queue, ended + 4000);
    expect(queue.arm?.name).toBe("shrug");
  });

  it("allows a head nod beside an arm explain", () => {
    const queue = createGestureQueue(0);
    const now = holdExplain(queue, 0);
    enqueue(queue, {
      name: "nod",
      priority: 2,
      channel: "head",
      hands: "none",
      oneshot: true,
      weight: 1,
      durationMs: 900,
      intensity: 0.7,
      pose: null,
    }, now);
    tick(queue, now + FRAME_MS);
    expect(queue.arm?.name).toBe("explain");
    expect(queue.arm?.phase).not.toBe("release");
    expect(queue.head?.name).toBe("nod");
    expect(queue.head?.phase).not.toBe("release");
  });
});

describe("director gestures", () => {
  it("leaves the queue alone while motion v2 is off", () => {
    expect(avatarMotionV2).toBe(true);
    const director = createDirector("idle", 0);
    requestState(director, "speaking", 0);
    directGestures(director, 2000, { ...QUIET, shot: "waist" }, false);
    expect(director.queue.arm).toBeNull();
    expect(director.queue.head).toBeNull();
  });

  it("holds explain at weight 0.4 while speaking", () => {
    const director = createDirector("idle", 0);
    requestState(director, "speaking", 0);
    directGestures(director, 250, QUIET, true);
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.weight).toBe(EXPLAIN_WEIGHT);
    expect(director.queue.arm?.pose).toBeTruthy();
    const blend = armBlendWeight(director.queue, "L", 0);
    expect(blend).toBeLessThanOrEqual(EXPLAIN_WEIGHT * GESTURE_SCALE + 1e-6);
  });

  it("uses a head-only think on bust and an arm think on waist after 1.5 s", () => {
    const bust = createDirector("idle", 0);
    requestState(bust, "thinking", 0);
    directGestures(bust, 1499, QUIET, true);
    expect(bust.queue.arm?.name).not.toBe("think");
    expect(bust.queue.head?.name).not.toBe("think");
    directGestures(bust, 1500, QUIET, true);
    expect(bust.queue.arm).toBeNull();
    expect(bust.queue.head?.name).toBe("think");
    expect(bust.queue.head?.head).toEqual({ pitch: 0.1, yaw: 0, roll: 0.1 });

    const waist = createDirector("idle", 0);
    requestState(waist, "thinking", 0);
    directGestures(waist, 1500, { ...QUIET, shot: "waist" }, true);
    expect(waist.queue.arm?.name).toBe("think");
    expect(waist.queue.arm?.hands).toBe("R");
    expect(waist.queue.head).toBeNull();
    expect(waist.queue.arm?.pose).toEqual(THINK_POSE);
  });

  it("releases explain on barge-in before 200 ms", () => {
    const director = createDirector("speaking", 0);
    directGestures(director, 0, QUIET, true);
    const heldAt = 400;
    directGestures(director, heldAt, QUIET, true);
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.phase).toBe("hold");
    requestState(director, "listening", heldAt);
    directGestures(director, heldAt, QUIET, true);
    expect(director.queue.arm?.phase).toBe("release");
    directGestures(director, heldAt + 200, QUIET, true);
    const stillUp = director.queue.arm?.name === "explain" && director.queue.arm.phase !== "release";
    expect(stillUp).toBe(false);
  });

  it("enqueues shrug at P3 when the turn fails", () => {
    const director = createDirector("speaking", 0);
    directGestures(director, 250, QUIET, true);
    directGestures(director, 500, QUIET, true);
    expect(director.queue.arm?.phase).toBe("hold");
    directGestures(director, 500, { ...QUIET, turnError: true }, true);
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.phase).toBe("release");
    expect(director.queue.pending.some((item) => item.name === "shrug" && item.priority === 3)).toBe(
      true,
    );
  });

  it("plays hands once when the avatar becomes ready", () => {
    const director = createDirector("idle", 0);
    directGestures(director, 10, { ...QUIET, ready: true }, true);
    expect(director.queue.arm?.name).toBe("hands");
    const started = director.queue.arm?.startedAtMs;
    directGestures(director, 40, { ...QUIET, ready: true }, true);
    expect(director.queue.arm?.name).toBe("hands");
    expect(director.queue.arm?.startedAtMs).toBe(started);
    expect(director.queue.pending.filter((item) => item.name === "hands")).toHaveLength(0);
  });

  it("does not fire point or hold from the state", () => {
    const director = createDirector("idle", 0);
    requestState(director, "speaking", 0);
    directGestures(director, 250, { ...QUIET, shot: "full", ready: true }, true);
    requestState(director, "thinking", 250);
    directGestures(director, 2000, { ...QUIET, shot: "full" }, true);
    const names = [
      director.queue.arm?.name,
      director.queue.head?.name,
      ...director.queue.pending.map((item) => item.name),
    ];
    expect(names).not.toContain("point");
    expect(names).not.toContain("hold");
    expect(names).not.toContain("explainR");
  });

  it("nods at low intensity while listening, and not twice inside 2 s", () => {
    const director = createDirector("listening", 0);
    directGestures(director, 2999, QUIET, true);
    expect(director.queue.head?.name).not.toBe("nod");
    directGestures(director, 3000, QUIET, true);
    expect(director.queue.head?.name).toBe("nod");
    expect(director.queue.head?.intensity).toBe(LISTENING_NOD_INTENSITY);
    directGestures(director, 4500, QUIET, true);
    expect(director.queue.pending.filter((item) => item.name === "nod")).toHaveLength(0);
    if (director.queue.head?.name === "nod") {
      expect(director.queue.head.startedAtMs).toBe(3000);
    }
  });
});

describe("point stays on request", () => {
  it("enters the queue only when a test passes a target", () => {
    const queue = createGestureQueue(0);
    const aimed = pointPose({ x: -0.4, y: 0.1, z: 1 });
    enqueue(queue, {
      name: "point",
      priority: 1,
      channel: "arm",
      hands: "L",
      oneshot: true,
      weight: 1,
      durationMs: 1200,
      pose: aimed.pose,
    }, 0);
    tick(queue, FRAME_MS);
    expect(queue.arm?.name).toBe("point");
    expect(queue.arm?.hands).toBe("L");
  });
});
