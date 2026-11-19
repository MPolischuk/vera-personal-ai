import type { ReminderItemDto } from "../../api/rest";
import { channelLabel, localClockLabel, recurrenceLabel } from "./format";

export function ActiveList({
  items,
  busyId,
  onCancel,
}: {
  items: ReminderItemDto[];
  busyId: string | null;
  onCancel: (item: ReminderItemDto) => void;
}) {
  if (items.length === 0) {
    return <p className="text-sm text-[var(--muted)]">No hay recordatorios activos.</p>;
  }

  return (
    <ul className="space-y-3">
      {items.map((item) => (
        <li
          key={item.id}
          className="flex flex-col gap-3 border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0 sm:flex-row sm:items-start sm:justify-between"
        >
          <div className="min-w-0">
            <p className="text-sm">{item.text}</p>
            <p className="mt-1 text-xs text-[var(--muted)]">
              {item.relative}
              {" · "}
              {localClockLabel(item.local_due)}
              {" · "}
              {recurrenceLabel(item.recurrence)}
              {" · "}
              {channelLabel(item.channel)}
            </p>
          </div>
          <button
            type="button"
            className="min-h-11 rounded-lg border border-[var(--danger)] px-4 text-sm text-[var(--danger)] disabled:opacity-50"
            disabled={busyId === item.id}
            onClick={() => onCancel(item)}
          >
            Cancelar
          </button>
        </li>
      ))}
    </ul>
  );
}
