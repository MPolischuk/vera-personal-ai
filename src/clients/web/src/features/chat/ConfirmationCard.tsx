import { useEffect, useMemo, useState } from "react";
import { useAppStore } from "../../store/appStore";

function formatExpiry(expiresAt: string): { label: string; expired: boolean } {
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (Number.isNaN(ms) || ms <= 0) {
    return { label: "0:00", expired: true };
  }
  const totalSeconds = Math.ceil(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return {
    label: `${minutes}:${seconds.toString().padStart(2, "0")}`,
    expired: false,
  };
}

export function ConfirmationCard() {
  const pending = useAppStore((s) => s.pendingConfirmation);
  const respond = useAppStore((s) => s.respondToConfirmation);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!pending) {
      return;
    }
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [pending]);

  const countdown = useMemo(() => {
    if (!pending) {
      return { label: "0:00", expired: true };
    }
    void now;
    return formatExpiry(pending.expiresAt);
  }, [pending, now]);

  if (!pending) {
    return null;
  }

  return (
    <div className="rounded-2xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-[#6a4b10]">
      <div className="mb-2 flex items-center justify-between gap-3">
        <p className="font-semibold">Confirmación requerida</p>
        <span className="rounded-full bg-white/80 px-2 py-0.5 text-xs">
          {countdown.label}
        </span>
      </div>
      <p className="mb-1 text-xs uppercase tracking-wide opacity-80">
        Herramienta: {pending.toolName}
      </p>
      <p className="mb-3 whitespace-pre-wrap">{pending.prompt}</p>
      <div className="flex gap-2">
        <button
          type="button"
          disabled={countdown.expired}
          className="rounded-lg bg-[var(--accent)] px-3 py-1.5 text-white disabled:opacity-50"
          onClick={() => void respond(true)}
        >
          Aprobar
        </button>
        <button
          type="button"
          disabled={countdown.expired}
          className="rounded-lg border border-[var(--danger)] px-3 py-1.5 text-[var(--danger)] disabled:opacity-50"
          onClick={() => void respond(false)}
        >
          Rechazar
        </button>
      </div>
      {countdown.expired && (
        <p className="mt-2 text-xs">
          El tiempo venció. Esperando cierre del servidor…
        </p>
      )}
    </div>
  );
}
