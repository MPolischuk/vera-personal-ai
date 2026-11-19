import { describe, expect, it } from "vitest";
import { resolveGesturePlayback } from "./resolveGesturePlayback";

describe("resolveGesturePlayback", () => {
  it("returns clip when id is in loaded", () => {
    expect(resolveGesturePlayback("wave", new Set(["wave"]))).toBe("clip");
  });

  it("returns procedural when loaded is empty", () => {
    expect(resolveGesturePlayback("wave", new Set())).toBe("procedural");
  });

  it("returns procedural for unknown ids even if other clips loaded", () => {
    expect(resolveGesturePlayback("nod", new Set(["wave"]))).toBe("procedural");
    expect(resolveGesturePlayback("clap", new Set(["wave"]))).toBe("procedural");
  });
});
