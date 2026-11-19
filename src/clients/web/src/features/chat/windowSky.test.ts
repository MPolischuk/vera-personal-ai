import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { windowBackgroundUrl, type TimeBand } from "./windowScene";
import {
  WINDOW_SKY_STORAGE_KEY,
  fetchWeatherCode,
  readWindowSky,
  resolveSkyCondition,
  skyCondition,
} from "./windowSky";

const featureDir = path.dirname(fileURLToPath(import.meta.url));

const OPEN_METEO_URL =
  "https://api.open-meteo.com/v1/forecast?latitude=-27.4692&longitude=-58.8306&current=weather_code&timezone=America%2FArgentina%2FCordoba&forecast_days=1";

describe("skyCondition", () => {
  it("AB4 maps each published code and rejects the rest", () => {
    for (const code of [0, 1]) {
      expect(skyCondition(code), String(code)).toBe("clear");
    }
    for (const code of [2, 3, 45, 48, 71, 73, 75, 77, 85, 86]) {
      expect(skyCondition(code), String(code)).toBe("cloudy");
    }
    for (const code of [51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 80, 81, 82, 95, 96, 99]) {
      expect(skyCondition(code), String(code)).toBe("rain");
    }
    for (const code of [4, 50, -1]) {
      expect(skyCondition(code), String(code)).toBeNull();
    }
  });
});

describe("failed weather query", () => {
  it("AB5 uses clear and a clear plate when nothing is stored", async () => {
    const storage = memoryStorage();
    const code = await fetchWeatherCode(failingFetch, new AbortController().signal);
    expect(code).toBeNull();
    const condition = resolveSkyCondition(
      code,
      storage,
      new Date("2026-09-22T18:00:00.000Z"),
    );
    expect(condition).toBe("clear");
    expect(storage.getItem(WINDOW_SKY_STORAGE_KEY)).toBeNull();
    const bands: TimeBand[] = ["day", "dusk", "night"];
    for (const band of bands) {
      expect(windowBackgroundUrl(band, condition)).toBe(
        `/avatars/backgrounds/window-${band}-clear.png`,
      );
    }
  });

  it("AB6 keeps the stored rain plate and does not rewrite it", async () => {
    const stored = JSON.stringify({
      condition: "rain",
      at: "2026-09-22T18:00:00.000Z",
    });
    const storage = memoryStorage({ [WINDOW_SKY_STORAGE_KEY]: stored });
    const code = await fetchWeatherCode(failingFetch, new AbortController().signal);
    const condition = resolveSkyCondition(
      code,
      storage,
      new Date("2026-09-22T18:30:00.000Z"),
    );
    expect(condition).toBe("rain");
    expect(storage.getItem(WINDOW_SKY_STORAGE_KEY)).toBe(stored);
    expect(windowBackgroundUrl("dusk", condition)).toBe(
      "/avatars/backgrounds/window-dusk-rain.png",
    );
  });

  it("does not replace a stored sky when the code is unknown", () => {
    const stored = JSON.stringify({
      condition: "rain",
      at: "2026-09-22T18:00:00.000Z",
    });
    const storage = memoryStorage({ [WINDOW_SKY_STORAGE_KEY]: stored });
    const condition = resolveSkyCondition(
      4,
      storage,
      new Date("2026-09-22T18:30:00.000Z"),
    );
    expect(skyCondition(4)).toBeNull();
    expect(condition).toBe("rain");
    expect(storage.getItem(WINDOW_SKY_STORAGE_KEY)).toBe(stored);
  });
});

describe("window sky cache", () => {
  it("treats invalid JSON, an unknown condition, a bad instant, and a throwing store as empty", () => {
    const invalid = memoryStorage({ [WINDOW_SKY_STORAGE_KEY]: "{" });
    const unknown = memoryStorage({
      [WINDOW_SKY_STORAGE_KEY]: JSON.stringify({
        condition: "snow",
        at: "2026-09-22T18:00:00.000Z",
      }),
    });
    const badInstant = memoryStorage({
      [WINDOW_SKY_STORAGE_KEY]: JSON.stringify({
        condition: "cloudy",
        at: "yesterday",
      }),
    });
    expect(readWindowSky(invalid)).toBeNull();
    expect(readWindowSky(unknown)).toBeNull();
    expect(readWindowSky(badInstant)).toBeNull();
    expect(readWindowSky(throwingStorage())).toBeNull();
    expect(() =>
      resolveSkyCondition(0, throwingStorage(), new Date("2026-09-22T18:00:00.000Z")),
    ).not.toThrow();
  });
});

describe("fetchWeatherCode", () => {
  it("asks the design URL and returns null when weather_code is missing", async () => {
    const calls: Array<{ url: string; signal: AbortSignal | undefined }> = [];
    const signal = new AbortController().signal;
    const fetchImpl: typeof fetch = async (input, init) => {
      calls.push({ url: String(input), signal: init?.signal ?? undefined });
      return new Response(JSON.stringify({ current: {} }), { status: 200 });
    };
    const code = await fetchWeatherCode(fetchImpl, signal);
    expect(calls).toEqual([{ url: OPEN_METEO_URL, signal }]);
    expect(code).toBeNull();
  });

  it("returns a numeric weather_code and ignores anything else", async () => {
    const signal = new AbortController().signal;
    await expect(
      fetchWeatherCode(jsonFetch({ current: { weather_code: 61 } }), signal),
    ).resolves.toBe(61);
    await expect(
      fetchWeatherCode(jsonFetch({ current: { weather_code: "61" } }), signal),
    ).resolves.toBeNull();
    await expect(
      fetchWeatherCode(jsonFetch({ current: { weather_code: 0 } }, 503), signal),
    ).resolves.toBeNull();
  });
});

describe("ChatView wiring", () => {
  it("asks the hook and does not fetch or store the sky itself", () => {
    const source = fs.readFileSync(path.join(featureDir, "ChatView.tsx"), "utf8");
    expect(source).toContain("useWindowBackground");
    expect(source).not.toContain("open-meteo");
    expect(source).not.toContain("localStorage");
  });
});

function memoryStorage(initial: Record<string, string> = {}): Storage {
  const items = new Map(Object.entries(initial));
  return {
    get length() {
      return items.size;
    },
    clear() {
      items.clear();
    },
    getItem(key: string) {
      return items.get(key) ?? null;
    },
    key(index: number) {
      return [...items.keys()][index] ?? null;
    },
    removeItem(key: string) {
      items.delete(key);
    },
    setItem(key: string, value: string) {
      items.set(key, value);
    },
  };
}

function throwingStorage(): Storage {
  const denied = () => {
    throw new Error("storage denied");
  };
  return {
    get length() {
      denied();
      return 0;
    },
    clear: denied,
    getItem: denied,
    key: denied,
    removeItem: denied,
    setItem: denied,
  };
}

const failingFetch: typeof fetch = async () => {
  throw new Error("network down");
};

function jsonFetch(body: unknown, status = 200): typeof fetch {
  return async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });
}
