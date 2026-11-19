import type { PushSubscriptionInput } from "./pushApi";

export const pushDatabaseName = "vera-push";
export const pushStoreName = "flags";
export const resubscribeFlag = "resubscribe";
export const vapidPublicKeyFlag = "vapidPublicKey";

export type PushPermission = "granted" | "denied" | "default" | "unsupported";

export interface PushSyncPorts {
  permission: PushPermission;
  storedKey: string | null;
  resubscribeMarked: boolean;
  getServerKey: () => Promise<string | null>;
  getSubscription: () => Promise<PushSubscriptionInput | null>;
  subscribe: (applicationServerKey: Uint8Array<ArrayBuffer>) => Promise<PushSubscriptionInput>;
  unsubscribe: () => Promise<void>;
  putSubscription: (subscription: PushSubscriptionInput) => Promise<void>;
  saveKey: (key: string) => Promise<void>;
  clearMark: () => Promise<void>;
}

export type PushSyncOutcome =
  | { status: "skipped"; reason: "permission" | "unsupported" | "disabled" }
  | { status: "updated" }
  | { status: "rotated" }
  | { status: "resubscribed" };

/** Syncs an already granted subscription. It never requests notification permission. */
export async function syncPushSubscription(ports: PushSyncPorts): Promise<PushSyncOutcome> {
  if (ports.permission === "unsupported") {
    return { status: "skipped", reason: "unsupported" };
  }

  if (ports.permission !== "granted") {
    return { status: "skipped", reason: "permission" };
  }

  const serverKey = (await ports.getServerKey())?.trim() ?? "";
  if (!serverKey) {
    return { status: "skipped", reason: "disabled" };
  }

  const current = await ports.getSubscription();
  const keyChanged = ports.storedKey !== null && ports.storedKey !== serverKey;
  const needsNew = ports.resubscribeMarked || keyChanged || current === null;

  if (!needsNew && current) {
    await ports.putSubscription(current);
    await ports.saveKey(serverKey);
    return { status: "updated" };
  }

  if (current) {
    await ports.unsubscribe();
  }

  const created = await ports.subscribe(decodeBase64Url(serverKey));
  await ports.putSubscription(created);
  await ports.saveKey(serverKey);
  await ports.clearMark();
  if (keyChanged) {
    return { status: "rotated" };
  }

  if (ports.resubscribeMarked) {
    return { status: "resubscribed" };
  }

  return { status: "updated" };
}

export function decodeBase64Url(value: string): Uint8Array<ArrayBuffer> {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized + "=".repeat((4 - (normalized.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

export interface PushFlagSnapshot {
  storedKey: string | null;
  resubscribeMarked: boolean;
}

export async function readPushFlags(): Promise<PushFlagSnapshot> {
  try {
    return await withStore("readonly", (store, finish) => {
      const keyRequest = store.get(vapidPublicKeyFlag);
      const markRequest = store.get(resubscribeFlag);
      markRequest.onsuccess = () => {
        const stored = keyRequest.result;
        finish({
          storedKey: typeof stored === "string" && stored.length > 0 ? stored : null,
          resubscribeMarked: markRequest.result === true,
        });
      };
    });
  } catch {
    return { storedKey: null, resubscribeMarked: false };
  }
}

export function writePushFlag(key: string, value: unknown): Promise<void> {
  return withStore("readwrite", (store, finish) => {
    const request = store.put(value, key);
    request.onsuccess = () => finish(undefined);
  });
}

export function deletePushFlag(key: string): Promise<void> {
  return withStore("readwrite", (store, finish) => {
    const request = store.delete(key);
    request.onsuccess = () => finish(undefined);
  });
}

function openPushDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(pushDatabaseName, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(pushStoreName)) {
        db.createObjectStore(pushStoreName);
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("IndexedDB open failed."));
  });
}

function withStore<T>(
  mode: IDBTransactionMode,
  run: (store: IDBObjectStore, finish: (value: T) => void) => void,
): Promise<T> {
  return openPushDb().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const tx = db.transaction(pushStoreName, mode);
        let value: T | undefined;
        let hasValue = false;
        let finished = false;
        const fail = (error: unknown) => {
          if (finished) {
            return;
          }

          finished = true;
          db.close();
          reject(error instanceof Error ? error : new Error("IndexedDB request failed."));
        };

        tx.oncomplete = () => {
          if (finished) {
            return;
          }

          finished = true;
          db.close();
          if (!hasValue) {
            reject(new Error("IndexedDB request produced no result."));
            return;
          }

          resolve(value as T);
        };
        tx.onerror = () => fail(tx.error);
        tx.onabort = () => fail(tx.error);
        run(tx.objectStore(pushStoreName), (next) => {
          hasValue = true;
          value = next;
        });
      }),
  );
}
