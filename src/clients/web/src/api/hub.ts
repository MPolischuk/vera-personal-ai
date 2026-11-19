import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { shouldAckNotification } from "../features/notifications/notificationAck";
import { pushNotification } from "../features/notifications/useNotifications";
import { dispatchEnvelope, type EnvelopeHandlers } from "./dispatchEnvelope";
import { getApiBaseUrl, getBearerToken } from "./rest";
import { getAccessToken, refresh } from "./session";
import {
  createEnvelope,
  MessageTypes,
  type AudioChunkHeader,
  type Envelope,
  type NotificationPayload,
} from "./protocol";

export type AudioHandler = (header: AudioChunkHeader, pcm: Uint8Array) => void;

let connection: HubConnection | null = null;
let connectPromise: Promise<HubConnection> | null = null;
let audioHandler: AudioHandler | null = null;

/** Normalize SignalR JSON byte[] (Uint8Array, ArrayBuffer, Base64 string, or number[]). */
export function asPcmBytes(raw: unknown): Uint8Array {
  if (raw instanceof Uint8Array) {
    return raw;
  }
  if (raw instanceof ArrayBuffer) {
    return new Uint8Array(raw);
  }
  if (ArrayBuffer.isView(raw)) {
    const view = raw as ArrayBufferView;
    return new Uint8Array(view.buffer, view.byteOffset, view.byteLength);
  }
  if (typeof raw === "string") {
    const binary = atob(raw);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  }
  if (Array.isArray(raw)) {
    return Uint8Array.from(raw as number[]);
  }
  throw new Error("Unsupported PCM payload from the hub.");
}

/** SignalR JSON protocol encodes C# byte[] as a Base64 string. */
export function pcmToBase64(pcm: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < pcm.length; i++) {
    binary += String.fromCharCode(pcm[i]!);
  }
  return btoa(binary);
}

export function setAudioHandler(handler: AudioHandler | null): void {
  audioHandler = handler;
}

export function getHubConnection(): HubConnection | null {
  return connection;
}

export async function connectHub(handlers: EnvelopeHandlers): Promise<HubConnection> {
  if (!getAccessToken()) {
    throw new Error("No se pudo entrar.");
  }

  if (connection?.state === HubConnectionState.Connected) {
    return connection;
  }

  if (connectPromise) {
    return connectPromise;
  }

  connectPromise = (async () => {
    if (connection) {
      await connection.stop();
      connection = null;
    }

    const base = getApiBaseUrl();
    const hub = new HubConnectionBuilder()
      .withUrl(`${base}/hub/assistant`, {
        accessTokenFactory: () => getAccessToken(),
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    hub.on("Receive", (envelope: Envelope) => {
      dispatchEnvelope(envelope, withNotificationHandler(handlers, hub));
    });

    hub.on("ReceiveAudio", (header: AudioChunkHeader, pcm: unknown) => {
      if (!audioHandler) {
        return;
      }
      audioHandler(header, asPcmBytes(pcm));
    });

    await startHub(hub);
    connection = hub;
    return hub;
  })();

  try {
    return await connectPromise;
  } finally {
    connectPromise = null;
  }
}

async function startHub(hub: HubConnection): Promise<void> {
  try {
    await hub.start();
  } catch (error) {
    if (getBearerToken() || !(await refresh())) {
      throw error;
    }
    await hub.start();
  }
}

function withNotificationHandler(
  handlers: EnvelopeHandlers,
  hub: HubConnection,
): EnvelopeHandlers {
  return {
    ...handlers,
    [MessageTypes.Notification]: (envelope) => {
      const payload = envelope.payload as NotificationPayload;
      if (!payload?.notificationId || !payload.deliveryId) {
        return;
      }

      pushNotification(payload);
      const visibility =
        typeof document === "undefined" ? "hidden" : document.visibilityState;
      if (!shouldAckNotification(visibility)) {
        return;
      }

      void hub.invoke(
        "Send",
        createEnvelope(
          MessageTypes.NotificationAck,
          { deliveryId: payload.deliveryId },
          payload.deliveryId,
        ),
      );
    },
  };
}

export async function disconnectHub(): Promise<void> {
  if (!connection) {
    return;
  }
  await connection.stop();
  connection = null;
}

export async function sendEnvelope(envelope: Envelope): Promise<void> {
  if (!connection || connection.state !== HubConnectionState.Connected) {
    throw new Error("No hay conexión con el hub.");
  }
  await connection.invoke("Send", envelope);
}

export async function sendAudio(
  conversationId: string,
  pcm: Uint8Array,
): Promise<void> {
  if (!connection || connection.state !== HubConnectionState.Connected) {
    throw new Error("No hay conexión con el hub.");
  }
  await connection.invoke("SendAudio", conversationId, pcmToBase64(pcm));
}
