import { useState } from "react";
import { ApiError, createTelegramLink } from "../../api/rest";

export function LinkTelegramDialog({ linked }: { linked: boolean }) {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);

  async function create() {
    setBusy(true);
    setError(null);
    setCopied(false);
    try {
      const link = await createTelegramLink();
      setUrl(link.url);
    } catch (caught) {
      const status = caught instanceof ApiError ? caught.status : 0;
      setError(
        status === 409
          ? "Telegram no está disponible para vincular."
          : "No pude armar el enlace. Probá de nuevo.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function copy() {
    if (!url) {
      return;
    }

    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <section className="space-y-3">
      <h3 className="text-sm font-semibold">Vincular Telegram</h3>
      <p className="text-sm text-[var(--muted)]">
        {linked
          ? "Ya hay un chat vinculado. Si querés cambiarlo, desvinculalo primero."
          : "El enlace vence en 10 minutos y sirve una sola vez. Abrilo en el teléfono donde usás Telegram."}
      </p>
      <button
        type="button"
        onClick={() => void create()}
        disabled={busy}
        className="min-h-12 w-full rounded-xl bg-[var(--ink)] px-4 text-sm font-medium text-white disabled:opacity-50 sm:w-auto"
      >
        {busy ? "Armando el enlace…" : "Crear enlace"}
      </button>
      {url && (
        <div className="space-y-2">
          <input
            readOnly
            value={url}
            className="w-full rounded-xl border border-[var(--line)] bg-white px-3 py-3 text-base break-all"
            onFocus={(event) => event.currentTarget.select()}
          />
          <button
            type="button"
            onClick={() => void copy()}
            className="min-h-12 rounded-xl border border-[var(--line)] bg-white px-4 text-sm"
          >
            {copied ? "Copiado" : "Copiar enlace"}
          </button>
        </div>
      )}
      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}
    </section>
  );
}
