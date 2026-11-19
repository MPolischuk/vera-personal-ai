import { describe, expect, it } from "vitest";
import { createPlaybackLedger, createPlaybackQueue } from "./playbackQueue";

describe("playback ledger (D6)", () => {
  it("counts playedChunks at chunk start, not only on ended", () => {
    const ledger = createPlaybackLedger();

    // Three chunks enqueued; start 0 and 1, then interrupt mid-2.
    ledger.start(0);
    ledger.start(1);
    expect(ledger.interrupt()).toBe(2);
    expect(ledger.started).toEqual([0, 1]);
  });

  it("counts the in-flight chunk when barge-in hits mid-first", () => {
    const ledger = createPlaybackLedger();
    ledger.start(0);
    expect(ledger.interrupt()).toBe(1);
  });
});

describe("playback queue schedule (PT-19 D5 / H3)", () => {
  /** 160 samples = 10 ms at 16 kHz. */
  const pcm10ms = new Uint8Array(320);

  function createFakeContext(currentTime = 0) {
    const startAts: number[] = [];
    const sources: Array<{
      onended: ((this: AudioBufferSourceNode, ev: Event) => any) | null;
      start: (when?: number) => void;
      stop: () => void;
    }> = [];

    const ctx = {
      get currentTime() {
        return currentTime;
      },
      setCurrentTime(t: number) {
        currentTime = t;
      },
      createBuffer(_channels: number, length: number, sampleRate: number) {
        return {
          duration: length / sampleRate,
          length,
          numberOfChannels: 1,
          sampleRate,
          getChannelData: () => new Float32Array(length),
          copyFromChannel() {},
          copyToChannel() {},
        } as AudioBuffer;
      },
      createBufferSource() {
        const source = {
          buffer: null as AudioBuffer | null,
          connect() {
            return source as unknown as AudioBufferSourceNode;
          },
          disconnect() {},
          onended: null as ((this: AudioBufferSourceNode, ev: Event) => any) | null,
          start(when = 0) {
            startAts.push(when);
          },
          stop() {},
        };
        sources.push(source);
        return source as unknown as AudioBufferSourceNode;
      },
      destination: {} as AudioDestinationNode,
      startAts,
      sources,
    };

    return ctx;
  }

  it("schedules N enqueued chunks before the first onended (no serial gap)", () => {
    const ctx = createFakeContext(0);
    const started: number[] = [];
    const queue = createPlaybackQueue(
      () => ctx as unknown as AudioContext,
      {
        onStart(sequence) {
          started.push(sequence);
        },
      },
    );

    queue.enqueue(0, pcm10ms);
    queue.enqueue(1, pcm10ms);
    queue.enqueue(2, pcm10ms);

    expect(ctx.startAts).toHaveLength(3);
    expect(started).toEqual([0, 1, 2]);
    expect(queue.getPlayedChunks()).toBe(3);
    // Contiguous schedule from t=0: 0, 0.01, 0.02
    expect(ctx.startAts[0]).toBeCloseTo(0, 5);
    expect(ctx.startAts[1]).toBeCloseTo(0.01, 5);
    expect(ctx.startAts[2]).toBeCloseTo(0.02, 5);
  });

  it("does not reset nextTime while pending sources remain", () => {
    const ctx = createFakeContext(0);
    let emptyCalls = 0;
    const queue = createPlaybackQueue(
      () => ctx as unknown as AudioContext,
      { onEmpty: () => emptyCalls++ },
    );

    queue.enqueue(0, pcm10ms);
    queue.enqueue(1, pcm10ms);
    expect(ctx.startAts).toHaveLength(2);

    // First source ends; second still pending — must not fire onEmpty or wipe timeline.
    ctx.sources[0].onended?.call(ctx.sources[0] as unknown as AudioBufferSourceNode, new Event("ended"));
    expect(emptyCalls).toBe(0);
    expect(queue.isEmpty()).toBe(false);

    // Late enqueue while first already ended: schedules against remaining nextTime.
    ctx.setCurrentTime(0.005);
    queue.enqueue(2, pcm10ms);
    expect(ctx.startAts).toHaveLength(3);
    expect(ctx.startAts[2]).toBeCloseTo(0.02, 5);

    ctx.sources[1].onended?.call(ctx.sources[1] as unknown as AudioBufferSourceNode, new Event("ended"));
    ctx.sources[2].onended?.call(ctx.sources[2] as unknown as AudioBufferSourceNode, new Event("ended"));
    expect(emptyCalls).toBe(1);
    expect(queue.isEmpty()).toBe(true);
  });
});
