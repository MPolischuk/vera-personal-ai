import type { NotificationLogItemDto } from "../../api/rest";
import {
  actionLabel,
  channelLabel,
  deliveryStatusLabel,
  groupByLocalDay,
  localClockLabel,
} from "./format";

export function NotificationsLog({ items }: { items: NotificationLogItemDto[] }) {
  if (items.length === 0) {
    return <p className="text-sm text-[var(--muted)]">No hay avisos.</p>;
  }

  const groups = groupByLocalDay(items.map((item) => ({ ...item, localAt: item.local_at })));

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <section key={group.heading} className="space-y-3">
          <h3 className="text-sm font-medium text-[var(--muted)]">{group.heading}</h3>
          <ul className="space-y-3">
            {group.items.map((item) => (
              <li
                key={item.id}
                className="border-b border-[var(--line)] pb-3 last:border-b-0 last:pb-0"
              >
                <p className="text-sm font-medium">{item.title}</p>
                {item.body ? <p className="mt-1 text-sm">{item.body}</p> : null}
                <p className="mt-1 text-xs text-[var(--muted)]">
                  {localClockLabel(item.local_at)}
                  {" · "}
                  {channelLabel(item.channel)}
                  {" · "}
                  {deliveryStatusLabel(item.status, item.channel)}
                  {" · "}
                  {actionLabel(item.action)}
                </p>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}
