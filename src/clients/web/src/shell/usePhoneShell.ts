import { useLayoutEffect } from "react";
import { useAppStore } from "../store/appStore";

const PHONE_SHELL_QUERY = "(max-width: 767px)";

/** Writes `phoneShell` before paint so the desktop nav does not flash on a phone. */
export function usePhoneShell(): void {
  const setPhoneShell = useAppStore((s) => s.setPhoneShell);

  useLayoutEffect(() => {
    if (typeof window.matchMedia !== "function") {
      return;
    }

    const media = window.matchMedia(PHONE_SHELL_QUERY);
    const sync = () => {
      setPhoneShell(media.matches);
    };

    sync();
    media.addEventListener("change", sync);
    return () => media.removeEventListener("change", sync);
  }, [setPhoneShell]);
}
