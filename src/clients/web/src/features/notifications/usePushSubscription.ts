import { useCallback, useEffect, useState } from "react";
import {
  deleteCurrentPushSubscription,
  getVapidPublicKey,
  listPushSubscriptions,
  pushErrorMessage,
  putPushSubscription,
  sendTestPush,
  type PushSubscriptionInput,
  type PushSubscriptionSummary,
} from "./pushApi";
import {
  deletePushFlag,
  readPushFlags,
  resubscribeFlag,
  syncPushSubscription,
  vapidPublicKeyFlag,
  writePushFlag,
  type PushPermission,
} from "./pushSync";

export interface PushSubscriptionController {
  permission: PushPermission;
  subscribed: boolean;
  devices: PushSubscriptionSummary[];
  busy: boolean;
  error: string | null;
  notice: string | null;
  activate: () => Promise<void>;
  deactivate: () => Promise<void>;
  sendTest: () => Promise<void>;
}

function readPermission(): PushPermission {
  if (typeof Notification === "undefined" || !("serviceWorker" in navigator) || !("PushManager" in window)) {
    return "unsupported";
  }

  if (Notification.permission === "granted" || Notification.permission === "denied" || Notification.permission === "default") {
    return Notification.permission;
  }

  return "unsupported";
}

function snapshot(subscription: PushSubscription): PushSubscriptionInput | null {
  const json = subscription.toJSON();
  const p256dh = json.keys?.p256dh;
  const auth = json.keys?.auth;
  if (!json.endpoint || !p256dh || !auth) {
    return null;
  }

  return { endpoint: json.endpoint, keys: { p256dh, auth } };
}

async function registrationReady(): Promise<ServiceWorkerRegistration> {
  const ready = navigator.serviceWorker.ready;
  const timeout = new Promise<never>((_, reject) => {
    window.setTimeout(() => reject(new Error("The service worker did not become ready.")), 10_000);
  });
  return Promise.race([ready, timeout]);
}

let grantedSync: Promise<boolean> | null = null;

async function syncGranted(permission: PushPermission): Promise<boolean> {
  if (permission !== "granted") {
    return false;
  }

  if (!grantedSync) {
    grantedSync = performGrantedSync().finally(() => {
      grantedSync = null;
    });
  }

  return grantedSync;
}

async function performGrantedSync(): Promise<boolean> {
  const registration = await registrationReady();
  const flags = await readPushFlags();
  await syncPushSubscription({
    permission: "granted",
    storedKey: flags.storedKey,
    resubscribeMarked: flags.resubscribeMarked,
    getServerKey: getVapidPublicKey,
    getSubscription: async () => {
      const current = await registration.pushManager.getSubscription();
      return current ? snapshot(current) : null;
    },
    subscribe: async (applicationServerKey) => {
      const created = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey,
      });
      const body = snapshot(created);
      if (!body) {
        throw new Error("The push subscription has no keys.");
      }

      return body;
    },
    unsubscribe: async () => {
      const current = await registration.pushManager.getSubscription();
      if (current) {
        await current.unsubscribe();
      }
    },
    putSubscription: putPushSubscription,
    saveKey: (key) => writePushFlag(vapidPublicKeyFlag, key),
    clearMark: () => deletePushFlag(resubscribeFlag),
  });

  const current = await registration.pushManager.getSubscription();
  return current !== null;
}

export function usePushSubscription(active: boolean): PushSubscriptionController {
  const [permission, setPermission] = useState<PushPermission>("default");
  const [subscribed, setSubscribed] = useState(false);
  const [devices, setDevices] = useState<PushSubscriptionSummary[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const reload = useCallback(async () => {
    const currentPermission = readPermission();
    setPermission(currentPermission);
    if (!active) {
      setSubscribed(false);
      setDevices([]);
      return;
    }

    const locallySubscribed = await syncGranted(currentPermission);
    setSubscribed(locallySubscribed);
    try {
      setDevices(await listPushSubscriptions());
    } catch (listError) {
      setDevices([]);
      throw listError;
    }
  }, [active]);

  useEffect(() => {
    if (!active) {
      setSubscribed(false);
      setDevices([]);
      setError(null);
      setNotice(null);
      return;
    }

    let cancelled = false;
    setBusy(true);
    void reload()
      .catch((syncError: unknown) => {
        if (!cancelled) {
          setError(pushErrorMessage(syncError));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBusy(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [active, reload]);

  const activate = useCallback(async () => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      let current = readPermission();
      if (current === "unsupported") {
        setPermission(current);
        return;
      }

      if (current !== "granted") {
        current = await Notification.requestPermission();
        setPermission(current);
      }

      if (current !== "granted") {
        return;
      }

      await reload();
      setNotice("Listo. Este dispositivo ya puede recibir avisos.");
    } catch (activateError) {
      setError(pushErrorMessage(activateError));
    } finally {
      setBusy(false);
    }
  }, [reload]);

  const deactivate = useCallback(async () => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await deleteCurrentPushSubscription();
      if ("serviceWorker" in navigator) {
        const registration = await registrationReady();
        const current = await registration.pushManager.getSubscription();
        if (current) {
          await current.unsubscribe();
        }
      }

      await deletePushFlag(vapidPublicKeyFlag);
      setSubscribed(false);
      setDevices(await listPushSubscriptions().catch(() => []));
      setNotice("Listo, este dispositivo ya no recibe avisos.");
    } catch (disableError) {
      setError(pushErrorMessage(disableError, "disable"));
    } finally {
      setBusy(false);
    }
  }, []);

  const sendTest = useCallback(async () => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await sendTestPush();
      setNotice("Mandé una prueba a este dispositivo. Si cerrás la app, el aviso tiene que aparecer igual.");
    } catch (testError) {
      setError(pushErrorMessage(testError, "test"));
    } finally {
      setBusy(false);
    }
  }, []);

  return {
    permission,
    subscribed,
    devices,
    busy,
    error,
    notice,
    activate,
    deactivate,
    sendTest,
  };
}
