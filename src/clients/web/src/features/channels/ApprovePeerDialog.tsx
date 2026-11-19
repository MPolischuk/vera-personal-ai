import { useState } from "react";
import { ApiError, approveChannelPeer } from "../../api/rest";
import { pairingErrorMessage } from "./pairingFormat";

export function ApprovePeerDialog({
  peerId,
  displayName,
  onApproved,
  onClose,
}: {
  peerId: string;
  displayName: string;
  onApproved: () => void;
  onClose: () => void;
}) {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      await approveChannelPeer(peerId, code);
      onApproved();
    } catch (caught) {
      const status = caught instanceof ApiError ? caught.status : 0;
      const problem = caught instanceof ApiError ? caught.code : undefined;
      setError(pairingErrorMessage(status, problem));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="space-y-3 rounded-xl border border-[var(--line)] bg-slate-50 p-3"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <p className="text-sm">
        Aprobá a <span className="font-medium">{displayName}</span> con el código que ves en Telegram.
      </p>
      <label className="block text-sm">
        Código
        <input
          value={code}
          onChange={(event) => setCode(event.target.value)}
          autoComplete="one-time-code"
          autoCapitalize="characters"
          inputMode="text"
          spellCheck={false}
          className="mt-1 w-full rounded-xl border border-[var(--line)] bg-white px-3 py-3 text-base tracking-widest uppercase"
        />
      </label>
      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}
      <div className="flex flex-col gap-2 sm:flex-row">
        <button
          type="submit"
          disabled={busy || code.trim().length === 0}
          className="min-h-12 flex-1 rounded-xl bg-[var(--accent)] px-4 text-sm font-medium text-white disabled:opacity-50"
        >
          {busy ? "Aprobando…" : "Aprobar"}
        </button>
        <button
          type="button"
          onClick={onClose}
          className="min-h-12 rounded-xl border border-[var(--line)] bg-white px-4 text-sm"
        >
          Cancelar
        </button>
      </div>
    </form>
  );
}
