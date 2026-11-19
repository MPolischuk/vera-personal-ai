import { useEffect, useState, useSyncExternalStore } from "react";
import { disconnectHub } from "./api/hub";
import { hasBearerToken } from "./api/rest";
import { hasSession, refresh, subscribeSession } from "./api/session";
import { ChatView } from "./features/chat/ChatView";
import { parseNotificationDeepLink, parseServiceWorkerNotification } from "./features/notifications/deepLink";
import { NotificationToast } from "./features/notifications/NotificationToast";
import { usePushSubscription } from "./features/notifications/usePushSubscription";
import { LoginView } from "./features/auth/LoginView";
import { MemoryView } from "./features/memory/MemoryView";
import { ActivityView } from "./features/activity/ActivityView";
import { RemindersView } from "./features/reminders/RemindersView";
import { BriefingView } from "./features/briefing/BriefingView";
import { DiaryView } from "./features/diary/DiaryView";
import { ExtensionsView } from "./features/extensions/ExtensionsView";
import { SettingsView } from "./features/settings/SettingsView";
import { UsageView } from "./features/usage/UsageView";
import { PhoneShellBar } from "./shell/PhoneShellBar";
import { usePhoneShell } from "./shell/usePhoneShell";
import { useAppStore, type AppView } from "./store/appStore";
import { isViewportMode } from "./store/presentation";

const navItems: { id: AppView; label: string }[] = [
  { id: "chat", label: "Chat" },
  { id: "memory", label: "Memoria" },
  { id: "reminders", label: "Recordatorios" },
  { id: "activity", label: "Actividad" },
  { id: "briefing", label: "Briefing" },
  { id: "diary", label: "Diario" },
  { id: "extensions", label: "Extensiones" },
  { id: "settings", label: "Personalidad" },
  { id: "usage", label: "Uso" },
];

function viewFromSearch(search: string): "briefing" | "extensions" | null {
  const query = search.startsWith("?") ? search.slice(1) : search;
  const view = new URLSearchParams(query).get("view");
  if (view === "briefing" || view === "extensions") {
    return view;
  }
  return null;
}

function connectionLabel(status: string): string {
  switch (status) {
    case "connected":
      return "Conectado";
    case "connecting":
      return "Conectando…";
    case "error":
      return "Error de conexión";
    default:
      return "Sin conectar";
  }
}

