import { usePushSubscription } from "./usePushSubscription";
import { readStandalone, shouldShowIosInstallInstructions } from "./platform";

function familyLabel(family: string | null): string {
  switch (family) {
    case "chrome-desktop":
      return "Chrome en la computadora";
    case "edge-desktop":
      return "Edge en la computadora";
    case "firefox-desktop":
      return "Firefox en la computadora";
    case "safari-desktop":
      return "Safari en la computadora";
    case "chrome-android":
      return "Chrome en Android";
    case "edge-android":
      return "Edge en Android";
    case "firefox-android":
      return "Firefox en Android";
    case "ios-safari":
      return "Safari en el iPhone o iPad";
    default:
      return "Otro navegador";
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case "active":
      return "activa";
    case "disabled":
      return "desactivada";
    case "revoked":
      return "revocada";
    default:
      return status;
  }
}

function formatWhen(value: string | null): string {
  if (!value) {
    return "sin fecha";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "sin fecha";
  }

  return date.toLocaleString("es-AR", { dateStyle: "medium", timeStyle: "short" });
}

export function NotificationsSettings() {
  const push = usePushSubscription(true);
  const showInstallInstructions = shouldShowIosInstallInstructions(
    navigator.userAgent,
    readStandalone(navigator as Navigator & { standalone?: boolean }),
  );

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Notificaciones</h2>
        <p className="mt-1 text-sm text-[var(--muted)]">
          Los avisos llegan aunque la app esté cerrada.
        </p>
      </div>

      {push.error && <p className="text-sm text-[var(--danger)]">{push.error}</p>}
      {push.notice && <p className="text-sm text-[var(--accent)]">{push.notice}</p>}

      {showInstallInstructions ? (
        <p className="text-sm">
          En el iPhone o iPad los avisos sólo llegan si la app está instalada. Tocá Compartir y
          después Agregar a inicio. Abrila desde el ícono y recién ahí vas a poder activar las
          notificaciones.
        </p>
      ) : (
        <PushControls push={push} />
      )}

      <section className="space-y-2">
        <h3 className="text-sm font-semibold">Dispositivos</h3>
        {push.devices.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">Todavía no hay dispositivos suscritos.</p>
        ) : (
          <ul className="space-y-2 text-sm">
            {push.devices.map((device) => (
              <li key={device.id} className="rounded-xl border border-[var(--line)] px-3 py-2">
                <p className="font-medium">{familyLabel(device.userAgentFamily)}</p>
                <p className="text-[var(--muted)]">
                  {statusLabel(device.status)} · desde {formatWhen(device.createdAt)}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function PushControls({
  push,
}: {
  push: ReturnType<typeof usePushSubscription>;
}) {
  if (push.permission === "unsupported") {
    return <p className="text-sm">Este navegador no puede recibir avisos.</p>;
  }

  if (push.permission === "denied") {
    return (
      <p className="text-sm">
        El navegador bloqueó los avisos. Para volver a permitirlos, entrá a la configuración del
        sitio y cambialos ahí. Desde acá no se puede.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-[var(--muted)]">
        {push.permission === "granted"
          ? push.subscribed
            ? "Este navegador puede mostrar avisos."
            : "El permiso está concedido, pero este dispositivo todavía no está suscrito."
          : "Todavía no pedimos permiso."}
      </p>
      <div className="flex flex-wrap gap-2">
        {push.permission !== "granted" && (
          <button
            type="button"
            className="min-h-11 rounded-xl bg-[var(--accent)] px-4 text-sm text-white disabled:opacity-60"
            disabled={push.busy}
            onClick={() => void push.activate()}
          >
            Activar notificaciones
          </button>
        )}
        {push.permission === "granted" && (
          <>
            {!push.subscribed && (
              <button
                type="button"
                className="min-h-11 rounded-xl bg-[var(--accent)] px-4 text-sm text-white disabled:opacity-60"
                disabled={push.busy}
                onClick={() => void push.activate()}
              >
                Activar notificaciones
              </button>
            )}
            <button
              type="button"
              className="min-h-11 rounded-xl border border-[var(--line)] bg-white px-4 text-sm disabled:opacity-60"
              disabled={push.busy || !push.subscribed}
              onClick={() => void push.sendTest()}
            >
              Enviar prueba
            </button>
            <button
              type="button"
              className="min-h-11 rounded-xl border border-[var(--line)] bg-white px-4 text-sm disabled:opacity-60"
              disabled={push.busy || !push.subscribed}
              onClick={() => void push.deactivate()}
            >
              Desactivar en este dispositivo
            </button>
          </>
        )}
      </div>
    </div>
  );
}
