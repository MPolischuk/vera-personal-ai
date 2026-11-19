/* Classic script imported by the generated service worker (generateSW + importScripts).
   No fetch, no credentials. Push actions are forwarded to the page. */
self.addEventListener("push", (event) => {
  event.waitUntil(showPush(event));
});

self.addEventListener("notificationclick", (event) => {
  event.waitUntil(openFromNotification(event));
});

self.addEventListener("pushsubscriptionchange", (event) => {
  event.waitUntil(markResubscribe());
});

async function showPush(event) {
  const fallback = "Tenés un aviso de Vera";
  let title = fallback;
  /** @type {NotificationOptions} */
  let options = {};
  try {
    const payload = event.data ? event.data.json() : null;
    const notification = payload && payload.notification;
    if (notification && typeof notification === "object") {
      title =
        typeof notification.title === "string" && notification.title.trim()
          ? notification.title
          : fallback;
      const vera = payload.vera && typeof payload.vera === "object" ? payload.vera : null;
      const actions = notificationActions(vera);
      options = {
        body: typeof notification.body === "string" ? notification.body : "",
        lang: typeof notification.lang === "string" ? notification.lang : "es-AR",
        dir: notification.dir === "rtl" ? "rtl" : "ltr",
        data: {
          navigate: typeof notification.navigate === "string" ? notification.navigate : "/",
          vera,
        },
      };
      if (typeof notification.tag === "string" && notification.tag) {
        options.tag = notification.tag;
      }
      if (actions.length > 0) {
        options.actions = actions;
      }
    }
  } catch {
    title = fallback;
    options = {};
  }

  try {
    await self.registration.showNotification(title, options);
  } catch {
    await self.registration.showNotification(fallback);
  }
}

function notificationActions(vera) {
  if (!vera || !Array.isArray(vera.actions)) {
    return [];
  }

  return vera.actions
    .filter((item) => item && typeof item.action === "string" && typeof item.title === "string")
    .map((item) => ({ action: item.action, title: item.title }));
}

async function openFromNotification(event) {
  event.notification.close();
  const data = (event.notification && event.notification.data) || {};
  const navigate = typeof data.navigate === "string" ? data.navigate : "/";
  const action = typeof event.action === "string" ? event.action : "";
  const url = withAction(navigate, action);
  const absolute = absoluteUrl(url);
  const vera = data.vera && typeof data.vera === "object" ? data.vera : {};
  const message = {
    type: "vera.notification",
    notificationId: typeof vera.notificationId === "string" ? vera.notificationId : null,
    action,
    conversationId: queryParam(absolute, "conversation"),
    navigate: absolute,
  };

  const windows = await self.clients.matchAll({ type: "window" });
  const client = windows[0];
  if (client) {
    await client.focus();
    client.postMessage(message);
    return;
  }

  await self.clients.openWindow(absolute);
}

function withAction(navigate, action) {
  if (!action) {
    return navigate;
  }

  const join = navigate.includes("?") ? "&" : "?";
  return navigate + join + "action=" + encodeURIComponent(action);
}

function queryParam(value, name) {
  return new URL(value, self.registration.scope).searchParams.get(name);
}

function absoluteUrl(value) {
  return new URL(value, self.registration.scope).href;
}

function markResubscribe() {
  return putPushFlag(true, "resubscribe");
}

function putPushFlag(value, key) {
  return new Promise((resolve) => {
    const request = self.indexedDB.open("vera-push", 1);
    request.onerror = () => resolve();
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains("flags")) {
        db.createObjectStore("flags");
      }
    };
    request.onsuccess = () => {
      const db = request.result;
      const tx = db.transaction("flags", "readwrite");
      tx.objectStore("flags").put(value, key);
      tx.oncomplete = () => {
        db.close();
        resolve();
      };
      tx.onerror = () => {
        db.close();
        resolve();
      };
    };
  });
}
