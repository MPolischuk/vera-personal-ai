import { describe, expect, it } from "vitest";
import { CLIP_CATALOG, clipUrlForGesture } from "./clipCatalog";
import { resolveGesturePlayback } from "./resolveGesturePlayback";
import { oneshotPose } from "./gestureQueue";
import { WAVE_POSE } from "./gestureCatalog";

describe("wave without the Mixamo clip", () => {
  it("has no wave clip in the catalog", () => {
    expect(clipUrlForGesture("wave")).toBeUndefined();
    expect(Object.values(CLIP_CATALOG).some((url) => url.includes("wave"))).toBe(false);
  });

  it("routes the protocol wave to the procedural WAVE_POSE", () => {
    expect(resolveGesturePlayback("wave", new Set())).toBe("procedural");
    expect(oneshotPose("wave")).toBe(WAVE_POSE);
  });
});
