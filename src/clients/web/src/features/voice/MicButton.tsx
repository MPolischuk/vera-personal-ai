import { useState } from "react";
import { useAppStore } from "../../store/appStore";
import { toggleRecording } from "./voiceController";

function statusLabel(status: string, recording: boolean): string {
  if (recording) {
    return "Grabando";
  }
  switch (status) {
    case "awaiting_turn":
      return "Esperando respuesta";
    case "playing":
      return "Reproduciendo";
    default:
      return "Listo para grabar";
  }
}

export function MicButton() {
  const voiceStatus = useAppStore((s) => s.voiceStatus);
  const connectionStatus = useAppStore((s) => s.connectionStatus);
  const confirmationPending = useAppStore((s) => s.pendingConfirmation);
  const setClientError = useAppStore((s) => s.clearClientError);
  const [busy, setBusy] = useState(false);

  const recording = voiceStatus === "capturing";
  const waitingForReply = voiceStatus === "awaiting_turn";
  const disabled =
    connectionStatus !== "connected" ||
    busy ||
    confirmationPending !== null ||
    waitingForReply;

  async function onToggle() {
    if (disabled) {
      return;
    }

    if (
      typeof window !== "undefined" &&
      !window.isSecureContext &&
      !recording
    ) {
      useAppStore.setState({
        clientError: {
          code: "insecure_context",
          detail:
            "El micrófono requiere un contexto seguro: usá http://localhost o HTTPS.",
        },
      });
      return;
    }

    setBusy(true);
    setClientError();
    try {
      await toggleRecording();
    } catch (error) {
      useAppStore.setState({
        clientError: {
          code: "mic_failed",
          detail:
            error instanceof Error
              ? error.message
              : "No se pudo usar el micrófono.",
        },
        micEnabled: false,
        voiceStatus: "off",
      });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        disabled={disabled}
        onClick={() => void onToggle()}
        aria-pressed={recording}
        className={`rounded-xl px-3 py-2 text-sm ${
          recording
            ? "bg-[var(--accent)] text-white"
            : "border border-[var(--line)] bg-white text-[var(--ink)]"
        }`}
      >
        {recording ? "Enviar" : "Grabar"}
      </button>
      <span className="text-xs text-[var(--muted)]">
        {statusLabel(voiceStatus, recording)}
      </span>
    </div>
  );
}
