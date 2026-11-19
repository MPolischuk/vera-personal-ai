import { describe, expect, it } from "vitest";
import { approvePeerRequest, formatPairingCode, maskExternalId, pairingErrorMessage } from "./pairingFormat";

describe("pairing format", () => {
  it("masks the external id down to the last four characters", () => {
    expect(maskExternalId("1234564821")).toBe("…4821");
    expect(maskExternalId("…4821")).toBe("…4821");
  });

  it("groups a pairing code as XXXX-XXXX", () => {
    expect(formatPairingCode("k7m2 qx4p")).toBe("K7M2-QX4P");
    expect(formatPairingCode("k7m2-qx4p")).toBe("K7M2-QX4P");
  });

  it("builds the approve request without sending the raw peer id as the code", () => {
    const request = approvePeerRequest("6f0b9c4e-1d2a-4b3c-8d4e-5f6a7b8c9d0e", "k7m2qx4p");
    expect(request.path).toBe("/api/channels/peers/6f0b9c4e-1d2a-4b3c-8d4e-5f6a7b8c9d0e/approve");
    expect(request.body).toEqual({ code: "K7M2-QX4P" });
    expect(JSON.stringify(request)).not.toContain("1234564821");
  });

  it("maps the limit and a bad code to rioplatense copy", () => {
    expect(pairingErrorMessage(409, "peer_limit_reached")).toContain("vinculado");
    expect(pairingErrorMessage(410, "pairing_code_expired")).toContain("venció");
    expect(pairingErrorMessage(422, "pairing_code_invalid")).toContain("no cierra");
  });
});
