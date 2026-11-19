import { useState } from "react";
import { ApiError, blockChannelPeer, revokeChannelPeer, type ChannelPeerDto } from "../../api/rest";
import { maskExternalId } from "./pairingFormat";
import { ApprovePeerDialog } from "./ApprovePeerDialog";

export function PeerList({
  peers,
  onChanged,
}: {
  peers: ChannelPeerDto[];
  onChanged: () => void;
}) {
  const [approving, setApproving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  async function mutate(peerId: string, action: "revoke" | "block") {
    setBusyId(peerId);
    setError(null);
    try {
      if (action === "revoke") {
        await revokeChannelPeer(peerId);
      } else {
        await blockChannelPeer(peerId);
      }
      onChanged();
    } catch (caught) {
      const status = caught instanceof ApiError ? caught.status : 0;
      setError(status === 404 ? "Esa solicitud ya no está." : "No pude guardar el cambio. Probá de nuevo.");
    } finally {
      setBusyId(null);
    }
  }

  if (peers.length === 0) {
    return <p className="text-sm text-[var(--muted)]">No hay solicitudes.</p>;
  }

  return (
    <ul className="space-y-3">
      {peers.map((peer) => {
        const name = peer.displayName?.trim() || "Sin nombre";
        const handle = peer.username ? `@${peer.username}` : null;
        const when = new Date(peer.lastInboundAt ?? peer.createdAt).toLocaleString("es-AR", {
          day: "numeric",
          month: "short",
          hour: "2-digit",
          minute: "2-digit",
        });
        return (
          <li key={peer.id} className="space-y-3 rounded-xl border border-[var(--line)] p-3">
            <div>
              <p className="font-medium">{name}</p>
              <p className="text-sm text-[var(--muted)]">
                {handle ? `${handle} · ` : ""}
                {maskExternalId(peer.maskedExternalId)} · {label(peer.status)} · {when}
              </p>
            </div>
            {approving === peer.id && (
              <ApprovePeerDialog
                peerId={peer.id}
                displayName={name}
                onClose={() => setApproving(null)}
                onApproved={() => {
                  setApproving(null);
                  onChanged();
                }}
              />
            )}
            <div className="flex flex-col gap-2 sm:flex-row">
              {peer.status === "pending" && approving !== peer.id && (
                <button
                  type="button"
                  onClick={() => setApproving(peer.id)}
                  className="min-h-12 rounded-xl bg-[var(--accent)] px-4 text-sm font-medium text-white"
                >
                  Aprobar
                </button>
              )}
              {peer.status === "approved" && (
                <button
                  type="button"
                  disabled={busyId === peer.id}
                  onClick={() => void mutate(peer.id, "revoke")}
                  className="min-h-12 rounded-xl border border-[var(--line)] bg-white px-4 text-sm"
                >
                  Desvincular
                </button>
              )}
              {peer.status !== "blocked" && (
                <button
                  type="button"
                  disabled={busyId === peer.id}
                  onClick={() => void mutate(peer.id, "block")}
                  className="min-h-12 rounded-xl border border-red-200 bg-white px-4 text-sm text-[var(--danger)]"
                >
                  Bloquear
                </button>
              )}
            </div>
          </li>
        );
      })}
      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}
    </ul>
  );
}

function label(status: string): string {
  switch (status) {
    case "pending":
      return "pendiente";
    case "approved":
      return "vinculado";
    case "revoked":
      return "desvinculado";
    case "blocked":
      return "bloqueado";
    default:
      return status;
  }
}
