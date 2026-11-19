/** Readable delivery state. The clock stays in the server string. */
export function deliveryStatusLabel(status: string, channel: string | null): string {
  const settled = status === "delivered" || status === "acted";
  if (settled && channel === "telegram") {
    return "Entregado por Telegram";
  }

  if (settled && channel === "webpush") {
    return "Push aceptado";
  }

  if (settled && channel === "hub") {
    return "Visto en la app";
  }

  if (status === "failed") {
    return "No llegó por ningún canal";
  }

  if (status === "cancelled") {
    return "Cancelado";
  }

  return "Todavía en camino";
}

export function actionLabel(action: string | null): string {
  if (!action) {
    return "Sin acción";
  }

  if (action === "done") {
    return "Listo";
  }

  if (action === "snooze:10m") {
    return "+10 min";
  }

  return action;
}

export function recurrenceLabel(recurrence: string | null): string {
  const text = recurrence?.trim() ?? "";
  return text.length > 0 ? text : "Una sola vez";
}

export function channelLabel(channel: string | null): string {
  switch (channel) {
    case "telegram":
      return "Telegram";
    case "webpush":
      return "Push";
    case "hub":
      return "La app";
    default:
      return channel && channel.length > 0 ? channel : "Ninguno";
  }
}

const localDate = /^(\d{4})-(\d{2})-(\d{2})/;
const localClock = /T(\d{2}:\d{2})/;
const weekdays = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

/** Calendar date already written in the offset string. */
export function localDayHeading(localAt: string): string {
  const match = localDate.exec(localAt);
  if (!match) {
    return localAt;
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const weekday = weekdays[new Date(Date.UTC(year, month - 1, day)).getUTCDay()] ?? "día";
  return `${weekday} ${day}/${month}`;
}

/** `HH:mm` copied from the offset string. */
export function localClockLabel(localAt: string): string {
  return localClock.exec(localAt)?.[1] ?? localAt;
}

export function groupByLocalDay<T extends { localAt: string }>(
  items: T[],
): { heading: string; items: T[] }[] {
  const groups: { heading: string; items: T[] }[] = [];
  for (const item of items) {
    const heading = localDayHeading(item.localAt);
    const last = groups[groups.length - 1];
    if (last && last.heading === heading) {
      last.items.push(item);
    } else {
      groups.push({ heading, items: [item] });
    }
  }

  return groups;
}
