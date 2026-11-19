import { PerspectiveCamera, Vector3 } from "three";
import { describe, expect, it } from "vitest";
import {
  CAMERA_SHOT_MOVE_MS,
  CAMERA_SHOTS,
  canZoomIn,
  canZoomOut,
  createCameraShotMotion,
  framingForShot,
  retargetCameraShot,
  stepCameraShot,
  updateCameraShotMotion,
  type BodyAnchors,
} from "./cameraShot";

const sample: BodyAnchors = { headY: 1.5, hipsY: 0.9, feetY: 0 };

describe("cameraShot steps", () => {
  it("has three discrete shots", () => {
    expect(CAMERA_SHOTS).toEqual(["bust", "waist", "full"]);
  });

  it("zooms out toward full and in toward bust, clamped at the ends", () => {
    expect(stepCameraShot("bust", "out")).toBe("waist");
    expect(stepCameraShot("waist", "out")).toBe("full");
    expect(stepCameraShot("full", "out")).toBe("full");
    expect(stepCameraShot("full", "in")).toBe("waist");
    expect(stepCameraShot("waist", "in")).toBe("bust");
    expect(stepCameraShot("bust", "in")).toBe("bust");
  });

  it("disables zoom buttons at the ends", () => {
    expect(canZoomIn("bust")).toBe(false);
    expect(canZoomOut("bust")).toBe(true);
    expect(canZoomIn("full")).toBe(true);
    expect(canZoomOut("full")).toBe(false);
    expect(canZoomIn("waist")).toBe(true);
    expect(canZoomOut("waist")).toBe(true);
  });
});

describe("framingForShot", () => {
  it("keeps the PT-16 bust recipe when a head anchor exists", () => {
    const framing = framingForShot("bust", sample);
    expect(framing.lookAt.y).toBeCloseTo(1.45);
    expect(framing.position.y).toBeCloseTo(1.5);
    expect(framing.position.z).toBeCloseTo(1.45);
  });

  it("keeps the PT-16 fallback bust when there are no anchors", () => {
    const framing = framingForShot("bust", null);
    expect(framing.position).toEqual({ x: 0, y: 1.35, z: 1.55 });
    expect(framing.lookAt).toEqual({ x: 0, y: 1.25, z: 0 });
  });

  it("pulls the camera back and down from bust to waist to full", () => {
    const bust = framingForShot("bust", sample);
    const waist = framingForShot("waist", sample);
    const full = framingForShot("full", sample);

    expect(waist.position.z).toBeGreaterThan(bust.position.z);
    expect(full.position.z).toBeGreaterThan(waist.position.z);
    expect(waist.lookAt.y).toBeLessThan(bust.lookAt.y);
    expect(full.lookAt.y).toBeLessThan(waist.lookAt.y);
    expect(full.lookAt.y).toBeGreaterThan(sample.feetY);
  });

  it("frames full from mesh bounds so hair and shoes fit", () => {
    const bonesOnly = framingForShot("full", sample);
    const withHair = framingForShot("full", {
      ...sample,
      visualTop: 1.88,
      visualBottom: -0.06,
    });
    expect(withHair.position.z).toBeGreaterThan(bonesOnly.position.z);
  });
});

describe("camera shot motion", () => {
  it("reaches the target framing after the move duration", () => {
    const camera = new PerspectiveCamera(28, 1, 0.1, 20);
    const start = framingForShot("bust", sample);
    const motion = createCameraShotMotion(start);
    retargetCameraShot(motion, framingForShot("full", sample), false);

    const steps = Math.ceil(CAMERA_SHOT_MOVE_MS / (1000 / 60));
    for (let i = 0; i < steps; i += 1) {
      updateCameraShotMotion(camera, motion, 1 / 60);
    }

    const target = framingForShot("full", sample);
    expect(camera.position.z).toBeCloseTo(target.position.z, 3);
    expect(motion.currentLook.y).toBeCloseTo(target.lookAt.y, 3);
  });

  it("snaps without interpolating", () => {
    const camera = new PerspectiveCamera(28, 1, 0.1, 20);
    const motion = createCameraShotMotion(framingForShot("bust", sample));
    const full = framingForShot("full", sample);
    retargetCameraShot(motion, full, true);
    updateCameraShotMotion(camera, motion, 0);

    expect(camera.position.distanceTo(new Vector3(full.position.x, full.position.y, full.position.z))).toBeLessThan(
      1e-6,
    );
  });
});
