import { useAppStore } from "../store/appStore";

export function PhoneShellBar({ overlay }: { overlay: boolean }) {
  const presentationMode = useAppStore((s) => s.presentationMode);
  const view = useAppStore((s) => s.view);
  const enterPhoneVera = useAppStore((s) => s.enterPhoneVera);
  const showPhoneChat = useAppStore((s) => s.showPhoneChat);
  const setView = useAppStore((s) => s.setView);
  const veraActive = presentationMode === "stage";
  const reminders = view === "reminders";
  const activity = view === "activity";
  const briefing = view === "briefing";
  const diary = view === "diary";
  const chatActive = !veraActive && !reminders && !activity && !briefing && !diary;

  return (
    <nav
      className={
        overlay
          ? "absolute inset-x-0 top-0 z-50 flex flex-wrap gap-2 p-3"
          : "flex flex-wrap gap-2"
      }
      aria-label="Cáscara del teléfono"
    >
      <button
        type="button"
        aria-current={veraActive ? "page" : undefined}
        onClick={enterPhoneVera}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          veraActive
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Vera
      </button>
      <button
        type="button"
        aria-current={chatActive ? "page" : undefined}
        onClick={showPhoneChat}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          chatActive
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Chat
      </button>
      <button
        type="button"
        aria-current={reminders ? "page" : undefined}
        onClick={() => setView("reminders")}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          reminders
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Recordatorios
      </button>
      <button
        type="button"
        aria-current={activity ? "page" : undefined}
        onClick={() => setView("activity")}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          activity
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Actividad
      </button>
      <button
        type="button"
        aria-current={briefing ? "page" : undefined}
        onClick={() => setView("briefing")}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          briefing
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Briefing
      </button>
      <button
        type="button"
        aria-current={diary ? "page" : undefined}
        onClick={() => setView("diary")}
        className={`min-h-11 rounded-xl px-4 text-sm ${
          diary
            ? "bg-[var(--ink)] text-white"
            : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
        }`}
      >
        Diario
      </button>
    </nav>
  );
}
