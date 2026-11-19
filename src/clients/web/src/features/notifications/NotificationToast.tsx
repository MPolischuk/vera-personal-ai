import { createEnvelope, MessageTypes } from "../../api/protocol";
import { sendEnvelope } from "../../api/hub";
import { dismissNotification, reminderActionButtons, useNotifications } from "./useNotifications";

export function NotificationToast() {
  const items = useNotifications();
  if (items.length === 0) {
    return null;
  }

  return (
    <div className="pointer-events-none fixed right-4 bottom-4 z-50 flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">
      {items.map((item) => (
        <article
          key={item.notificationId}
          role="status"
          className="pointer-events-auto rounded-xl border border-[var(--line)] bg-[var(--panel)] px-4 py-3 shadow-lg"
        >
          <div className="flex items-start justify-between gap-3">
            <h2 className="text-sm font-semibold">{item.title}</h2>
            <button
              type="button"
              className="shrink-0 text-sm text-[var(--muted)] underline"
              onClick={() => dismissNotification(item.notificationId)}
            >
              Cerrar
            </button>
          </div>
          {item.body && <p className="mt-1 text-sm text-[var(--ink)]">{item.body}</p>}
          {reminderActionButtons(item.actions).length > 0 && (
            <div className="mt-3 flex gap-2">
              {reminderActionButtons(item.actions).map((action) => (
                <button
                  key={action.actionId}
                  type="button"
                  className="rounded-lg border border-[var(--line)] px-3 py-1 text-sm"
                  onClick={() => {
                    void sendEnvelope(
                      createEnvelope(MessageTypes.NotificationAction, {
                        notificationId: item.notificationId,
                        action: action.actionId,
                      }),
                    ).then(() => dismissNotification(item.notificationId));
                  }}
                >
                  {action.label}
                </button>
              ))}
            </div>
          )}
        </article>
      ))}
    </div>
  );
}
