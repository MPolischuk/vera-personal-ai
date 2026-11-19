import { describe, expect, it } from "vitest";
import { avatarMotionV2 } from "./avatarMotionFlag";
import {
  activeLayers,
  activeState,
  createDirector,
  directGestures,
  LISTEN_LEAN,
  listenLeanAt,
  requestState,
  SLOW_THINK_MS,
} from "./director";
import { tick } from "./gestureQueue";

describe("avatarMotionV2", () => {
  it("defaults to true", () => {
    expect(avatarMotionV2).toBe(true);
  });
});

describe("director hysteresis", () => {
  it("publishes a request that has been stable for 250 ms", () => {
    const director = createDirector("idle");
    requestState(director, "thinking", 0);
    expect(activeState(director, 249)).toBe("idle");
    expect(activeState(director, 250)).toBe("thinking");
  });

  it("does not reset the hold when the same state is requested again", () => {
    const director = createDirector("idle");
    requestState(director, "listening", 0);
    requestState(director, "listening", 200);
    expect(activeState(director, 249)).toBe("idle");
    expect(activeState(director, 250)).toBe("listening");
  });

  it("publishes one state while thinking and speaking alternate every 100 ms for 1 s", () => {
    const director = createDirector("idle");
    const seen = new Set<string>();
    for (let i = 0; i < 10; i += 1) {
      const nowMs = i * 100;
      requestState(director, i % 2 === 0 ? "thinking" : "speaking", nowMs);
      seen.add(activeState(director, nowMs));
      seen.add(activeState(director, nowMs + 99));
    }
    expect([...seen]).toEqual(["idle"]);
  });
});

describe("activeLayers", () => {
  it("returns the five layers and a bust posture weight while motion v2 is on", () => {
    const director = createDirector("speaking");
    const layers = activeLayers(director, 0);
    expect(layers.map((layer) => layer.id)).toEqual([
      "posture",
      "idle",
      "state",
      "gesture",
      "attention",
    ]);
    expect(layers[0]?.armWeight).toBe(1);
    expect(layers.slice(1).every((layer) => layer.armWeight === 0)).toBe(true);
  });
});

describe("bank motion preview", () => {
  const quiet = {
    shot: "bust" as const,
    hubGesture: null,
    turnError: false,
    ready: false,
  };

  it("holds explain while idle and clears it on rest", () => {
    const director = createDirector("idle", 0);
    const held = { ...quiet, motionPreview: { name: "explain", playId: "preview-a" } };
    directGestures(director, 0, held);
    expect(director.queue.arm?.name).toBe("explain");
    directGestures(director, 2000, held);
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.phase).not.toBe("release");
    directGestures(director, 2200, {
      ...quiet,
      motionPreview: { name: "rest", playId: "preview-b" },
    });
    const arm = director.queue.arm;
    expect(arm === null || arm.phase === "release").toBe(true);
  });
});

describe("intent reactions", () => {
  const quiet = { shot: "bust" as const, hubGesture: null, turnError: false, ready: false, motionPreview: null };

  it.each([
    ["greeting", "bow"],
    ["thanks", "handChest"],
    ["goodNews", "clap"],
    ["sad", "sadClasp"],
    ["funny", "laugh"],
    ["surprise", "surpriseStep"],
  ] as const)("user %s plays %s with a listening lean + nod", (kind, motion) => {
    const director = createDirector("idle", 0);
    directGestures(director, 1000, { ...quiet, intent: { playId: `u:${kind}`, source: "user", kind } });
    expect(director.queue.arm?.name).toBe(motion);
    expect(listenLeanAt(director, 1000 + LISTEN_LEAN.ms / 2)).toBeGreaterThan(0.5);
    expect(listenLeanAt(director, 1000 + LISTEN_LEAN.ms + 1)).toBe(0);
    // Same playId does not replay.
    const before = director.queue.arm;
    directGestures(director, 1100, { ...quiet, intent: { playId: `u:${kind}`, source: "user", kind } });
    expect(director.queue.arm).toBe(before);
  });

  it("plays the speech gesture once speaking, then a closing open hand", () => {
    const director = createDirector("idle", 0);
    const intent = { playId: "a:1", source: "assistant" as const, kind: "list" as const, closing: true };
    directGestures(director, 0, { ...quiet, intent });
    expect(director.queue.arm).toBeNull();
    requestState(director, "speaking", 0);
    for (let t = 300; t <= 1600; t += 50) { directGestures(director, t, { ...quiet, intent }); tick(director.queue, t); }
    expect(director.queue.arm?.name).toBe("count");
    requestState(director, "idle", 8000);
    for (let t = 8000; t <= 9000; t += 50) { directGestures(director, t, { ...quiet, intent }); tick(director.queue, t); }
    expect(director.queue.arm?.name).toBe("openHand");
  });

  it("taps fingers when thinking is slow", () => {
    const director = createDirector("idle", 0);
    requestState(director, "thinking", 0);
    for (let t = 0; t <= SLOW_THINK_MS + 900; t += 50) { directGestures(director, t, quiet); tick(director.queue, t); }
    expect(director.queue.arm?.name).toBe("tapFingers");
  });
});
