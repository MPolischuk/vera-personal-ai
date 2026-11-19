import { describe, expect, it } from "vitest";
import {
  AnimationClip,
  QuaternionKeyframeTrack,
  VectorKeyframeTrack,
} from "three";
import {
  clipWithoutHipsPosition,
  normalizeMixamoBoneName,
  stripHipsPositionTracks,
} from "./retargetMixamoClip";

describe("retargetMixamoClip strip (D4)", () => {
  it("normalizes colon bone names", () => {
    expect(normalizeMixamoBoneName("mixamorig:Hips")).toBe("mixamorigHips");
    expect(normalizeMixamoBoneName("mixamorigHips")).toBe("mixamorigHips");
  });

  it("removes hips.position VectorKeyframeTrack from a synthetic clip", () => {
    const hipsPos = new VectorKeyframeTrack(
      "normalizedHips.position",
      [0, 1],
      [0, 1, 0, 0.1, 1, 0],
    );
    const hipsRot = new QuaternionKeyframeTrack(
      "normalizedHips.quaternion",
      [0, 1],
      [0, 0, 0, 1, 0, 0, 0, 1],
    );
    const armRot = new QuaternionKeyframeTrack(
      "normalizedRightUpperArm.quaternion",
      [0, 1],
      [0, 0, 0, 1, 0, 0.1, 0, 1],
    );
    const clip = new AnimationClip("synthetic", 1, [hipsPos, hipsRot, armRot]);
    const stripped = clipWithoutHipsPosition(clip);

    expect(stripped.tracks).toHaveLength(2);
    expect(
      stripped.tracks.every((t) => !t.name.endsWith(".position")),
    ).toBe(true);
    expect(stripHipsPositionTracks(clip.tracks).map((t) => t.name)).toEqual([
      "normalizedHips.quaternion",
      "normalizedRightUpperArm.quaternion",
    ]);
  });

  it("also drops mixamorigHips.position before rename", () => {
    const track = new VectorKeyframeTrack(
      "mixamorigHips.position",
      [0],
      [0, 0, 0],
    );
    expect(stripHipsPositionTracks([track])).toEqual([]);
  });
});
