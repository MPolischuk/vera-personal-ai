import { describe, expect, it, vi } from "vitest";
import { getPlaybackAnalysisStream, getPlaybackInput } from "./playbackTap";

describe("playbackTap", () => {
  it("falls back to destination when createGain is missing", () => {
    const destination = { kind: "destination" };
    const ctx = { destination } as unknown as AudioContext;

    expect(getPlaybackInput(ctx)).toBe(destination);
  });

  it("fans Gain out to destination and MediaStreamDestination", () => {
    const destination = { kind: "destination" };
    const analysisStream = { id: "analysis" } as MediaStream;
    const analysis = {
      stream: analysisStream,
    };
    const connected: unknown[] = [];
    const gain = {
      connect(target: unknown) {
        connected.push(target);
        return gain;
      },
    };

    const ctx = {
      destination,
      createGain: vi.fn(() => gain),
      createMediaStreamDestination: vi.fn(() => analysis),
    } as unknown as AudioContext;

    expect(getPlaybackInput(ctx)).toBe(gain);
    expect(connected).toEqual([destination, analysis]);
    expect(getPlaybackAnalysisStream(ctx)).toBe(analysisStream);
    // Same tap reused.
    expect(getPlaybackInput(ctx)).toBe(gain);
    expect(ctx.createGain).toHaveBeenCalledTimes(1);
  });

  it("does not create an analyser on the playback path", () => {
    const destination = { kind: "destination" };
    const analysis = { stream: { id: "analysis" } };
    const connected: unknown[] = [];
    const gain = {
      connect(target: unknown) {
        connected.push(target);
        return gain;
      },
    };
    const createAnalyser = vi.fn();
    const ctx = {
      destination,
      createGain: vi.fn(() => gain),
      createMediaStreamDestination: vi.fn(() => analysis),
      createAnalyser,
    } as unknown as AudioContext;

    expect(getPlaybackInput(ctx)).toBe(gain);
    expect(createAnalyser).not.toHaveBeenCalled();
    expect(connected).toEqual([destination, analysis]);
    expect(getPlaybackAnalysisStream(ctx)).toBe(analysis.stream);
  });
});
