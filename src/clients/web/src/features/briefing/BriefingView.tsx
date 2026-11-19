import { useEffect, useState } from "react";
import {
  ApiError,
  getTodayBriefing,
  setBriefingFeedback,
  type BriefingDto,
  type BriefingFeedbackValue,
  type BriefingItemDto,
} from "../../api/rest";

const feedbackActions: { value: BriefingFeedbackValue; label: string }[] = [
  { value: "done", label: "Hecho" },
  { value: "dismissed", label: "Descartar" },
  { value: "helpful", label: "Útil" },
  { value: "not_helpful", label: "No útil" },
];

function triageLabel(triage: string): string {
  if (triage === "needs_you") {
    return "Te necesita";
  }
  if (triage === "can_wait") {
    return "Puede esperar";
  }
  return triage;
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

function ItemList({
  items,
  busyId,
  onFeedback,
}: {
  items: BriefingItemDto[];
  busyId: string | null;
  onFeedback: (itemId: string, feedback: BriefingFeedbackValue) => void;
}) {
  if (items.length === 0) {
    return <p className="text-sm text-zinc-600">Nada.</p>;
  }

  return (
    <ul className="space-y-3">
      {items.map((item) => (
        <li key={item.id} className="space-y-2 rounded-xl border border-[var(--line)] bg-[var(--panel)] p-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h4 className="font-medium">{item.title}</h4>
            <span className="text-xs tracking-wide text-[var(--muted)] uppercase">
              {triageLabel(item.triage)}
            </span>
          </div>
          <p className="text-sm text-zinc-700">{item.reason}</p>
          <div className="flex flex-wrap gap-2">
            {feedbackActions.map((action) => {
              const selected = item.feedback === action.value;
              return (
                <button
                  key={action.value}
                  type="button"
                  aria-pressed={selected}
                  disabled={busyId === item.id}
                  className={`rounded-lg px-3 py-1.5 text-sm disabled:opacity-50 ${
                    selected
                      ? "bg-[var(--ink)] text-white"
                      : "border border-[var(--line)] bg-white text-[var(--ink)]"
                  }`}
                  onClick={() => onFeedback(item.id, action.value)}
                >
                  {action.label}
                </button>
              );
            })}
          </div>
        </li>
      ))}
    </ul>
  );
}

export function BriefingView() {
  const [briefing, setBriefing] = useState<BriefingDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const today = await getTodayBriefing();
        if (!cancelled) {
          setBriefing(today);
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

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  async function onFeedback(itemId: string, feedback: BriefingFeedbackValue) {
    if (!briefing) {
      return;
    }

    setBusyId(itemId);
    setError(null);
    try {
      await setBriefingFeedback(briefing.id, itemId, feedback);
      setBriefing((current) => {
        if (!current) {
          return current;
        }
        const mark = (item: BriefingItemDto) =>
          item.id === itemId ? { ...item, feedback } : item;
        return {
          ...current,
          today: current.today.map(mark),
          ahead: current.ahead.map(mark),
        };
      });
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto">
      <h2 className="text-lg font-semibold">Briefing</h2>
      {error && (
        <p className="text-sm text-red-700" role="alert">
          {error}
        </p>
      )}
      {loading && <p className="text-sm text-zinc-600">Cargando…</p>}
      {!loading && !briefing && (
        <p className="text-sm text-zinc-700">Todavía no armé el briefing de hoy.</p>
      )}
      {briefing && (
        <div className="space-y-6">
          <p className="text-sm text-zinc-800">{briefing.summary}</p>
          <section className="space-y-3">
            <h3 className="font-medium">Hoy</h3>
            <ItemList
              items={briefing.today}
              busyId={busyId}
              onFeedback={(itemId, feedback) => {
                void onFeedback(itemId, feedback);
              }}
            />
          </section>
          <section className="space-y-3">
            <h3 className="font-medium">Más adelante</h3>
            <ItemList
              items={briefing.ahead}
              busyId={busyId}
              onFeedback={(itemId, feedback) => {
                void onFeedback(itemId, feedback);
              }}
            />
          </section>
        </div>
      )}
    </div>
  );
}
