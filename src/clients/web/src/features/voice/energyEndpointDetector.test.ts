import { describe, expect, it } from "vitest";
import {
  createEnergyEndpointDetector,
  vadDefaults,
} from "./energyEndpointDetector";

describe("createEnergyEndpointDetector", () => {
  it("emits endpoint after speech burst + silence ≥ threshold", () => {
    const detector = createEnergyEndpointDetector({
      speechRmsThreshold: 0.02,
      silenceMs: 800,
      minSpeechMs: 100,
    });

    expect(detector.push({ rms: 0.05, tMs: 0 })).toBe("continue");
    expect(detector.push({ rms: 0.05, tMs: 100 })).toBe("continue");
    expect(detector.push({ rms: 0.05, tMs: 200 })).toBe("continue");
    // spokenMs = 200 >= minSpeechMs 100
    expect(detector.push({ rms: 0.001, tMs: 400 })).toBe("continue");
    expect(detector.push({ rms: 0.001, tMs: 900 })).toBe("continue");
    expect(detector.push({ rms: 0.001, tMs: 1001 })).toBe("endpoint");
  });

  it("ignores a short click without enough speech", () => {
    const detector = createEnergyEndpointDetector({
      speechRmsThreshold: 0.02,
      silenceMs: 800,
      minSpeechMs: 180,
    });

    // Single speech frame → spokenMs = 0
    expect(detector.push({ rms: 0.05, tMs: 0 })).toBe("continue");
    expect(detector.push({ rms: 0.001, tMs: 20 })).toBe("continue");
    expect(detector.push({ rms: 0.001, tMs: 900 })).toBe("continue");
  });

  it("reset clears speech state", () => {
    const detector = createEnergyEndpointDetector({
      silenceMs: vadDefaults.silenceMs,
      minSpeechMs: 100,
    });

    detector.push({ rms: 0.05, tMs: 0 });
    detector.push({ rms: 0.05, tMs: 200 });
    detector.reset();
    expect(detector.push({ rms: 0.001, tMs: 2000 })).toBe("continue");
  });
});
