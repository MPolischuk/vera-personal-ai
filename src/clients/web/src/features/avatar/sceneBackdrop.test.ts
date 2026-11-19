import { describe, expect, it } from "vitest";
import {
  BACKDROP_PLANE_Z,
  backdropPlaneOffset,
  backdropPlaneSize,
  coverRepeatOffset,
  photoPlaneSize,
} from "./sceneBackdrop";

describe("coverRepeatOffset", () => {
  it("keeps the floor when the view is wider than the image", () => {
    const fit = coverRepeatOffset(16 / 9, 3 / 4);
    expect(fit.repeatX).toBe(1);
    expect(fit.repeatY).toBeCloseTo((3 / 4) / (16 / 9));
    expect(fit.offsetX).toBe(0);
    expect(fit.offsetY).toBe(0);
  });

  it("can still center-crop vertically when asked", () => {
    const fit = coverRepeatOffset(16 / 9, 3 / 4, "center");
    expect(fit.offsetY).toBeCloseTo((1 - fit.repeatY) / 2);
  });

  it("crops horizontally when the view is taller than the image", () => {
    const fit = coverRepeatOffset(9 / 16, 16 / 9);
    expect(fit.repeatY).toBe(1);
    expect(fit.repeatX).toBeCloseTo((9 / 16) / (16 / 9));
    expect(fit.offsetY).toBe(0);
    expect(fit.offsetX).toBeCloseTo((1 - fit.repeatX) / 2);
  });
});

describe("photoPlaneSize", () => {
  it("keeps the photo aspect and a room-scale height", () => {
    const size = photoPlaneSize(1920, 1080, 2.45);
    expect(size.height).toBeCloseTo(2.45);
    expect(size.width / size.height).toBeCloseTo(1920 / 1080);
    expect(size.height).toBeLessThan(3.2);
  });
});

describe("backdropPlaneSize", () => {
  it("grows with camera distance so a closer shot crops the same wall", () => {
    const near = backdropPlaneSize(16 / 9, 1.45, BACKDROP_PLANE_Z);
    const far = backdropPlaneSize(16 / 9, 6.8, BACKDROP_PLANE_Z);
    expect(far.height).toBeGreaterThan(near.height * 1.8);
    expect(far.width / far.height).toBeCloseTo(16 / 9);
  });
});

describe("backdropPlaneOffset", () => {
  it("puts the photo floor on y = 0 at the avatar origin", () => {
    const cover = { repeatX: 1, repeatY: 1, offsetX: 0, offsetY: 0 };
    const pose = backdropPlaneOffset(8, 5, { floorV: 0.1, standU: 0.5, imageHeightMeters: 2.55 }, cover);
    expect(pose.x).toBeCloseTo(0);
    expect(pose.y).toBeCloseTo((0.5 - 0.1) * 5);
    const floorWorldY = pose.y + (0.1 - 0.5) * 5;
    expect(floorWorldY).toBeCloseTo(0);
  });

  it("slides the room so standU sits on x = 0", () => {
    const cover = { repeatX: 1, repeatY: 1, offsetX: 0, offsetY: 0 };
    const pose = backdropPlaneOffset(8, 5, { floorV: 0.1, standU: 0.75, imageHeightMeters: 2.55 }, cover);
    const standWorldX = pose.x + (0.75 - 0.5) * 8;
    expect(standWorldX).toBeCloseTo(0);
  });
});
