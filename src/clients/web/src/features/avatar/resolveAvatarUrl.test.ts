import { describe, expect, it } from "vitest";
import { resolveAvatarModelUrl } from "./resolveAvatarUrl";

describe("resolveAvatarModelUrl", () => {
  it("maps a relative YAML path to a root-absolute SPA URL", () => {
    expect(resolveAvatarModelUrl("avatars/default.vrm")).toBe("/avatars/default.vrm");
  });

  it("does not duplicate a leading slash", () => {
    expect(resolveAvatarModelUrl("/avatars/x.vrm")).toBe("/avatars/x.vrm");
  });

  it("trims whitespace and extra leading slashes", () => {
    expect(resolveAvatarModelUrl("  ///avatars/alt.vrm  ")).toBe("/avatars/alt.vrm");
  });
});
