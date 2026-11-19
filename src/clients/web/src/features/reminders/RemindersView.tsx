import { useEffect, useState } from "react";
import {
  ApiError,
  cancelReminder,
  listNotifications,
  listReminders,
  type NotificationLogItemDto,
  type ReminderItemDto,
} from "../../api/rest";
import { useNotifications } from "../notifications/useNotifications";
import { ActiveList } from "./ActiveList";
import { MissedList } from "./MissedList";
import { NotificationsLog } from "./NotificationsLog";

type ReminderTab = "active" | "missed" | "notices";

const tabs: { id: ReminderTab; label: string }[] = [
  { id: "active", label: "Activos" },
  { id: "missed", label: "Perdidos" },
  { id: "notices", label: "Avisos" },
];

export function RemindersView() {
  const [tab, setTab] = useState<ReminderTab>("active");
  const [active, setActive] = useState<ReminderItemDto[]>([]);
  const [missed, setMissed] = useState<ReminderItemDto[]>([]);
  const [notices, setNotices] = useState<NotificationLogItemDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const incoming = useNotifications();
  const incomingId = incoming[0]?.notificationId ?? "";

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [activePage, missedPage, log] = await Promise.all([
          listReminders("active"),
          listReminders("missed"),
          listNotifications(50),
        ]);
        if (cancelled) {
          return;
        }

        setActive(activePage.items);
        setMissed(missedPage.items);
        setNotices(log.items);
        setError(null);
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
  }, [incomingId]);

  async function cancel(item: ReminderItemDto) {
    if (!window.confirm("¿Cancelar este recordatorio?")) {
      return;
    }

    setBusyId(item.id);
    setError(null);
    try {
      await cancelReminder(item.id);
      setActive((current) => current.filter((row) => row.id !== item.id));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto">
      <h2 className="text-lg font-semibold">Recordatorios</h2>
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="Recordatorios">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            onClick={() => setTab(item.id)}
            className={`min-h-11 rounded-xl px-4 text-sm ${
              tab === item.id
                ? "bg-[var(--ink)] text-white"
                : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
            }`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {error && (
        <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-[var(--danger)]">
          {error}
        </p>
      )}

      <section className="rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
        {loading ? (
          <p className="text-sm text-[var(--muted)]">Cargando…</p>
        ) : tab === "active" ? (
          <ActiveList items={active} busyId={busyId} onCancel={(item) => void cancel(item)} />
        ) : tab === "missed" ? (
          <MissedList items={missed} />
        ) : (
          <NotificationsLog items={notices} />
        )}
      </section>
    </div>
  );
}

function messageOf(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 404) {
      return "Ese recordatorio no está.";
    }

    if (err.code === "not_active") {
      return "Ese recordatorio ya no está activo.";
    }

    return "No se pudo completar.";
  }

  return "No se pudo completar.";
}
