import { describe, expect, it } from "vitest";
import { REST_ARM } from "./restPose";

describe("restPose offsets", () => {
  it("drops the arms well below a T-pose without reaching a full hang", () => {
    expect(REST_ARM.upperZ).toBeGreaterThan(0.7);
    expect(REST_ARM.upperZ).toBeLessThan(Math.PI / 2);
    expect(REST_ARM.lowerX).toBeGreaterThan(0);
    expect(REST_ARM.lowerX).toBeLessThan(0.6);
  });
});
