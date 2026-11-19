import { MicButton } from "../voice/MicButton";
import {
  canZoomIn,
  canZoomOut,
  type CameraShot,
} from "../avatar/cameraShot";

export interface StageChromeProps {
  stageChatVisible: boolean;
  showInterrupt: boolean;
  canHideChat: boolean;
  cameraShot: CameraShot;
  onToggleChat: () => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onExit: () => void;
  onInterrupt: () => void;
  phoneShell?: boolean;
}

/**
 * Floating controls for stage mode. Always visible so voice turns work
 * with the chat overlay hidden (D2).
 */
export function StageChrome({
  stageChatVisible,
  showInterrupt,
  canHideChat,
  cameraShot,
  onToggleChat,
  onZoomIn,
  onZoomOut,
  onExit,
  onInterrupt,
  phoneShell = false,
}: StageChromeProps) {
  return (
    <div className="pointer-events-none absolute inset-x-0 bottom-0 z-40 flex items-end justify-between gap-3 p-3 md:p-4">
      <div className="pointer-events-auto flex flex-wrap items-center gap-2">
        {!stageChatVisible && <MicButton />}
        {showInterrupt && (
          <button
            type="button"
            className="rounded-lg border border-[var(--danger)] bg-[var(--panel)]/90 px-3 py-1.5 text-xs text-[var(--danger)] backdrop-blur-sm"
            onClick={onInterrupt}
          >
            Interrumpir
          </button>
        )}
      </div>
      <div className="pointer-events-auto flex flex-wrap items-center justify-end gap-2">
        <div className="flex overflow-hidden rounded-lg border border-[var(--line)] bg-[var(--panel)]/90 text-[var(--ink)] backdrop-blur-sm">
          <button
            type="button"
            className="px-2.5 py-1.5 text-xs disabled:opacity-40"
            aria-label="Alejar cámara"
            disabled={!canZoomOut(cameraShot)}
            onClick={onZoomOut}
          >
            −
          </button>
          <span className="min-w-16 border-x border-[var(--line)] px-2 py-1.5 text-center text-xs">
            Zoom
          </span>
          <button
            type="button"
            className="px-2.5 py-1.5 text-xs disabled:opacity-40"
            aria-label="Acercar cámara"
            disabled={!canZoomIn(cameraShot)}
            onClick={onZoomIn}
          >
            +
          </button>
        </div>
        {!phoneShell && (
          <button
            type="button"
            className="rounded-lg border border-[var(--line)] bg-[var(--panel)]/90 px-3 py-1.5 text-xs text-[var(--ink)] backdrop-blur-sm disabled:opacity-50"
            disabled={!canHideChat && stageChatVisible}
            onClick={onToggleChat}
          >
            {stageChatVisible ? "Ocultar chat" : "Mostrar chat"}
          </button>
        )}
        {!phoneShell && (
          <button
            type="button"
            className="rounded-lg bg-[var(--ink)] px-3 py-1.5 text-xs text-white"
            onClick={onExit}
          >
            Salir
          </button>
        )}
      </div>
    </div>
  );
}
