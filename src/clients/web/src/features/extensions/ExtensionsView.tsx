import { useEffect, useState } from "react";
import {
  ApiError,
  approveSkillProposal,
  classifyExtensionTool,
  discardSkillProposal,
  listExtensions,
  listSkillProposals,
  setExtensionEnabled,
  startCalendarOAuth,
  type ExtensionServerDto,
  type SkillProposalDto,
} from "../../api/rest";

const permissionOptions = [
  { value: "Read", label: "Lectura" },
  { value: "ReversibleAction", label: "Acción reversible" },
  { value: "SensitiveAction", label: "Acción sensible" },
] as const;

const trustOptions = [
  { value: "Untrusted", label: "No confiable" },
  { value: "Trusted", label: "Confiable" },
] as const;

type ToolDraft = {
  permissionLevel: string;
  outputTrust: string;
  writesDurableState: boolean;
};

function draftKey(serverId: string, remoteName: string): string {
  return `${serverId}\0${remoteName}`;
}

function statusLabel(status: string): string {
  switch (status) {
    case "Connected":
      return "Conectado";
    case "Disabled":
      return "Deshabilitado";
    case "Unavailable":
      return "No disponible";
    case "NotConnected":
      return "Sin conectar";
    default:
      return status;
  }
}

function messageOf(err: unknown): string {
  if (err instanceof ApiError) {
    return err.message;
  }
  if (err instanceof Error) {
    return err.message;
  }
  return "Error de red.";
}

function diffLineClass(line: string): string {
  if (line.startsWith("+++") || line.startsWith("---") || line.startsWith("@@")) {
    return "text-zinc-400";
  }
  if (line.startsWith("+")) {
    return "text-green-400";
  }
  if (line.startsWith("-")) {
    return "text-red-400";
  }
  return "text-zinc-300";
}

function DiffBlock({ diff }: { diff: string }) {
  const lines = diff.split("\n");
  return (
    <pre className="overflow-x-auto rounded bg-zinc-950 p-3 font-mono text-xs leading-5">
      {lines.map((line, index) => (
        <div key={index} className={diffLineClass(line)}>
          {line.length === 0 ? " " : line}
        </div>
      ))}
    </pre>
  );
}

