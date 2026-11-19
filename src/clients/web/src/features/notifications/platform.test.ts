import { describe, expect, it } from "vitest";
import { shouldShowIosInstallInstructions } from "./platform";

describe("shouldShowIosInstallInstructions", () => {
  it("asks to install on iOS when the app is not standalone", () => {
    expect(
      shouldShowIosInstallInstructions(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_4 like Mac OS X) AppleWebKit/605.1.15",
        false,
      ),
    ).toBe(true);
    expect(
      shouldShowIosInstallInstructions(
        "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15",
        false,
      ),
    ).toBe(true);
  });

  it("shows the button once the iOS PWA is installed", () => {
    expect(
      shouldShowIosInstallInstructions(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_4 like Mac OS X) AppleWebKit/605.1.15",
        true,
      ),
    ).toBe(false);
  });

  it("does not treat desktop Chrome as iOS", () => {
    expect(
      shouldShowIosInstallInstructions(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36",
        false,
      ),
    ).toBe(false);
  });
});
