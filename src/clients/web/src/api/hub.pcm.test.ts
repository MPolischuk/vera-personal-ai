import { describe, expect, it } from "vitest";
import { asPcmBytes, pcmToBase64 } from "./hub";

describe("asPcmBytes", () => {
  it("passes through Uint8Array", () => {
    const input = new Uint8Array([1, 2, 3]);
    expect(asPcmBytes(input)).toEqual(input);
  });

  it("decodes Base64 strings from JSON SignalR", () => {
    const bytes = new Uint8Array([0, 16, 255]);
    const b64 = Buffer.from(bytes).toString("base64");
    expect(asPcmBytes(b64)).toEqual(bytes);
  });

  it("accepts number arrays", () => {
    expect(asPcmBytes([4, 5, 6])).toEqual(new Uint8Array([4, 5, 6]));
  });

  it("round-trips s16le frames as SignalR JSON Base64", () => {
    const pcm = new Uint8Array(640);
    pcm[0] = 7;
    pcm[639] = 9;
    expect(asPcmBytes(pcmToBase64(pcm))).toEqual(pcm);
  });
});