export function ExtensionsView() {
  const [servers, setServers] = useState<ExtensionServerDto[]>([]);
  const [proposals, setProposals] = useState<SkillProposalDto[]>([]);
  const [drafts, setDrafts] = useState<Record<string, ToolDraft>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  async function reload() {
    const [inventory, pending] = await Promise.all([listExtensions(), listSkillProposals()]);
    setServers(inventory.servers);
    setProposals(pending);
    setDrafts((current) => {
      const next: Record<string, ToolDraft> = {};
      for (const server of inventory.servers) {
        for (const tool of server.tools) {
          const key = draftKey(server.id, tool.remoteName);
          next[key] = current[key] ?? {
            permissionLevel:
              tool.declared && tool.permissionLevel !== "Undeclared"
                ? tool.permissionLevel
                : "Read",
            outputTrust: tool.outputTrust === "Trusted" ? "Trusted" : "Untrusted",
            writesDurableState: tool.writesDurableState,
          };
        }
      }
      return next;
    });
  }

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        await reload();
      } catch (err) {
        if (!cancelled) {
          setError(messageOf(err));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  async function connectCalendar() {
    setBusyKey("oauth:calendar");
    setError(null);
    try {
      const started = await startCalendarOAuth();
      window.location.assign(started.url);
    } catch (err) {
      setError(messageOf(err));
      setBusyKey(null);
    }
  }

  async function toggleEnabled(server: ExtensionServerDto, enabled: boolean) {
    setBusyKey(`enabled:${server.id}`);
    setError(null);
    try {
      await setExtensionEnabled(server.id, enabled);
      await reload();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  async function saveTool(serverId: string, remoteName: string) {
    const key = draftKey(serverId, remoteName);
    const draft = drafts[key];
    if (!draft) {
      return;
    }

    setBusyKey(key);
    setError(null);
    try {
      await classifyExtensionTool(serverId, {
        toolName: remoteName,
        permissionLevel: draft.permissionLevel,
        outputTrust: draft.outputTrust,
        writesDurableState: draft.writesDurableState,
      });
      await reload();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  function updateDraft(serverId: string, remoteName: string, patch: Partial<ToolDraft>) {
    const key = draftKey(serverId, remoteName);
    setDrafts((current) => ({
      ...current,
      [key]: { ...current[key], ...patch },
    }));
  }

  async function resolveProposal(proposal: SkillProposalDto, action: "approve" | "discard") {
    setBusyKey(`${action}:${proposal.id}`);
    setError(null);
    try {
      if (action === "approve") {
        await approveSkillProposal(proposal.id, proposal.diffSha256);
      } else {
        await discardSkillProposal(proposal.id);
      }
      await reload();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyKey(null);
    }
  }

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto">
      <h2 className="text-lg font-semibold">Extensiones</h2>

      {error && (
        <p className="text-sm text-red-700" role="alert">
          {error}
        </p>
      )}

      {loading && <p className="text-sm text-zinc-600">Cargando…</p>}

      {!loading && servers.length === 0 && (
        <p className="text-sm text-zinc-600">
          No hay servidores en el manifiesto. Agregar uno es editar{" "}
          <code className="rounded bg-zinc-100 px-1">config/extensions.yaml</code> y reiniciar.
        </p>
      )}

      {servers.map((server) => (
        <section key={server.id} className="space-y-3 border-t border-zinc-200 pt-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h3 className="font-medium">{server.id}</h3>
              <p className="text-sm text-zinc-600">
                {server.transport} · {statusLabel(server.status)}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              {server.oauthConnected === true && (
                <span className="text-sm text-zinc-700">Conectado</span>
              )}
              {server.oauthConnected === false && (
                <button
                  type="button"
                  className="rounded bg-zinc-900 px-3 py-1 text-sm text-white disabled:opacity-50"
                  disabled={busyKey === "oauth:calendar"}
                  onClick={() => {
                    void connectCalendar();
                  }}
                >
                  Conectar
                </button>
              )}
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={server.enabled}
                  disabled={busyKey === `enabled:${server.id}`}
                  onChange={(event) => {
                    void toggleEnabled(server, event.target.checked);
                  }}
                />
                Habilitado
              </label>
            </div>
          </div>

          {server.tools.length === 0 ? (
            <p className="text-sm text-zinc-600">Sin tools en el manifiesto ni en la sesión.</p>
          ) : (
            <ul className="space-y-4">
              {server.tools.map((tool) => {
                const key = draftKey(server.id, tool.remoteName);
                const draft = drafts[key] ?? {
                  permissionLevel: "Read",
                  outputTrust: "Untrusted",
                  writesDurableState: false,
                };
                return (
                  <li key={tool.remoteName} className="space-y-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-sm">{tool.remoteName}</span>
                      {tool.catalogName && (
                        <span className="text-sm text-zinc-500">({tool.catalogName})</span>
                      )}
                      {!tool.declared && (
                        <span className="rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-900">
                          Sin clasificar
                        </span>
                      )}
                    </div>
                    <div className="flex flex-wrap items-end gap-3">
                      <label className="flex flex-col gap-1 text-xs text-zinc-600">
                        Nivel
                        <select
                          className="rounded border border-zinc-300 px-2 py-1 text-sm text-zinc-900"
                          value={draft.permissionLevel}
                          onChange={(event) =>
                            updateDraft(server.id, tool.remoteName, {
                              permissionLevel: event.target.value,
                            })
                          }
                        >
                          {permissionOptions.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="flex flex-col gap-1 text-xs text-zinc-600">
                        Confianza
                        <select
                          className="rounded border border-zinc-300 px-2 py-1 text-sm text-zinc-900"
                          value={draft.outputTrust}
                          onChange={(event) =>
                            updateDraft(server.id, tool.remoteName, {
                              outputTrust: event.target.value,
                            })
                          }
                        >
                          {trustOptions.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          checked={draft.writesDurableState}
                          onChange={(event) =>
                            updateDraft(server.id, tool.remoteName, {
                              writesDurableState: event.target.checked,
                            })
                          }
                        />
                        Escribe estado durable
                      </label>
                      <button
                        type="button"
                        className="rounded bg-zinc-900 px-3 py-1 text-sm text-white disabled:opacity-50"
                        disabled={busyKey === key}
                        onClick={() => {
                          void saveTool(server.id, tool.remoteName);
                        }}
                      >
                        Guardar
                      </button>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      ))}

      {!loading && (
        <section className="space-y-3 border-t border-zinc-200 pt-4">
          <h3 className="font-medium">Propuestas</h3>
          {proposals.length === 0 ? (
            <p className="text-sm text-zinc-600">No hay propuestas pendientes.</p>
          ) : (
            proposals.map((proposal) => (
              <article key={proposal.id} className="space-y-2 rounded border border-zinc-200 p-3">
                <h4 className="font-medium">{proposal.name}</h4>
                <p className="text-sm text-zinc-600">{proposal.description}</p>
                <DiffBlock diff={proposal.diff} />
                <div className="flex gap-2">
                  <button
                    type="button"
                    className="rounded bg-zinc-900 px-3 py-1 text-sm text-white disabled:opacity-50"
                    disabled={busyKey === `approve:${proposal.id}` || busyKey === `discard:${proposal.id}`}
                    onClick={() => {
                      void resolveProposal(proposal, "approve");
                    }}
                  >
                    Aprobar
                  </button>
                  <button
                    type="button"
                    className="rounded border border-zinc-300 px-3 py-1 text-sm disabled:opacity-50"
                    disabled={busyKey === `approve:${proposal.id}` || busyKey === `discard:${proposal.id}`}
                    onClick={() => {
                      void resolveProposal(proposal, "discard");
                    }}
                  >
                    Descartar
                  </button>
                </div>
              </article>
            ))
          )}
        </section>
      )}
    </div>
  );
}
