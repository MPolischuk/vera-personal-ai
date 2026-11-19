import { useCallback, useEffect, useState } from "react";
import { listChannelPeers, listChannels, type ChannelPeerDto, type ChannelStatusDto } from "../../api/rest";
import { LinkTelegramDialog } from "./LinkTelegramDialog";
import { PeerList } from "./PeerList";

export function ChannelsSettings() {
  const [telegram, setTelegram] = useState<ChannelStatusDto | null>(null);
  const [peers, setPeers] = useState<ChannelPeerDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const [channels, listed] = await Promise.all([listChannels(), listChannelPeers()]);
      setTelegram(channels.channels.find((channel) => channel.id === "telegram") ?? null);
      setPeers(listed.peers);
      setError(null);
    } catch {
      setError("No pude cargar los canales.");
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const bot = telegram?.botUsername ? `@${telegram.botUsername}` : null;

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Canales</h2>
        <p className="mt-1 text-sm text-[var(--muted)]">
          {telegram?.enabled
            ? telegram.linked
              ? `Telegram ${bot ?? ""} está vinculado.`.trim()
              : `Telegram ${bot ?? ""} está activo y todavía no tiene un chat aprobado.`.trim()
            : "Telegram está apagado. Cuando lo actives, las solicitudes aparecen acá."}
        </p>
      </div>

      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}

      <section className="space-y-3">
        <h3 className="text-sm font-semibold">Solicitudes</h3>
        <PeerList peers={peers} onChanged={() => void reload()} />
      </section>

      <LinkTelegramDialog linked={telegram?.linked === true} />
    </div>
  );
}
