import { describe, expect, it } from "vitest";
import type { PushSubscriptionInput } from "./pushApi";
import { decodeBase64Url, syncPushSubscription, type PushSyncPorts } from "./pushSync";

const existing: PushSubscriptionInput = {
  endpoint: "https://push.example/existing",
  keys: { p256dh: "p256", auth: "auth" },
};

const created: PushSubscriptionInput = {
  endpoint: "https://push.example/created",
  keys: { p256dh: "p256-new", auth: "auth-new" },
};

function keyOf(fill: number): string {
  const bytes = new Uint8Array(65);
  bytes.fill(fill);
  bytes[0] = 4;
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function ports(overrides: Partial<PushSyncPorts> = {}): PushSyncPorts & { calls: string[] } {
  const calls: string[] = [];
  const same = keyOf(1);
  return {
    calls,
    permission: "granted",
    storedKey: same,
    resubscribeMarked: false,
    getServerKey: async () => {
      calls.push("server-key");
      return same;
    },
    getSubscription: async () => {
      calls.push("get");
      return existing;
    },
    subscribe: async (applicationServerKey) => {
      calls.push(`subscribe:${applicationServerKey.length}:${applicationServerKey[0]}`);
      return created;
    },
    unsubscribe: async () => {
      calls.push("unsubscribe");
    },
    putSubscription: async (subscription) => {
      calls.push(`put:${subscription.endpoint}`);
    },
    saveKey: async () => {
      calls.push("save");
    },
    clearMark: async () => {
      calls.push("clear");
    },
    ...overrides,
  };
}

describe("syncPushSubscription", () => {
  it("puts the current subscription when permission is granted and the key matches", async () => {
    const fake = ports();
    const outcome = await syncPushSubscription(fake);

    expect(outcome).toEqual({ status: "updated" });
    expect(fake.calls).toEqual([
      "server-key",
      "get",
      "put:https://push.example/existing",
      "save",
    ]);
  });

  it("does not subscribe or request anything when permission was not granted", async () => {
    const fake = ports({
      permission: "default",
      getServerKey: async () => {
        throw new Error("should not read the server key");
      },
    });

    await expect(syncPushSubscription(fake)).resolves.toEqual({
      status: "skipped",
      reason: "permission",
    });
    expect(fake.calls).toEqual([]);
  });

  it("unsubscribes and subscribes again when the VAPID key changed", async () => {
    const next = keyOf(9);
    const fake = ports({
      getServerKey: async () => next,
    });

    const outcome = await syncPushSubscription(fake);

    expect(outcome).toEqual({ status: "rotated" });
    expect(fake.calls).toEqual([
      "get",
      "unsubscribe",
      "subscribe:65:4",
      "put:https://push.example/created",
      "save",
      "clear",
    ]);
    expect(decodeBase64Url(next)).toHaveLength(65);
  });

  it("subscribes when the service worker marked a subscription change", async () => {
    const fake = ports({ resubscribeMarked: true });
    const outcome = await syncPushSubscription(fake);

    expect(outcome).toEqual({ status: "resubscribed" });
    expect(fake.calls[0]).toBe("server-key");
    expect(fake.calls).toContain("unsubscribe");
    expect(fake.calls).toContain("subscribe:65:4");
    expect(fake.calls.at(-1)).toBe("clear");
  });

  it("does not clear the resubscribe mark when the put fails", async () => {
    const fake = ports({
      resubscribeMarked: true,
      putSubscription: async () => {
        fake.calls.push("put");
        throw new Error("put failed");
      },
    });

    await expect(syncPushSubscription(fake)).rejects.toThrow("put failed");
    expect(fake.calls).not.toContain("clear");
    expect(fake.calls).not.toContain("save");
  });
});
