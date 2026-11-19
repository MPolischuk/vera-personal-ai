import { useEffect, useState } from "react";
import { ApiError, cancelActivity, getActivity, type ActivityDto } from "../../api/rest";

const stateLabels: Record<string, string> = {
  submitted: "Enviada",
  working: "En curso",
  "input-required": "Espera confirmación",
  "auth-required": "Espera autorización",
  completed: "Terminada",
  failed: "Falló",
  canceled: "Cancelada",
  rejected: "Rechazada",
};

const cancellable = new Set(["submitted", "working", "input-required"]);

function stateLabel(state: string): string {
  return stateLabels[state] ?? state;
}

function when(value: string | null): string {
  if (!value) {
    return "";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString("es-AR");
}

export function ActivityView() {
  const [page, setPage] = useState<ActivityDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getActivity()
      .then((loaded) => {
        if (!cancelled) {
          setPage(loaded);
          setError(null);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : "No se pudo cargar la actividad.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function cancel(id: string) {
    setBusyId(id);
    setError(null);
    try {
      await cancelActivity(id);
      setPage(await getActivity());
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : "No se pudo cancelar la tarea.");
    } finally {
      setBusyId(null);
    }
  }

  const tasks = page?.tasks ?? [];
  const tools = page?.tools ?? [];

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto">
      <h2 className="text-lg font-semibold">Actividad</h2>
      {error && (
        <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-[var(--danger)]">
          {error}
        </p>
      )}
      <section className="rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
        <h3 className="mb-3 text-sm font-medium">Tareas</h3>
        {loading ? (
          <p className="text-sm text-[var(--muted)]">Cargando…</p>
        ) : tasks.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">Todavía no hay tareas.</p>
        ) : (
          <ul className="space-y-3">
            {tasks.map((task) => (
              <li key={task.id} className="border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0">
                <p className="text-sm">
                  {task.kind}
                  {" · "}
                  {stateLabel(task.state)}
                  {task.failure_reason === "interrupted" ? " · Cortada por un reinicio" : ""}
                </p>
                <p className="mt-1 text-xs text-[var(--muted)]">
                  {`USD ${task.cost_usd.toFixed(6)}`}
                  {" · "}
                  {when(task.created_at)}
                </p>
                {task.partial && <p className="mt-1 text-sm">{task.partial}</p>}
                {cancellable.has(task.state) && (
                  <button
                    type="button"
                    className="mt-2 rounded-lg border border-[var(--line)] px-3 py-1 text-xs"
                    disabled={busyId === task.id}
                    onClick={() => void cancel(task.id)}
                  >
                    Cancelar
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
      <section className="rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
        <h3 className="mb-3 text-sm font-medium">Auditoría de tools</h3>
        {loading ? (
          <p className="text-sm text-[var(--muted)]">Cargando…</p>
        ) : tools.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">Todavía no hay tools en la auditoría.</p>
        ) : (
          <ul className="space-y-3">
            {tools.map((tool, index) => (
              <li
                key={`${tool.at}-${tool.name}-${index}`}
                className="border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
              >
                <p className="text-sm">
                  {tool.name}
                  {" · "}
                  {tool.extension}
                  {" · "}
                  {tool.outcome}
                </p>
                <p className="mt-1 text-xs text-[var(--muted)]">{when(tool.at)}</p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