export default function App() {
  const baked = hasBearerToken();
  const signedIn = useSyncExternalStore(subscribeSession, hasSession, hasSession);
  const [refreshSettled, setRefreshSettled] = useState(baked);
  const view = useAppStore((s) => s.view);
  const phoneShell = useAppStore((s) => s.phoneShell);
  const presentationMode = useAppStore((s) => s.presentationMode);
  const setView = useAppStore((s) => s.setView);
  const selectConversation = useAppStore((s) => s.selectConversation);
  const connect = useAppStore((s) => s.connect);
  const loadPersonality = useAppStore((s) => s.loadPersonality);
  const connectionStatus = useAppStore((s) => s.connectionStatus);
  const connectionError = useAppStore((s) => s.connectionError);
  const clientError = useAppStore((s) => s.clientError);
  const clearClientError = useAppStore((s) => s.clearClientError);

  usePhoneShell();
  usePushSubscription(signedIn);

  useEffect(() => {
    if (baked) {
      return;
    }
    let active = true;
    void refresh().finally(() => {
      if (active) {
        setRefreshSettled(true);
      }
    });
    return () => {
      active = false;
    };
  }, [baked]);

  const stage = view === "chat" && isViewportMode(presentationMode);
  const hasError = Boolean(connectionError || clientError);

  useEffect(() => {
    if (!signedIn) {
      void disconnectHub();
      return;
    }
    void connect();
  }, [connect, signedIn]);

  useEffect(() => {
    if (!signedIn) {
      return;
    }
    void loadPersonality().catch(() => {
      /* Avatar waits; chat remains usable without personality. */
    });
  }, [loadPersonality, signedIn]);

  useEffect(() => {
    if (!signedIn) {
      return;
    }

    const requested = viewFromSearch(window.location.search);
    if (requested) {
      setView(requested);
    }
    const link = parseNotificationDeepLink(window.location.search);
    if (link?.action === "briefing") {
      setView("briefing");
    }
    if (link?.conversationId) {
      void selectConversation(link.conversationId);
    }
  }, [selectConversation, setView, signedIn]);

  useEffect(() => {
    if (!signedIn || !("serviceWorker" in navigator)) {
      return;
    }

    const onMessage = (event: MessageEvent) => {
      const link = parseServiceWorkerNotification(event.data);
      if (link?.action === "briefing") {
        setView("briefing");
      }
      if (link?.conversationId) {
        void selectConversation(link.conversationId);
      }
    };

    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, [selectConversation, setView, signedIn]);

  if (!baked && (!refreshSettled || !signedIn)) {
    if (!refreshSettled) {
      return null;
    }
    return (
      <div className="flex h-full items-center justify-center p-6">
        <LoginView />
      </div>
    );
  }

  return (
    <div
      className={
        stage
          ? "relative flex h-full flex-col overflow-hidden"
          : "mx-auto flex h-full max-w-6xl flex-col gap-3 p-3 md:p-5"
      }
    >
      {/* Stable slot 0: app chrome — hidden in stage so ChatView/main never remount (D4). */}
      {phoneShell ? (
        <PhoneShellBar overlay={stage} />
      ) : (
        <div className={stage ? "hidden" : "contents"} aria-hidden={stage}>
          <header className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-[var(--line)] bg-[var(--panel)] px-4 py-3 shadow-sm">
            <div>
              <p className="text-xs tracking-[0.14em] text-[var(--muted)] uppercase">
                Asistente personal
              </p>
              <h1 className="text-lg font-semibold">Cliente web mínimo</h1>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <span
                className={`rounded-full px-3 py-1 text-sm ${
                  connectionStatus === "connected"
                    ? "bg-[var(--accent-soft)] text-[var(--accent)]"
                    : connectionStatus === "error"
                      ? "bg-red-50 text-[var(--danger)]"
                      : "bg-slate-100 text-[var(--muted)]"
                }`}
              >
                {connectionLabel(connectionStatus)}
              </span>
              {connectionStatus !== "connected" && connectionStatus !== "connecting" && (
                <button
                  type="button"
                  className="rounded-lg bg-[var(--accent)] px-3 py-1.5 text-sm text-white"
                  onClick={() => void connect()}
                >
                  Reconectar
                </button>
              )}
            </div>
          </header>

          <nav className="flex flex-wrap gap-2">
            {navItems.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => setView(item.id)}
                className={`rounded-xl px-4 py-2 text-sm ${
                  view === item.id
                    ? "bg-[var(--ink)] text-white"
                    : "border border-[var(--line)] bg-[var(--panel)] text-[var(--ink)]"
                }`}
              >
                {item.label}
              </button>
            ))}
          </nav>
        </div>
      )}

      {/* Stable slot 1: errors */}
      <div
        className={
          !hasError
            ? "hidden"
            : stage
              ? "pointer-events-auto absolute top-3 right-3 left-3 z-50 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-[var(--danger)] shadow-sm"
              : "rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-[var(--danger)]"
        }
        aria-hidden={!hasError}
      >
        {connectionError && <p>{connectionError}</p>}
        {clientError && (
          <div className="flex items-start justify-between gap-3">
            <p>
              <span className="font-medium">{clientError.code}</span>
              {" — "}
              {clientError.detail}
            </p>
            <button
              type="button"
              className="shrink-0 underline"
              onClick={clearClientError}
            >
              Cerrar
            </button>
          </div>
        )}
      </div>

      {/* Stable slot 2: main */}
      <main className="flex min-h-0 flex-1 flex-col overflow-hidden">
        {view === "chat" && (
          <ChatView
            showAvatar={!(phoneShell && presentationMode === "chat")}
            phoneShell={phoneShell}
          />
        )}
        {view === "memory" && <MemoryView />}
        {view === "reminders" && <RemindersView />}
        {view === "activity" && <ActivityView />}
        {view === "briefing" && <BriefingView />}
        {view === "diary" && <DiaryView />}
        {view === "extensions" && <ExtensionsView />}
        {view === "settings" && <SettingsView />}
        {view === "usage" && <UsageView />}
      </main>
      <NotificationToast />
    </div>
  );
}
