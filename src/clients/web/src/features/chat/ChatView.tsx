import { useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { messageIntentCue } from "./messageIntent";
import type {
  AvatarExpressionCue,
  AvatarExpressionName,
} from "../avatar/applyExpression";
import { useAppStore } from "../../store/appStore";
import { isViewportMode } from "../../store/presentation";
import {
  resolveAvatarExpression,
  resolveAvatarPresentation,
} from "../../store/resolveAvatarPresentation";
import { AvatarStage } from "../avatar/AvatarStage";
import { resolveAvatarModelUrl } from "../avatar/resolveAvatarUrl";
import { MicButton } from "../voice/MicButton";
import { BankChrome } from "./BankChrome";
import { ConfirmationCard } from "./ConfirmationCard";
import { bankExpressionCue, type BankMotionItem } from "./expressionBank";
import type { MotionPreview } from "../avatar/director";
import { StageChrome } from "./StageChrome";
import { useWindowBackground } from "./useWindowBackground";

const STAGE_AVATAR_CLASS =
  "relative h-full min-h-0 w-full overflow-hidden bg-[var(--ink)]";

function roleLabel(role: string): string {
  switch (role) {
    case "User":
      return "Vos";
    case "Assistant":
      return "Asistente";
    case "System":
      return "Sistema";
    case "Tool":
      return "Herramienta";
    default:
      return role;
  }
}

function conversationLabel(title: string | null, startedAt: string): string {
  if (title?.trim()) {
    return title;
  }
  return new Date(startedAt).toLocaleString("es-AR", {
    dateStyle: "short",
    timeStyle: "short",
  });
}

export function ChatView({
  showAvatar = true,
  phoneShell = false,
}: {
  showAvatar?: boolean;
  phoneShell?: boolean;
}) {
  const conversations = useAppStore((s) => s.conversations);
  const selectedId = useAppStore((s) => s.selectedConversationId);
  const messages = useAppStore((s) => s.messages);
  const streamingText = useAppStore((s) => s.streamingText);
  const partialTranscript = useAppStore((s) => s.partialTranscript);
  const turnActive = useAppStore((s) => s.turnActive);
  const voiceStatus = useAppStore((s) => s.voiceStatus);
  const avatarState = useAppStore((s) => s.avatarState);
  const avatarGesture = useAppStore((s) => s.avatarGesture);
  const activeTools = useAppStore((s) => s.activeTools);
  const clientError = useAppStore((s) => s.clientError);
  const characterExpression = useAppStore((s) => s.characterExpression);
  const activeTurnId = useAppStore((s) => s.activeTurnId);
  const lastTurnId = useAppStore((s) => s.lastTurnId);
  const pendingConfirmation = useAppStore((s) => s.pendingConfirmation);
  const loadingMessages = useAppStore((s) => s.loadingMessages);
  const sending = useAppStore((s) => s.sending);
  const connectionStatus = useAppStore((s) => s.connectionStatus);
  const presentationMode = useAppStore((s) => s.presentationMode);
  const stageChatVisible = useAppStore((s) => s.stageChatVisible);
  const stageCameraShot = useAppStore((s) => s.stageCameraShot);
  const selectConversation = useAppStore((s) => s.selectConversation);
  const createConversation = useAppStore((s) => s.createConversation);
  const deleteConversation = useAppStore((s) => s.deleteConversation);
  const deleteAllConversations = useAppStore((s) => s.deleteAllConversations);
  const sendMessage = useAppStore((s) => s.sendMessage);
  const interrupt = useAppStore((s) => s.interrupt);
  const enterStage = useAppStore((s) => s.enterStage);
  const enterBank = useAppStore((s) => s.enterBank);
  const exitStage = useAppStore((s) => s.exitStage);
  const toggleStageChat = useAppStore((s) => s.toggleStageChat);
  const stepStageCameraShot = useAppStore((s) => s.stepStageCameraShot);
  const personality = useAppStore((s) => s.personality);

  const [draft, setDraft] = useState("");
  const [bankExpression, setBankExpression] = useState<AvatarExpressionCue | null>(
    null,
  );
  const [bankPreview, setBankPreview] = useState<MotionPreview | null>(null);
  const [bankMotionId, setBankMotionId] = useState<string | null>(null);
  const { backgroundUrl, backgroundAlign } = useWindowBackground();
  const transcriptEnd = useRef<HTMLDivElement>(null);
  const previousPresentationMode = useRef(presentationMode);

  const isStage = presentationMode === "stage";
  const isBank = presentationMode === "bank";
  const isViewport = isViewportMode(presentationMode);
  const showChatPanel = phoneShell
    ? presentationMode === "chat"
    : presentationMode === "chat" || (isStage && stageChatVisible);

  const modelUrl = useMemo(() => {
    const model = personality?.profile.avatar.model;
    if (!model?.trim()) {
      return null;
    }
    return resolveAvatarModelUrl(model);
  }, [personality]);

  const visualState = useMemo(
    () =>
      resolveAvatarPresentation({
        voiceStatus,
        turnActive,
        avatarState,
      }),
    [voiceStatus, turnActive, avatarState],
  );

  const resolvedExpression = useMemo(
    () =>
      resolveAvatarExpression({
        visualState,
        characterHold: characterExpression,
        currentTurnId: activeTurnId ?? lastTurnId,
      }),
    [visualState, characterExpression, activeTurnId, lastTurnId],
  );

  const composerLocked = useMemo(
    () =>
      Boolean(pendingConfirmation) ||
      turnActive ||
      sending ||
      voiceStatus === "capturing" ||
      connectionStatus !== "connected",
    [pendingConfirmation, turnActive, sending, voiceStatus, connectionStatus],
  );

  const showInterrupt = turnActive || voiceStatus === "playing";
  const canMutateList = connectionStatus === "connected" && !sending;
  const canHideChat = !pendingConfirmation;

  useEffect(() => {
    transcriptEnd.current?.scrollIntoView({ block: "end" });
  }, [messages, streamingText, partialTranscript, pendingConfirmation]);

  useEffect(() => {
    const previous = previousPresentationMode.current;
    previousPresentationMode.current = presentationMode;
    if (presentationMode === "bank" && previous !== "bank") {
      setBankExpression(resolvedExpression);
      setBankPreview(null);
      setBankMotionId(null);
      return;
    }
    if (presentationMode !== "bank" && previous === "bank") {
      setBankExpression(null);
      setBankPreview(null);
      setBankMotionId(null);
    }
  }, [presentationMode, resolvedExpression]);

  useEffect(() => {
    if (!isViewport) {
      return;
    }
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") {
        exitStage();
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isViewport, exitStage]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const text = draft;
    setDraft("");
    await sendMessage(text);
  }

  function onComposerKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      if (!composerLocked && draft.trim()) {
        void sendMessage(draft).then(() => setDraft(""));
      }
    }
  }

  function onDeleteConversation(id: string, label: string) {
    if (!window.confirm(`¿Eliminar “${label}”?`)) {
      return;
    }
    void deleteConversation(id);
  }

  function onDeleteAll() {
    if (
      !window.confirm(
        "¿Eliminar todas las conversaciones? Esta acción no se puede deshacer.",
      )
    ) {
      return;
    }
    void deleteAllConversations();
  }

  function onBankExpression(name: AvatarExpressionName) {
    setBankExpression(bankExpressionCue(name));
  }

  function onBankMotion(item: BankMotionItem) {
    setBankMotionId(item.id);
    setBankPreview({ name: item.id, playId: crypto.randomUUID() });
  }

  // Intent cue for the director (client heuristic, see messageIntent.ts). While
  // the reply streams the cue is keyed by turn index + kind so a list detected
  // mid-stream reaches the speech gesture before the voice ends; the final
  // message reuses the same key so it does not replay.
  const lastMessage = messages[messages.length - 1];
  const intentCue = useMemo(() => {
    if (streamingText) {
      const cue = messageIntentCue({ id: "stream", role: "Assistant", content: streamingText });
      return cue && { ...cue, playId: `a:${messages.length}:${cue.kind}:${cue.closing ? 1 : 0}` };
    }
    const cue = messageIntentCue(lastMessage);
    if (cue && cue.source === "assistant") {
      return { ...cue, playId: `a:${messages.length - 1}:${cue.kind}:${cue.closing ? 1 : 0}` };
    }
    return cue;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lastMessage?.id, lastMessage?.role, streamingText, messages.length]);

  const avatarStage = showAvatar ? (
    <AvatarStage
      modelUrl={modelUrl}
      speaking={voiceStatus === "playing"}
      visualState={isBank ? "idle" : visualState}
      expression={isBank ? bankExpression : resolvedExpression}
      gesture={isBank ? null : avatarGesture}
      backgroundUrl={backgroundUrl}
      backgroundAlign={backgroundAlign}
      shot={isViewport ? stageCameraShot : "bust"}
      turnError={isBank ? false : Boolean(clientError)}
      activeTools={isBank ? [] : activeTools}
      motionPreview={isBank ? bankPreview : null}
      userTyping={!isBank && draft.trim().length > 0}
      intent={isBank ? null : intentCue}
      className={isViewport ? STAGE_AVATAR_CLASS : undefined}
    />
  ) : null;

  const transcript = (
    <div className="min-h-0 flex-1 space-y-3 overflow-y-auto overscroll-y-contain px-4 py-4">
      {loadingMessages && (
        <p className="text-sm text-[var(--muted)]">Cargando historial…</p>
      )}
      {!loadingMessages &&
        messages.length === 0 &&
        !streamingText &&
        !partialTranscript && (
          <p className="text-sm text-[var(--muted)]">
            Escribí un mensaje o activá el micrófono para empezar.
          </p>
        )}
      {messages.map((message) => (
        <article
          key={message.id}
          className={`max-w-[85%] rounded-2xl px-3 py-2 text-sm whitespace-pre-wrap ${
            message.role === "User"
              ? "ml-auto bg-[var(--user-bubble)] text-white"
              : "bg-[var(--assistant-bubble)] text-[var(--ink)]"
          }`}
        >
          <p className="mb-1 text-[0.7rem] tracking-wide uppercase opacity-70">
            {roleLabel(message.role)}
            {message.wasInterrupted ? " · interrumpido" : ""}
          </p>
          {message.content}
        </article>
      ))}
      {partialTranscript && (
        <article className="ml-auto max-w-[85%] rounded-2xl bg-[var(--user-bubble)] px-3 py-2 text-sm whitespace-pre-wrap opacity-90">
          <p className="mb-1 text-[0.7rem] tracking-wide uppercase opacity-70">
            Vos · en vivo
          </p>
          {partialTranscript}
        </article>
      )}
      {streamingText && (
        <article className="max-w-[85%] rounded-2xl bg-[var(--assistant-bubble)] px-3 py-2 text-sm whitespace-pre-wrap">
          <p className="mb-1 text-[0.7rem] tracking-wide uppercase opacity-70">
            Asistente · en vivo
          </p>
          {streamingText}
        </article>
      )}
      <ConfirmationCard />
      <div ref={transcriptEnd} />
    </div>
  );

  const composer = (
    <form
      onSubmit={(event) => void onSubmit(event)}
      className="shrink-0 border-t border-[var(--line)] p-3"
    >
      <textarea
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onComposerKeyDown}
        disabled={composerLocked}
        rows={3}
        placeholder={
          pendingConfirmation
            ? "Respondé la confirmación para continuar…"
            : voiceStatus === "capturing"
              ? "Grabando… tocá Enviar cuando termines"
              : turnActive
                ? "Turno en curso…"
                : "Escribí un mensaje…"
        }
        className="mb-2 w-full resize-none rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
      />
      <div className="flex items-center justify-between gap-2">
        <MicButton />
        <button
          type="submit"
          disabled={composerLocked || !draft.trim()}
          className="rounded-xl bg-[var(--accent)] px-4 py-2 text-sm text-white"
        >
          Enviar
        </button>
      </div>
    </form>
  );

  return (
    <div
      className={
        isViewport
          ? "relative h-full min-h-0 overflow-hidden"
          : "grid h-full min-h-0 grid-cols-1 grid-rows-[11rem_minmax(0,1fr)] gap-3 overflow-hidden md:grid-cols-[16rem_minmax(0,1fr)] md:grid-rows-none"
      }
    >
      {/* Stable slot 0: keeps sibling index of the panel so AvatarStage does not remount. */}
      <aside
        className={
          isViewport
            ? "hidden"
            : "flex min-h-0 flex-col overflow-hidden rounded-2xl border border-[var(--line)] bg-[var(--panel)] shadow-sm"
        }
        aria-hidden={isViewport}
      >
        {!isViewport && (
          <>
            <div className="flex items-center justify-between gap-2 border-b border-[var(--line)] px-3 py-3">
              <h2 className="text-sm font-semibold">Conversaciones</h2>
              <button
                type="button"
                className="rounded-lg bg-[var(--accent)] px-2.5 py-1 text-xs text-white"
                disabled={connectionStatus !== "connected"}
                onClick={() => void createConversation(null)}
              >
                Nueva
              </button>
            </div>
            <ul className="min-h-0 flex-1 overflow-y-auto p-2">
              {conversations.length === 0 && (
                <li className="px-2 py-3 text-sm text-[var(--muted)]">
                  Todavía no hay conversaciones.
                </li>
              )}
              {conversations.map((conversation) => {
                const selected = conversation.id === selectedId;
                const label = conversationLabel(
                  conversation.title,
                  conversation.startedAt,
                );
                return (
                  <li key={conversation.id} className="mb-1 flex items-stretch gap-1">
                    <button
                      type="button"
                      className={`min-w-0 flex-1 rounded-xl px-3 py-2 text-left text-sm ${
                        selected
                          ? "bg-[var(--accent-soft)] text-[var(--accent)]"
                          : "hover:bg-slate-50"
                      }`}
                      onClick={() => void selectConversation(conversation.id)}
                    >
                      <span className="block truncate font-medium">{label}</span>
                      <span className="block text-xs text-[var(--muted)]">
                        {new Date(conversation.lastMessageAt).toLocaleString("es-AR", {
                          dateStyle: "short",
                          timeStyle: "short",
                        })}
                      </span>
                    </button>
                    <button
                      type="button"
                      className="shrink-0 rounded-xl px-2 text-sm text-[var(--muted)] hover:bg-red-50 hover:text-[var(--danger)]"
                      aria-label={`Eliminar ${label}`}
                      title="Eliminar"
                      disabled={!canMutateList}
                      onClick={() => onDeleteConversation(conversation.id, label)}
                    >
                      ×
                    </button>
                  </li>
                );
              })}
            </ul>
            {conversations.length > 0 && (
              <div className="border-t border-[var(--line)] p-2">
                <button
                  type="button"
                  className="w-full rounded-lg px-2 py-1.5 text-xs text-[var(--danger)] hover:bg-red-50"
                  disabled={!canMutateList}
                  onClick={onDeleteAll}
                >
                  Eliminar todas
                </button>
              </div>
            )}
          </>
        )}
      </aside>

      <section
        className={
          isViewport
            ? "absolute inset-0 overflow-hidden"
            : "flex min-h-0 flex-col overflow-hidden rounded-2xl border border-[var(--line)] bg-[var(--panel)] shadow-sm"
        }
      >
        {/* Stable slot 0: conversation header (column mode only). */}
        <div
          className={
            isViewport
              ? "hidden"
              : "flex items-center justify-between gap-2 border-b border-[var(--line)] px-4 py-3"
          }
          aria-hidden={isViewport}
        >
          <h2 className="text-sm font-semibold">
            {selectedId ? "Conversación" : "Nueva conversación"}
          </h2>
          <div className="flex items-center gap-2">
            {!phoneShell && (
              <button
                type="button"
                className="rounded-lg border border-[var(--line)] px-3 py-1 text-xs text-[var(--ink)]"
                onClick={enterStage}
              >
                Pantalla completa
              </button>
            )}
            {!phoneShell && (
              <button
                type="button"
                className="rounded-lg border border-[var(--line)] px-3 py-1 text-xs text-[var(--ink)]"
                onClick={enterBank}
              >
                Banco
              </button>
            )}
            {showInterrupt && (
              <button
                type="button"
                className="rounded-lg border border-[var(--danger)] px-3 py-1 text-xs text-[var(--danger)]"
                onClick={() => void interrupt()}
              >
                Interrumpir
              </button>
            )}
          </div>
        </div>

        {/* Stable slot 1: avatar + chat panel */}
        <div
          className={
            isViewport
              ? "absolute inset-0"
              : showAvatar
                ? "grid min-h-0 flex-1 grid-cols-1 grid-rows-[minmax(11rem,14rem)_minmax(0,1fr)] gap-0 overflow-hidden md:grid-cols-[minmax(14rem,40%)_minmax(0,1fr)] md:grid-rows-none"
                : "flex min-h-0 flex-1 flex-col overflow-hidden"
          }
        >
          {showAvatar && (
            <div
              className={
                isViewport
                  ? "absolute inset-0"
                  : "min-h-44 max-h-56 border-b border-[var(--line)] p-3 md:max-h-none md:min-h-0 md:border-r md:border-b-0"
              }
            >
              {avatarStage}
            </div>
          )}

          {/* Stable slot: chat panel host — hide with CSS when overlay is off (D4 / D5). */}
          <div
            className={
              !showChatPanel
                ? "hidden"
                : isStage
                  ? "absolute inset-x-0 bottom-14 z-30 flex max-h-[min(55vh,28rem)] min-h-0 flex-col overflow-hidden rounded-t-2xl border border-[var(--line)] bg-[var(--panel)]/95 shadow-lg backdrop-blur-sm md:inset-x-auto md:top-3 md:right-3 md:bottom-16 md:left-auto md:w-[min(28rem,42vw)] md:max-h-none md:rounded-2xl"
                  : showAvatar
                    ? "flex min-h-0 flex-col overflow-hidden"
                    : "flex min-h-0 flex-1 flex-col overflow-hidden"
            }
            aria-hidden={!showChatPanel}
          >
            <div
              className={
                isStage
                  ? "flex shrink-0 items-center justify-between gap-2 border-b border-[var(--line)] px-4 py-3"
                  : "hidden"
              }
              aria-hidden={!isStage}
            >
              <h2 className="text-sm font-semibold">
                {selectedId ? "Conversación" : "Nueva conversación"}
              </h2>
              {showInterrupt && (
                <button
                  type="button"
                  className="rounded-lg border border-[var(--danger)] px-3 py-1 text-xs text-[var(--danger)]"
                  onClick={() => void interrupt()}
                >
                  Interrumpir
                </button>
              )}
            </div>
            {transcript}
            {composer}
          </div>
        </div>

        {/* Stable slot 2: stage chrome */}
        <div className={isStage ? undefined : "hidden"} aria-hidden={!isStage}>
          <StageChrome
            stageChatVisible={stageChatVisible}
            showInterrupt={showInterrupt && !stageChatVisible}
            canHideChat={canHideChat}
            cameraShot={stageCameraShot}
            onToggleChat={toggleStageChat}
            onZoomIn={() => stepStageCameraShot("in")}
            onZoomOut={() => stepStageCameraShot("out")}
            onExit={exitStage}
            onInterrupt={() => void interrupt()}
            phoneShell={phoneShell}
          />
        </div>

        {/* Stable slot 3: bank chrome */}
        <div className={isBank ? undefined : "hidden"} aria-hidden={!isBank}>
          <BankChrome
            expressionId={bankExpression?.expression ?? null}
            motionId={bankMotionId}
            cameraShot={stageCameraShot}
            onExpression={onBankExpression}
            onMotion={onBankMotion}
            onZoomIn={() => stepStageCameraShot("in")}
            onZoomOut={() => stepStageCameraShot("out")}
            onExit={exitStage}
          />
        </div>
      </section>
    </div>
  );
}
