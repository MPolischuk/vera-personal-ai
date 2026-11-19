import { describe, expect, it, vi } from "vitest";
import { dispatchEnvelope } from "./dispatchEnvelope";
import { MessageTypes, PROTOCOL_VERSION, type Envelope } from "./protocol";

function makeEnvelope(partial: Partial<Envelope> & Pick<Envelope, "type">): Envelope {
  return {
    v: PROTOCOL_VERSION,
    correlationId: "00000000-0000-0000-0000-000000000001",
    timestamp: "2026-09-10T12:00:00Z",
    payload: {},
    ...partial,
  };
}

describe("dispatchEnvelope", () => {
  it("invokes the handler for a known message type", () => {
    const tokenHandler = vi.fn();
    const envelope = makeEnvelope({
      type: MessageTypes.Token,
      payload: { text: "hola" },
    });

    dispatchEnvelope(envelope, { [MessageTypes.Token]: tokenHandler });

    expect(tokenHandler).toHaveBeenCalledTimes(1);
    expect(tokenHandler).toHaveBeenCalledWith(envelope);
  });

  it("ignores an unknown type without throwing", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const tokenHandler = vi.fn();
    const envelope = makeEnvelope({ type: "assistant.from_the_future" });

    expect(() =>
      dispatchEnvelope(envelope, { [MessageTypes.Token]: tokenHandler }),
    ).not.toThrow();

    expect(tokenHandler).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalledWith(
      "Ignored unknown message type",
      "assistant.from_the_future",
    );
    warn.mockRestore();
  });

  it("ignores avatar.gesture when no handler is registered (K10)", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const tokenHandler = vi.fn();
    const envelope = makeEnvelope({
      type: MessageTypes.AvatarGesture,
      payload: { gesture: "nod", intensity: 0.7, durationMs: 700 },
    });

    expect(() =>
      dispatchEnvelope(envelope, { [MessageTypes.Token]: tokenHandler }),
    ).not.toThrow();

    expect(tokenHandler).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalledWith(
      "Ignored unknown message type",
      MessageTypes.AvatarGesture,
    );
    warn.mockRestore();
  });

  it("passes known payloads that include extra fields to the handler", () => {
    const tokenHandler = vi.fn();
    const envelope = makeEnvelope({
      type: MessageTypes.Token,
      payload: { text: "hola", extraField: "ignored-by-consumer" },
    });

    dispatchEnvelope(envelope, { [MessageTypes.Token]: tokenHandler });

    expect(tokenHandler).toHaveBeenCalledWith(envelope);
  });

  it("ignores an unsupported protocol version", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const tokenHandler = vi.fn();
    const envelope = makeEnvelope({
      v: 99,
      type: MessageTypes.Token,
      payload: { text: "x" },
    });

    dispatchEnvelope(envelope, { [MessageTypes.Token]: tokenHandler });

    expect(tokenHandler).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalledWith("Ignored unsupported protocol version", 99);
    warn.mockRestore();
  });
});
