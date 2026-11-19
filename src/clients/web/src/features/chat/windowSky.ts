import type { SkyCondition } from "./windowScene";

export const WINDOW_SKY_STORAGE_KEY = "assistant.windowSky";

const OPEN_METEO_FORECAST_URL =
  "https://api.open-meteo.com/v1/forecast?latitude=-27.4692&longitude=-58.8306&current=weather_code&timezone=America%2FArgentina%2FCordoba&forecast_days=1";

const SKY_BY_WMO_CODE: Readonly<Record<number, SkyCondition>> = {
  0: "clear",
  1: "clear",
  2: "cloudy",
  3: "cloudy",
  45: "cloudy",
  48: "cloudy",
  51: "rain",
  53: "rain",
  55: "rain",
  56: "rain",
  57: "rain",
  61: "rain",
  63: "rain",
  65: "rain",
  66: "rain",
  67: "rain",
  71: "cloudy",
  73: "cloudy",
  75: "cloudy",
  77: "cloudy",
  80: "rain",
  81: "rain",
  82: "rain",
  85: "cloudy",
  86: "cloudy",
  95: "rain",
  96: "rain",
  99: "rain",
};

export type StoredWindowSky = {
  condition: SkyCondition;
  at: string;
};

export function skyCondition(wmoCode: number): SkyCondition | null {
  return SKY_BY_WMO_CODE[wmoCode] ?? null;
}

export function selectSkyCondition(
  mapped: SkyCondition | null,
  stored: SkyCondition | null,
): SkyCondition {
  return mapped ?? stored ?? "clear";
}

export function readWindowSky(storage: Storage): StoredWindowSky | null {
  try {
    const raw = storage.getItem(WINDOW_SKY_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    return parseStoredWindowSky(raw);
  } catch {
    return null;
  }
}

export function writeWindowSky(
  storage: Storage,
  condition: SkyCondition,
  at: Date,
): void {
  try {
    storage.setItem(
      WINDOW_SKY_STORAGE_KEY,
      JSON.stringify({ condition, at: at.toISOString() }),
    );
  } catch {
    // A blocked store leaves the previous condition in place.
  }
}

export function resolveSkyCondition(
  wmoCode: number | null,
  storage: Storage,
  at: Date,
): SkyCondition {
  const mapped = wmoCode === null ? null : skyCondition(wmoCode);
  const stored = readWindowSky(storage)?.condition ?? null;
  if (mapped !== null) {
    writeWindowSky(storage, mapped, at);
  }
  return selectSkyCondition(mapped, stored);
}

export async function fetchWeatherCode(
  fetchImpl: typeof fetch,
  signal: AbortSignal,
): Promise<number | null> {
  try {
    const response = await fetchImpl(OPEN_METEO_FORECAST_URL, { signal });
    if (!response.ok) {
      return null;
    }
    return weatherCodeFromBody(await response.json());
  } catch {
    return null;
  }
}

function parseStoredWindowSky(raw: string): StoredWindowSky | null {
  const parsed: unknown = JSON.parse(raw);
  if (!isRecord(parsed)) {
    return null;
  }
  const condition = parsed.condition;
  const at = parsed.at;
  if (!isSkyCondition(condition) || !isIsoInstant(at)) {
    return null;
  }
  return { condition, at };
}

function isSkyCondition(value: unknown): value is SkyCondition {
  return value === "clear" || value === "cloudy" || value === "rain";
}

function isIsoInstant(value: unknown): value is string {
  return typeof value === "string" && Number.isFinite(Date.parse(value));
}

function weatherCodeFromBody(body: unknown): number | null {
  if (!isRecord(body) || !isRecord(body.current)) {
    return null;
  }
  const code = body.current.weather_code;
  if (typeof code !== "number" || !Number.isFinite(code)) {
    return null;
  }
  return code;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
