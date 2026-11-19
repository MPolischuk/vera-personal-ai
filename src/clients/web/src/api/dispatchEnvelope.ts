import { PROTOCOL_VERSION, type Envelope } from "./protocol";

export type EnvelopeHandler = (envelope: Envelope) => void;

export type EnvelopeHandlers = Record<string, EnvelopeHandler | undefined>;

/**
 * Routes a protocol envelope to a typed handler.
 * Unknown types are ignored (B8). Wrong protocol version is logged and ignored.
 */
export function dispatchEnvelope(
  envelope: Envelope,
  handlers: EnvelopeHandlers,
): void {
  if (envelope.v !== PROTOCOL_VERSION) {
    console.warn("Ignored unsupported protocol version", envelope.v);
    return;
  }

  const handler = handlers[envelope.type];
  if (!handler) {
    console.warn("Ignored unknown message type", envelope.type);
    return;
  }

  handler(envelope);
}
