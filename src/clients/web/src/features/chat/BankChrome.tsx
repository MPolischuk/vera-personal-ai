import type { AvatarExpressionName } from "../avatar/applyExpression";
import { canZoomIn, canZoomOut, type CameraShot } from "../avatar/cameraShot";
import {
  BANK_EXPRESSION_IDS,
  BANK_MOTIONS,
  bankExpressionLabel,
  type BankMotionItem,
} from "./expressionBank";

export interface BankChromeProps {
  expressionId: string | null;
  motionId: string | null;
  cameraShot: CameraShot;
  onExpression: (name: AvatarExpressionName) => void;
  onMotion: (item: BankMotionItem) => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onExit: () => void;
}

function itemClass(selected: boolean): string {
  return selected
    ? "rounded-lg bg-[var(--ink)] px-2.5 py-1.5 text-left text-xs text-white"
    : "rounded-lg border border-[var(--line)] bg-white px-2.5 py-1.5 text-left text-xs text-[var(--ink)] hover:bg-slate-50";
}

/**
 * Side catalog for the expression bank. The lists scroll; zoom stays put.
 */
export function BankChrome({
  expressionId,
  motionId,
  cameraShot,
  onExpression,
  onMotion,
  onZoomIn,
  onZoomOut,
  onExit,
}: BankChromeProps) {
  return (
    <aside className="pointer-events-auto absolute bottom-3 left-3 top-3 z-40 flex w-60 flex-col rounded-2xl border border-[var(--line)] bg-[var(--panel)]/95 shadow-lg backdrop-blur-sm">
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-3">
        <div>
          <p className="mb-1.5 text-[0.7rem] tracking-wide text-[var(--muted)] uppercase">
            Expresión
          </p>
          <div className="flex flex-col gap-1.5">
            {BANK_EXPRESSION_IDS.map((id) => (
              <button
                key={id}
                type="button"
                className={itemClass(expressionId === id)}
                onClick={() => onExpression(id)}
              >
                {bankExpressionLabel(id)}
              </button>
            ))}
          </div>
        </div>
        <div>
          <p className="mb-1.5 text-[0.7rem] tracking-wide text-[var(--muted)] uppercase">
            Animación
          </p>
          <div className="flex flex-col gap-1.5">
            {BANK_MOTIONS.map((item) => (
              <button
                key={item.id}
                type="button"
                className={itemClass(motionId === item.id)}
                onClick={() => onMotion(item)}
              >
                {item.label}
              </button>
            ))}
          </div>
        </div>
      </div>
      <div className="flex items-center justify-between gap-2 border-t border-[var(--line)] p-3">
        <div className="flex overflow-hidden rounded-lg border border-[var(--line)] bg-white text-[var(--ink)]">
          <button
            type="button"
            className="px-2.5 py-1.5 text-xs disabled:opacity-40"
            aria-label="Alejar cámara"
            disabled={!canZoomOut(cameraShot)}
            onClick={onZoomOut}
          >
            −
          </button>
          <span className="min-w-14 border-x border-[var(--line)] px-2 py-1.5 text-center text-xs">
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
        <button
          type="button"
          className="rounded-lg bg-[var(--ink)] px-3 py-1.5 text-xs text-white"
          onClick={onExit}
        >
          Salir
        </button>
      </div>
    </aside>
  );
}
