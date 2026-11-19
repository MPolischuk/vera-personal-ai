import { useEffect, useState } from "react";
import { ChannelsSettings } from "../channels/ChannelsSettings";
import { NotificationsSettings } from "../notifications/NotificationsSettings";
import { useAppStore } from "../../store/appStore";

export function SettingsView() {
  const [section, setSection] = useState<"personalidad" | "canales" | "notificaciones">("personalidad");
  const personality = useAppStore((s) => s.personality);
  const loadPersonality = useAppStore((s) => s.loadPersonality);

  useEffect(() => {
    void loadPersonality();
  }, [loadPersonality]);

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setSection("personalidad")}
          className={`min-h-11 rounded-xl px-4 text-sm ${
            section === "personalidad" ? "bg-[var(--ink)] text-white" : "border border-[var(--line)] bg-white"
          }`}
        >
          Personalidad
        </button>
        <button
          type="button"
          onClick={() => setSection("canales")}
          className={`min-h-11 rounded-xl px-4 text-sm ${
            section === "canales" ? "bg-[var(--ink)] text-white" : "border border-[var(--line)] bg-white"
          }`}
        >
          Canales
        </button>
        <button
          type="button"
          onClick={() => setSection("notificaciones")}
          className={`min-h-11 rounded-xl px-4 text-sm ${
            section === "notificaciones" ? "bg-[var(--ink)] text-white" : "border border-[var(--line)] bg-white"
          }`}
        >
          Notificaciones
        </button>
      </div>

      {section === "canales" ? (
        <ChannelsSettings />
      ) : section === "notificaciones" ? (
        <NotificationsSettings />
      ) : !personality ? (
        <p className="text-sm text-[var(--muted)]">Cargando personalidad…</p>
      ) : (
        <PersonalityPanel personality={personality} />
      )}
    </div>
  );
}

function PersonalityPanel({
  personality,
}: {
  personality: NonNullable<ReturnType<typeof useAppStore.getState>["personality"]>;
}) {
  const { profile, systemPrompt, estimatedTokens } = personality;

  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">Personalidad</h2>
        <p className="mt-1 text-sm text-[var(--muted)]">
          Solo lectura. Para cambiar la identidad editá{" "}
          <code className="rounded bg-slate-100 px-1">config/personality.yaml</code>{" "}
          y reiniciá la API.
        </p>
      </div>

      <dl className="grid gap-3 sm:grid-cols-2">
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">Nombre</dt>
          <dd className="font-medium">{profile.name}</dd>
        </div>
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">Idioma</dt>
          <dd className="font-medium">{profile.language}</dd>
        </div>
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">Zona horaria</dt>
          <dd className="font-medium">{profile.timeZone}</dd>
        </div>
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">
            Tokens estimados del prompt
          </dt>
          <dd className="font-medium">{estimatedTokens}</dd>
        </div>
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">
            Avatar (model)
          </dt>
          <dd className="font-medium">
            <code className="rounded bg-slate-100 px-1 text-sm">
              {profile.avatar.model}
            </code>
          </dd>
        </div>
        <div>
          <dt className="text-xs tracking-wide text-[var(--muted)] uppercase">
            Idle animation
          </dt>
          <dd className="font-medium">
            <code className="rounded bg-slate-100 px-1 text-sm">
              {profile.avatar.idleAnimation}
            </code>
          </dd>
        </div>
      </dl>

      <p className="text-sm text-[var(--muted)]">
        El path del avatar sale del YAML. Para cambiarlo editá{" "}
        <code className="rounded bg-slate-100 px-1">config/personality.yaml</code>{" "}
        y reiniciá la API (no hay picker en esta etapa).
      </p>

      <div>
        <h3 className="mb-2 text-sm font-semibold">Estilo</h3>
        <ul className="grid gap-2 text-sm sm:grid-cols-2">
          <li>Formalidad: {profile.style.formality}</li>
          <li>Humor: {profile.style.humor}</li>
          <li>Verbosidad: {profile.style.verbosity}</li>
          <li>Emoji: {profile.style.emoji}</li>
        </ul>
      </div>

      {profile.traits.length > 0 && (
        <div>
          <h3 className="mb-2 text-sm font-semibold">Rasgos</h3>
          <p className="text-sm">{profile.traits.join(" · ")}</p>
        </div>
      )}

      {profile.boundaries.length > 0 && (
        <div>
          <h3 className="mb-2 text-sm font-semibold">Límites</h3>
          <ul className="list-disc space-y-1 pl-5 text-sm">
            {profile.boundaries.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      )}

      <div>
        <h3 className="mb-2 text-sm font-semibold">System prompt compuesto</h3>
        <pre className="max-h-80 overflow-auto rounded-xl bg-slate-950 p-4 text-xs text-slate-100 whitespace-pre-wrap">
          {systemPrompt}
        </pre>
      </div>
    </div>
  );
}
