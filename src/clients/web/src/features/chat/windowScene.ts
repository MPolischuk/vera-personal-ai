import { sceneBackgroundUrl, type SceneBackgroundId } from "./sceneBackgrounds";

export type TimeBand = "day" | "dusk" | "night";

export type SkyCondition = "clear" | "cloudy" | "rain";

const LATITUDE_DEGREES = -27.4692;
const LONGITUDE_DEGREES = -58.8306;
const CIVIL_TIME_ZONE = "America/Argentina/Cordoba";

const DUSK_BEFORE_SUNSET_MS = 45 * 60 * 1000;
const NIGHT_AFTER_SUNSET_MS = 30 * 60 * 1000;

const SOLAR_ZENITH_DEGREES = 90.833;
const MINUTES_PER_DEGREE = 4;
const SOLAR_NOON_MINUTES = 720;
const FRACTIONAL_YEAR_HOUR_UTC = 12;

export function timeBand(at: Date, sunrise: Date, sunset: Date): TimeBand {
  const t = at.getTime();
  const rise = sunrise.getTime();
  const set = sunset.getTime();
  const duskStart = set - DUSK_BEFORE_SUNSET_MS;
  const nightStart = set + NIGHT_AFTER_SUNSET_MS;
  if (t < rise || t >= nightStart) {
    return "night";
  }
  if (t < duskStart) {
    return "day";
  }
  return "dusk";
}

export function solarSunriseSunset(at: Date): { sunrise: Date; sunset: Date } {
  const { year, month, day } = cordobaCivilDate(at);
  const { sunriseMinutes, sunsetMinutes } = solarMinutesUtc(year, month, day);
  const utcMidnight = Date.UTC(year, month - 1, day);
  return {
    sunrise: new Date(utcMidnight + sunriseMinutes * 60_000),
    sunset: new Date(utcMidnight + sunsetMinutes * 60_000),
  };
}

export function windowBackgroundUrl(
  band: TimeBand,
  condition: SkyCondition,
): string {
  const id: SceneBackgroundId = `window-${band}-${condition}`;
  return sceneBackgroundUrl(id);
}

function cordobaCivilDate(at: Date): { year: number; month: number; day: number } {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: CIVIL_TIME_ZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(at);
  return {
    year: partNumber(parts, "year"),
    month: partNumber(parts, "month"),
    day: partNumber(parts, "day"),
  };
}

function partNumber(parts: Intl.DateTimeFormatPart[], type: Intl.DateTimeFormatPartTypes): number {
  const value = parts.find((part) => part.type === type)?.value;
  return Number(value);
}

function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

function dayOfYear(year: number, month: number, day: number): number {
  const daysInMonth = [
    0,
    31,
    isLeapYear(year) ? 29 : 28,
    31,
    30,
    31,
    30,
    31,
    31,
    30,
    31,
    30,
    31,
  ];
  let ordinal = day;
  for (let monthIndex = 1; monthIndex < month; monthIndex += 1) {
    ordinal += daysInMonth[monthIndex];
  }
  return ordinal;
}

function solarMinutesUtc(
  year: number,
  month: number,
  day: number,
): { sunriseMinutes: number; sunsetMinutes: number } {
  const ordinal = dayOfYear(year, month, day);
  const daysInYear = isLeapYear(year) ? 366 : 365;
  const gamma =
    ((2 * Math.PI) / daysInYear) *
    (ordinal - 1 + (FRACTIONAL_YEAR_HOUR_UTC - 12) / 24);
  const eqtime =
    229.18 *
    (0.000075 +
      0.001868 * Math.cos(gamma) -
      0.032077 * Math.sin(gamma) -
      0.014615 * Math.cos(2 * gamma) -
      0.040849 * Math.sin(2 * gamma));
  const decl =
    0.006918 -
    0.399912 * Math.cos(gamma) +
    0.070257 * Math.sin(gamma) -
    0.006758 * Math.cos(2 * gamma) +
    0.000907 * Math.sin(2 * gamma) -
    0.002697 * Math.cos(3 * gamma) +
    0.00148 * Math.sin(3 * gamma);
  const latitude = (LATITUDE_DEGREES * Math.PI) / 180;
  const zenith = (SOLAR_ZENITH_DEGREES * Math.PI) / 180;
  const cosHourAngle =
    Math.cos(zenith) / (Math.cos(latitude) * Math.cos(decl)) -
    Math.tan(latitude) * Math.tan(decl);
  const hourAngle = (Math.acos(cosHourAngle) * 180) / Math.PI;
  const sunriseMinutes =
    SOLAR_NOON_MINUTES -
    MINUTES_PER_DEGREE * (LONGITUDE_DEGREES + hourAngle) -
    eqtime;
  const sunsetMinutes =
    SOLAR_NOON_MINUTES -
    MINUTES_PER_DEGREE * (LONGITUDE_DEGREES - hourAngle) -
    eqtime;
  return { sunriseMinutes, sunsetMinutes };
}
