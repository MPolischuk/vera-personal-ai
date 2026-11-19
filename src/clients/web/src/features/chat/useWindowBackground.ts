import { useEffect, useState } from "react";
import type { BackdropAlignment } from "../avatar/sceneBackdrop";
import { DEMO_SCENE, selectSceneBackground } from "./demoScene";
import { WINDOW_ALIGNMENT } from "./sceneBackgrounds";
import type { SkyCondition } from "./windowScene";
import { fetchWeatherCode, readWindowSky, resolveSkyCondition } from "./windowSky";

const BAND_REFRESH_MS = 60_000;
const WEATHER_REFRESH_MS = 20 * 60_000;
const WEATHER_TIMEOUT_MS = 4_000;

export function useWindowBackground(): {
  backgroundUrl: string;
  backgroundAlign: BackdropAlignment;
} {
  const demoEnabled = DEMO_SCENE.enabled;
  const [backgroundUrl, setBackgroundUrl] = useState(() =>
    selectSceneBackground(new Date(), cachedSkyCondition()).url,
  );

  useEffect(() => {
    if (demoEnabled) {
      return;
    }

    let cancelled = false;
    let inFlight = false;
    let lastAttemptAt: number | null = null;
    let controller: AbortController | null = null;
    let condition = cachedSkyCondition();

    function publish(sky: SkyCondition) {
      const next = selectSceneBackground(new Date(), sky, false).url;
      setBackgroundUrl((current) => (current === next ? current : next));
    }

    function refreshBand() {
      if (cancelled) {
        return;
      }
      publish(condition);
    }

    async function consultWeather(): Promise<void> {
      if (cancelled || inFlight) {
        return;
      }
      inFlight = true;
      const attempt = new AbortController();
      controller = attempt;
      const timeoutId = setTimeout(() => attempt.abort(), WEATHER_TIMEOUT_MS);
      try {
        const code = await fetchWeatherCode(fetch, attempt.signal);
        if (!cancelled) {
          condition = resolveSkyCondition(code, localStorage, new Date());
          publish(condition);
        }
      } finally {
        clearTimeout(timeoutId);
        if (controller === attempt) {
          controller = null;
        }
        inFlight = false;
        lastAttemptAt = Date.now();
      }
    }

    function onVisibilityChange() {
      if (document.visibilityState !== "visible") {
        return;
      }
      refreshBand();
      const stale =
        lastAttemptAt === null ||
        Date.now() - lastAttemptAt > WEATHER_REFRESH_MS;
      if (stale) {
        void consultWeather();
      }
    }

    refreshBand();
    void consultWeather();
    const bandTimer = setInterval(refreshBand, BAND_REFRESH_MS);
    const weatherTimer = setInterval(() => {
      void consultWeather();
    }, WEATHER_REFRESH_MS);
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      cancelled = true;
      clearInterval(bandTimer);
      clearInterval(weatherTimer);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      controller?.abort();
    };
  }, [demoEnabled]);

  if (demoEnabled) {
    return {
      backgroundUrl: DEMO_SCENE.url,
      backgroundAlign: DEMO_SCENE.alignment,
    };
  }

  return { backgroundUrl, backgroundAlign: WINDOW_ALIGNMENT };
}

function cachedSkyCondition(): SkyCondition {
  return readWindowSky(localStorage)?.condition ?? "clear";
}
