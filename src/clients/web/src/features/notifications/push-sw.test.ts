import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const source = readFileSync(
  fileURLToPath(new URL("../../../public/push-sw.js", import.meta.url)),
  "utf8",
);

interface ShownNotification {
  title: string;
  options: {
    body?: string;
    tag?: string;
    data?: { navigate?: string; vera?: { notificationId?: string } | null };
    actions?: { action: string; title: string }[];
    silent?: boolean;
  };
}

interface PostedMessage {
  type: string;
  notificationId: string | null;
  action: string;
  conversationId: string | null;
  navigate: string;
}

function createMemoryIdb() {
  const stores = new Map<string, Map<unknown, unknown>>();
  return {
    stores,
    open() {
      const request: {
        result?: {
          objectStoreNames: { contains: (name: string) => boolean };
          createObjectStore: (name: string) => void;
          transaction: (name: string, mode: string) => {
            oncomplete: (() => void) | null;
            onerror: (() => void) | null;
            error: Error | null;
            objectStore: () => { put: (value: unknown, key: unknown) => void };
          };
          close: () => void;
        };
        error: Error | null;
        onupgradeneeded: (() => void) | null;
        onsuccess: (() => void) | null;
        onerror: (() => void) | null;
      } = {
        error: null,
        onupgradeneeded: null,
        onsuccess: null,
        onerror: null,
      };

      queueMicrotask(() => {
        const db = {
          objectStoreNames: {
            contains: (name: string) => stores.has(name),
          },
          createObjectStore(name: string) {
            stores.set(name, new Map());
          },
          transaction(name: string) {
            const map = stores.get(name);
            if (!map) {
              throw new Error(`Missing store ${name}.`);
            }

            const tx = {
              oncomplete: null as (() => void) | null,
              onerror: null as (() => void) | null,
              error: null,
              objectStore() {
                return {
                  put(value: unknown, key: unknown) {
                    map.set(key, value);
                  },
                };
              },
            };
            queueMicrotask(() => tx.oncomplete?.());
            return tx;
          },
          close() {},
        };
        request.result = db;
        if (!stores.has("flags")) {
          request.onupgradeneeded?.();
        }
        request.onsuccess?.();
      });
      return request;
    },
  };
}

function boot() {
  const listeners = new Map<string, (event: Record<string, unknown>) => void>();
  const shown: ShownNotification[] = [];
  const posted: PostedMessage[] = [];
  const opened: string[] = [];
  const focused: number[] = [];
  const closed: number[] = [];
  const idb = createMemoryIdb();
  const clients: {
    focus: () => Promise<void>;
    postMessage: (data: PostedMessage) => void;
  }[] = [];

  const self = {
    addEventListener(type: string, handler: (event: Record<string, unknown>) => void) {
      listeners.set(type, handler);
    },
    registration: {
      scope: "https://assistant.example/",
      showNotification: async (title: string, options: ShownNotification["options"]) => {
        shown.push({ title, options });
      },
    },
    clients: {
      matchAll: async () => clients.slice(),
      openWindow: async (url: string) => {
        opened.push(url);
        return null;
      },
    },
    indexedDB: idb,
  };

  const sandbox = {
    self,
    URL,
    Promise,
    encodeURIComponent,
  };
  runInContext(source, createContext(sandbox));

  return {
    shown,
    posted,
    opened,
    focused,
    closed,
    stores: idb.stores,
    clients,
    async dispatch(type: string, event: Record<string, unknown>) {
      const handler = listeners.get(type);
      if (!handler) {
        throw new Error(`Missing listener ${type}.`);
      }

      let pending = Promise.resolve();
      handler({
        ...event,
        waitUntil(promise: Promise<void>) {
          pending = promise;
        },
      });
      await pending;
    },
  };
}

const payload = {
  web_push: 8030,
  notification: {
    title: "Recordatorio",
    body: "Sacar la basura",
    navigate: "https://assistant.example/?notification=8f1c&conversation=2b9e",
    tag: "reminder:5d7a",
    silent: true,
    lang: "es-AR",
    dir: "ltr",
  },
  vera: {
    notificationId: "8f1c",
    actions: [
      { action: "done", title: "Listo" },
      { action: "snooze:10m", title: "+10 min" },
    ],
  },
};

describe("push-sw", () => {
  it("shows the notification from a valid payload and never marks it silent", async () => {
    const worker = boot();
    await worker.dispatch("push", {
      data: { json: () => payload },
    });

    expect(worker.shown).toHaveLength(1);
    expect(worker.shown[0]?.title).toBe("Recordatorio");
    expect(worker.shown[0]?.options.body).toBe("Sacar la basura");
    expect(worker.shown[0]?.options.tag).toBe("reminder:5d7a");
    expect(worker.shown[0]?.options.silent).toBeUndefined();
    expect(worker.shown[0]?.options.data?.navigate).toContain("conversation=2b9e");
    expect(worker.shown[0]?.options.data?.vera?.notificationId).toBe("8f1c");
    expect(worker.shown[0]?.options.actions).toEqual([
      { action: "done", title: "Listo" },
      { action: "snooze:10m", title: "+10 min" },
    ]);
  });

  it("shows a fallback when the payload is not JSON", async () => {
    const worker = boot();
    await worker.dispatch("push", {
      data: {
        json: () => {
          throw new SyntaxError("bad");
        },
      },
    });

    expect(worker.shown).toEqual([{ title: "Tenés un aviso de Vera", options: {} }]);
  });

  it("focuses an open client and posts the click", async () => {
    const worker = boot();
    worker.clients.push({
      focus: async () => {
        worker.focused.push(1);
      },
      postMessage: (data) => {
        worker.posted.push(data);
      },
    });

    await worker.dispatch("notificationclick", {
      action: "done",
      notification: {
        close: () => {
          worker.closed.push(1);
        },
        data: {
          navigate: payload.notification.navigate,
          vera: payload.vera,
        },
      },
    });

    expect(worker.closed).toEqual([1]);
    expect(worker.focused).toEqual([1]);
    expect(worker.opened).toEqual([]);
    expect(worker.posted[0]).toMatchObject({
      type: "vera.notification",
      notificationId: "8f1c",
      action: "done",
      conversationId: "2b9e",
    });
    expect(worker.posted[0]?.navigate).toBe(
      "https://assistant.example/?notification=8f1c&conversation=2b9e&action=done",
    );
  });

  it("opens a window when no client is open", async () => {
    const worker = boot();
    await worker.dispatch("notificationclick", {
      action: "snooze:10m",
      notification: {
        close: () => {
          worker.closed.push(1);
        },
        data: {
          navigate: payload.notification.navigate,
          vera: payload.vera,
        },
      },
    });

    expect(worker.posted).toEqual([]);
    expect(worker.opened).toEqual([
      "https://assistant.example/?notification=8f1c&conversation=2b9e&action=snooze%3A10m",
    ]);
  });

  it("marks IndexedDB when the subscription changes", async () => {
    const worker = boot();
    await worker.dispatch("pushsubscriptionchange", {});
    expect(worker.stores.get("flags")?.get("resubscribe")).toBe(true);
  });
});
