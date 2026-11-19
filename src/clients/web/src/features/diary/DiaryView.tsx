import { useCallback, useEffect, useState } from "react";
import {
  ApiError,
  approveDiary,
  listDiary,
  rejectDiary,
  undoDiary,
  type DiaryEntryDto,
} from "../../api/rest";

function outcomeLabel(entry: DiaryEntryDto): string {
  if (entry.outcome === "proposed" && entry.proposalStatus === "rejected") {
    return "Rechazada";
  }

  switch (entry.outcome) {
    case "proposed":
      return "Propuesta";
    case "approved":
      return "Aprobada";
    case "rejected_version":
      return "Rechazada por versión";
    case "rejected_scan":
      return "Rechazada por escaneo";
    case "undone":
      return "Deshecha";
    default:
      return entry.outcome;
  }
}

function messageOf(err: unknown): string {
  if (err instanceof ApiError) {
    return err.message;
  }
  if (err instanceof Error) {
    return err.message;
  }
  return "Error de red.";
}

export function DiaryView() {
  const [entries, setEntries] = useState<DiaryEntryDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    const page = await listDiary();
    setEntries(page.entries);
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function first() {
      try {
        const page = await listDiary();
        if (!cancelled) {
          setEntries(page.entries);
        }
      } catch (err) {
        if (!cancelled) {
          setError(messageOf(err));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void first();
    return () => {
      cancelled = true;
    };
  }, []);

  async function run(id: string, action: () => Promise<{ outcome: string; toolName: string | null }>) {
    setBusyId(id);
    setError(null);
    setNotice(null);
    try {
      const result = await action();
      if (result.outcome === "confirmation_required") {
        const name = result.toolName?.trim() || "la herramienta";
        setNotice(
          `Esa acción es sensible (${name}). Quedó pendiente: hace falta la tarjeta de confirmación y no la ejecuté.`,
        );
      }
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section className="flex min-h-0 flex-1 flex-col gap-4 overflow-auto rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-4">
      <div>
        <h2 className="text-lg font-semibold">Diario</h2>
        <p className="text-sm text-zinc-600">
          Contradicciones revisadas. La razón se muestra; el texto del recuerdo no.
        </p>
      </div>
      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}
      {notice && <p className="text-sm text-zinc-700">{notice}</p>}
      {loading ? (
        <p className="text-sm text-zinc-600">Cargando…</p>
      ) : entries.length === 0 ? (
        <p className="text-sm text-zinc-600">No hay filas.</p>
      ) : (
        <ul className="space-y-3">
          {entries.map((entry) => {
            const pending = entry.outcome === "proposed" && entry.proposalStatus === "pending" && entry.proposalId;
            const canUndo = entry.outcome === "approved" && entry.previousId;
            return (
              <li key={entry.id} className="space-y-2 rounded-xl border border-[var(--line)] bg-white p-3">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <h3 className="font-medium">{outcomeLabel(entry)}</h3>
                  <time className="text-xs text-[var(--muted)]" dateTime={entry.createdAt}>
                    {new Date(entry.createdAt).toLocaleString("es-AR")}
                  </time>
                </div>
                <p className="text-sm text-zinc-700">{entry.reason}</p>
                {(pending || canUndo) && (
                  <div className="flex flex-wrap gap-2">
                    {pending && (
                      <>
                        <button
                          type="button"
                          disabled={busyId === entry.proposalId}
                          className="rounded-lg bg-[var(--ink)] px-3 py-1.5 text-sm text-white disabled:opacity-50"
                          onClick={() =>
                            void run(entry.proposalId!, () => approveDiary(entry.proposalId!))
                          }
                        >
                          Aprobar
                        </button>
                        <button
                          type="button"
                          disabled={busyId === entry.proposalId}
                          className="rounded-lg border border-[var(--line)] bg-white px-3 py-1.5 text-sm disabled:opacity-50"
                          onClick={() =>
                            void run(entry.proposalId!, () => rejectDiary(entry.proposalId!))
                          }
                        >
                          Rechazar
                        </button>
                      </>
                    )}
                    {canUndo && (
                      <button
                        type="button"
                        disabled={busyId === entry.id}
                        className="rounded-lg border border-[var(--line)] bg-white px-3 py-1.5 text-sm disabled:opacity-50"
                        onClick={() => void run(entry.id, () => undoDiary(entry.id))}
                      >
                        Deshacer
                      </button>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
