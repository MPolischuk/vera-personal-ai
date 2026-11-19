import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sceneBackgroundUrl, type SceneBackgroundId } from "./sceneBackgrounds";
import {
  solarSunriseSunset,
  timeBand,
  windowBackgroundUrl,
  type SkyCondition,
  type TimeBand,
} from "./windowScene";

const featureDir = path.dirname(fileURLToPath(import.meta.url));

describe("timeBand", () => {
  const sunrise = new Date("2026-06-21T10:00:00.000Z");
  const sunset = new Date("2026-06-21T22:00:00.000Z");

  function at(base: Date, deltaMs: number): Date {
    return new Date(base.getTime() + deltaMs);
  }

  // The 45 min, 30 min, and 1 s offsets are written here so they cannot drift
  // with the constants inside windowScene.ts.
  const edges: Array<[string, Date, TimeBand]> = [
    ["sunrise - 1s", at(sunrise, -1 * 1000), "night"],
    ["sunrise", sunrise, "day"],
    ["sunset - 45min - 1s", at(sunset, -(45 * 60 * 1000) - 1 * 1000), "day"],
    ["sunset - 45min", at(sunset, -(45 * 60 * 1000)), "dusk"],
    ["sunset", sunset, "dusk"],
    ["sunset + 30min - 1s", at(sunset, 30 * 60 * 1000 - 1 * 1000), "dusk"],
    ["sunset + 30min", at(sunset, 30 * 60 * 1000), "night"],
  ];

  it("AB1 clamps the seven injected edges", () => {
    for (const [label, instant, band] of edges) {
      expect(timeBand(instant, sunrise, sunset), label).toBe(band);
    }
  });
});

describe("solar clock source", () => {
  it("AB2 does not read the device local clock", () => {
    const source = fs.readFileSync(path.join(featureDir, "windowScene.ts"), "utf8");
    const timeBandSource = functionSource(source, "timeBand");
    const solarSource = functionSource(source, "solarSunriseSunset");
    const forbidden = [
      "getHours",
      "getMinutes",
      "getDate",
      "getTimezoneOffset",
      "toLocaleString",
    ];
    for (const token of forbidden) {
      expect(timeBandSource, token).not.toContain(token);
      expect(solarSource, token).not.toContain(token);
      expect(source, token).not.toContain(token);
    }
  });
});

describe("published sunrise pair", () => {
  it("AB3 stays within 3 minutes of the published pair", () => {
    // Queried 2026-09-22:
    // https://api.sunrise-sunset.org/json?lat=-27.4692&lng=-58.8306&date=2026-06-21&formatted=0
    // sunrise 2026-06-21T10:43:46+00:00
    // sunset  2026-06-21T21:10:36+00:00
    const pair = solarSunriseSunset(new Date("2026-06-21T15:00:00.000Z"));
    const toleranceMs = 3 * 60 * 1000;
    expect(
      Math.abs(pair.sunrise.getTime() - Date.parse("2026-06-21T10:43:46Z")),
    ).toBeLessThanOrEqual(toleranceMs);
    expect(
      Math.abs(pair.sunset.getTime() - Date.parse("2026-06-21T21:10:36Z")),
    ).toBeLessThanOrEqual(toleranceMs);
  });

  it("keeps the Cordoba civil date after UTC midnight", () => {
    const onCivilDay = solarSunriseSunset(new Date("2026-06-21T15:00:00.000Z"));
    const afterUtcMidnight = solarSunriseSunset(new Date("2026-06-22T02:00:00.000Z"));
    const previousCivilDay = solarSunriseSunset(new Date("2026-06-21T02:00:00.000Z"));
    expect(afterUtcMidnight.sunrise.getTime()).toBe(onCivilDay.sunrise.getTime());
    expect(afterUtcMidnight.sunset.getTime()).toBe(onCivilDay.sunset.getTime());
    expect(previousCivilDay.sunrise.getTime()).not.toBe(onCivilDay.sunrise.getTime());
  });
});

describe("windowBackgroundUrl", () => {
  it("matches sceneBackgroundUrl for all nine plates", () => {
    const bands: TimeBand[] = ["day", "dusk", "night"];
    const conditions: SkyCondition[] = ["clear", "cloudy", "rain"];
    for (const band of bands) {
      for (const condition of conditions) {
        const id: SceneBackgroundId = `window-${band}-${condition}`;
        expect(windowBackgroundUrl(band, condition)).toBe(sceneBackgroundUrl(id));
      }
    }
  });
});

function functionSource(source: string, name: string): string {
  const marker = `function ${name}`;
  const start = source.indexOf(marker);
  if (start < 0) {
    return "";
  }
  const next = source.indexOf("\nexport function ", start + marker.length);
  return next < 0 ? source.slice(start) : source.slice(start, next);
}
