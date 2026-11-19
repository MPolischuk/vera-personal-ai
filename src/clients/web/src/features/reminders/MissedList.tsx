import type { ReminderItemDto } from "../../api/rest";
import { channelLabel, localClockLabel, recurrenceLabel } from "./format";

export function MissedList({ items }: { items: ReminderItemDto[] }) {
  if (items.length === 0) {
    return (
      <p className="text-sm text-[var(--muted)]">No hay perdidos en los últimos 30 días.</p>
    );
  }

  return (
    <ul className="space-y-3">
      {items.map((item) => (
        <li key={item.id} className="border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0">
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
        </li>
      ))}
    </ul>
  );
}
