import { useEffect, useState } from "react";
import {
  ApiError,
  deleteMemory,
  deletePreference,
  downloadMemoryExport,
  listMemories,
  listMemoryJourney,
  listPreferences,
  restoreMemory,
  unpinMemory,
  upsertPreference,
  type MemoryItemDto,
  type MemoryJourneyDto,
  type MemoryJourneyItemDto,
  type PreferenceItemDto,
} from "../../api/rest";

const pageSize = 50;
const emptyJourney: MemoryJourneyDto = { current: [], replaced: [], forgotten: [] };
const backupNotice =
  "Si olvidás algo, Vera lo borra de la base a las 48 horas. En el backup cifrado sigue hasta 14 días, y hace falta tu clave privada para recuperarlo.";

export function MemoryView() {
  const [memories, setMemories] = useState<MemoryItemDto[]>([]);
  const [memorySkip, setMemorySkip] = useState(0);
  const [memoryTake, setMemoryTake] = useState(pageSize);
  const [hasMore, setHasMore] = useState(false);
  const [journey, setJourney] = useState<MemoryJourneyDto>(emptyJourney);
  const [preferences, setPreferences] = useState<PreferenceItemDto[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [memoryPage, preferenceRows, journeyPage] = await Promise.all([
          listMemories(0, pageSize),
          listPreferences(),
          listMemoryJourney(),
        ]);
        if (cancelled) {
          return;
        }
        setMemories(memoryPage.items);
        setMemorySkip(memoryPage.skip);
        setMemoryTake(memoryPage.take);
        setHasMore(memoryPage.items.length === memoryPage.take);
        setJourney(journeyPage);
        setPreferences(preferenceRows);
        setDrafts(Object.fromEntries(preferenceRows.map((row) => [row.key, row.value])));
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

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  async function reloadMemories() {
    const [memoryPage, journeyPage] = await Promise.all([
      listMemories(0, pageSize),
      listMemoryJourney(),
    ]);
    setMemories(memoryPage.items);
    setMemorySkip(memoryPage.skip);
    setMemoryTake(memoryPage.take);
    setHasMore(memoryPage.items.length === memoryPage.take);
    setJourney(journeyPage);
  }

  function successorText(id: string | null): string {
    if (!id) {
      return "";
    }

    return (
      journey.current.find((row) => row.id === id)?.content ??
      memories.find((row) => row.id === id)?.content ??
      id
    );
  }

  async function loadMore() {
    setBusyKey("more");
    setError(null);
    try {
      const page = await listMemories(memorySkip + memoryTake, pageSize);
      setMemories((current) => [...current, ...page.items]);
      setMemorySkip(page.skip);
      setMemoryTake(page.take);
      setHasMore(page.items.length === page.take);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function unpin(item: MemoryItemDto) {
    setBusyKey(item.id);
    setError(null);
    try {
      await unpinMemory(item.id);
      setMemories((current) =>
        current.map((row) => (row.id === item.id ? { ...row, pinned: false } : row)),
      );
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function forget(item: MemoryItemDto) {
    if (!window.confirm("¿Borrar este recuerdo?")) {
      return;
    }

    setBusyKey(item.id);
    setError(null);
    try {
      await deleteMemory(item.id);
      await reloadMemories();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function restore(item: MemoryJourneyItemDto) {
    setBusyKey(item.id);
    setError(null);
    try {
      await restoreMemory(item.id);
      await reloadMemories();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError("Ese recuerdo ya no se puede recuperar.");
      } else {
        setError(messageOf(err));
      }
    } finally {
      setBusyKey(null);
    }
  }

  async function savePreference(row: PreferenceItemDto) {
    setBusyKey(row.key);
    setError(null);
    try {
      const saved = await upsertPreference(row.key, drafts[row.key] ?? "");
      setPreferences((current) =>
        current.map((item) => (item.key === saved.key ? saved : item)),
      );
      setDrafts((current) => ({ ...current, [saved.key]: saved.value }));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function removePreference(row: PreferenceItemDto) {
    if (!window.confirm("¿Quitar esta preferencia?")) {
      return;
    }

    setBusyKey(row.key);
    setError(null);
    try {
      await deletePreference(row.key);
      setPreferences((current) => current.filter((item) => item.key !== row.key));
      setDrafts((current) => {
        const next = { ...current };
        delete next[row.key];
        return next;
      });
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function exportJson() {
    setBusyKey("export");
    setError(null);
    try {
      await downloadMemoryExport();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Memoria</h2>
        <button
          type="button"
          className="rounded-lg bg-[var(--accent)] px-3 py-1.5 text-sm text-white disabled:opacity-50"
          disabled={busyKey === "export"}
          onClick={() => void exportJson()}
        >
          Exportar JSON
        </button>
      </div>

      {error && (
        <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-[var(--danger)]">
          {error}
        </p>
      )}

      <section className="space-y-4 rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
        <div className="space-y-2">
          <h3 className="font-semibold">Qué aprendió Vera</h3>
          <p className="text-sm text-[var(--muted)]">{backupNotice}</p>
        </div>

        <div className="space-y-3">
          <h4 className="text-sm font-semibold">Vigentes</h4>
          {loading ? (
            <p className="text-sm text-[var(--muted)]">Cargando…</p>
          ) : memories.length === 0 ? (
            <p className="text-sm text-[var(--muted)]">No hay recuerdos vigentes.</p>
          ) : (
            <ul className="space-y-3">
              {memories.map((item) => (
                <li
                  key={item.id}
                  id={`memory-${item.id}`}
                  className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
                >
                  <div className="min-w-0">
                    <p className="text-sm">{item.content}</p>
                    {item.index_terms ? (
                      <p className="mt-1 text-xs text-[var(--muted)]">{item.index_terms}</p>
                    ) : null}
                    <p className="mt-1 text-xs text-[var(--muted)]">
                      {item.kind}
                      {item.write_provenance === "inferred" ? " · inferido" : ""}
                      {item.pinned ? " · Fijado" : ""}
                      {" · "}
                      {new Date(item.created_at).toLocaleString("es-AR")}
                    </p>
                  </div>
                  {item.pinned && (
                    <button
                      type="button"
                      className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-sm disabled:opacity-50"
                      disabled={busyKey === item.id}
                      onClick={() => void unpin(item)}
                    >
                      Desfijar
                    </button>
                  )}
                  <button
                    type="button"
                    className="rounded-lg border border-[var(--danger)] px-3 py-1.5 text-sm text-[var(--danger)] disabled:opacity-50"
                    disabled={busyKey === item.id}
                    onClick={() => void forget(item)}
                  >
                    Borrar
                  </button>
                </li>
              ))}
            </ul>
          )}
          {hasMore && (
            <button
              type="button"
              className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-sm disabled:opacity-50"
              disabled={busyKey === "more"}
              onClick={() => void loadMore()}
            >
              Cargar más
            </button>
          )}
        </div>

        <div className="space-y-3">
          <h4 className="text-sm font-semibold">Reemplazados</h4>
          {loading ? (
            <p className="text-sm text-[var(--muted)]">Cargando…</p>
          ) : journey.replaced.length === 0 ? (
            <p className="text-sm text-[var(--muted)]">No hay recuerdos reemplazados.</p>
          ) : (
            <ul className="space-y-3">
              {journey.replaced.map((item) => (
                <li
                  key={item.id}
                  className="border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
                >
                  {item.content ? <p className="text-sm">{item.content}</p> : null}
                  <p className="mt-1 text-xs text-[var(--muted)]">
                    {item.kind}
                    {item.write_provenance === "inferred" ? " · inferido" : ""}
                    {" · "}
                    <a className="underline" href={`#memory-${item.superseded_by ?? ""}`}>
                      Lo reemplazó: {successorText(item.superseded_by)}
                    </a>
                  </p>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="space-y-3">
          <h4 className="text-sm font-semibold">Olvidados</h4>
          {loading ? (
            <p className="text-sm text-[var(--muted)]">Cargando…</p>
          ) : journey.forgotten.length === 0 ? (
            <p className="text-sm text-[var(--muted)]">No hay recuerdos olvidados.</p>
          ) : (
            <ul className="space-y-3">
              {journey.forgotten.map((item) => (
                <li
                  key={item.id}
                  className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
                >
                  {item.content == null ? (
                    <p className="text-sm text-[var(--muted)]">Olvidado</p>
                  ) : (
                    <>
                      <div className="min-w-0">
                        <p className="text-sm">{item.content}</p>
                        {item.index_terms ? (
                          <p className="mt-1 text-xs text-[var(--muted)]">{item.index_terms}</p>
                        ) : null}
                        <p className="mt-1 text-xs text-[var(--muted)]">
                          {item.kind}
                          {item.write_provenance === "inferred" ? " · inferido" : ""}
                          {" · "}
                          {new Date(item.created_at).toLocaleString("es-AR")}
                        </p>
                      </div>
                      <button
                        type="button"
                        className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-sm disabled:opacity-50"
                        disabled={busyKey === item.id}
                        onClick={() => void restore(item)}
                      >
                        Restaurar
                      </button>
                    </>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      <section className="space-y-3 rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
        <h3 className="font-semibold">Preferencias</h3>
        {loading ? (
          <p className="text-sm text-[var(--muted)]">Cargando…</p>
        ) : preferences.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">No hay preferencias.</p>
        ) : (
          <ul className="space-y-3">
            {preferences.map((row) => (
              <li
                key={row.key}
                className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
              >
                <span className="min-w-32 font-medium">{row.key}</span>
                <input
                  className="min-w-40 flex-1 rounded-lg border border-[var(--line)] bg-white px-3 py-1.5 text-sm"
                  value={drafts[row.key] ?? ""}
                  aria-label={`Valor de ${row.key}`}
                  onChange={(event) =>
                    setDrafts((current) => ({ ...current, [row.key]: event.target.value }))
                  }
                />
                <button
                  type="button"
                  className="rounded-lg bg-[var(--ink)] px-3 py-1.5 text-sm text-white disabled:opacity-50"
                  disabled={busyKey === row.key}
                  onClick={() => void savePreference(row)}
                >
                  Guardar
                </button>
                <button
                  type="button"
                  className="rounded-lg border border-[var(--danger)] px-3 py-1.5 text-sm text-[var(--danger)] disabled:opacity-50"
                  disabled={busyKey === row.key}
                  onClick={() => void removePreference(row)}
                >
                  Quitar
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function messageOf(err: unknown): string {
  if (err instanceof ApiError) {
    return err.message;
  }
  return "No se pudo completar la operación.";
}
