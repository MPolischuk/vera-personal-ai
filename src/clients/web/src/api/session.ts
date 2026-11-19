import { getApiBaseUrl, getBearerToken } from "./rest";

let memoryToken = "";
let refreshInFlight: Promise<boolean> | null = null;
const listeners = new Set<() => void>();

export function getAccessToken(): string {
  const baked = getBearerToken();
  if (baked) {
    return baked;
  }
  return memoryToken;
}

export function hasSession(): boolean {
  return getAccessToken().length > 0;
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function clearSession(): void {
  if (!memoryToken) {
    return;
  }
  memoryToken = "";
  emit();
}

export async function login(password: string, deviceName: string): Promise<void> {
  const response = await fetch(`${getApiBaseUrl()}/api/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password, deviceName }),
  });
  if (!response.ok) {
    throw new Error("No se pudo entrar.");
  }

  const body = (await response.json()) as { accessToken?: string };
  if (!body.accessToken) {
    throw new Error("No se pudo entrar.");
  }

  memoryToken = body.accessToken;
  emit();
}

export function refresh(): Promise<boolean> {
  if (getBearerToken()) {
    return Promise.resolve(false);
  }
  if (!refreshInFlight) {
    refreshInFlight = performRefresh().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

async function performRefresh(): Promise<boolean> {
  const response = await fetch(`${getApiBaseUrl()}/api/auth/refresh`, {
    method: "POST",
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    memoryToken = "";
    emit();
    return false;
  }

  const body = (await response.json()) as { accessToken?: string };
  if (!body.accessToken) {
    memoryToken = "";
    emit();
    return false;
  }

  memoryToken = body.accessToken;
  emit();
  return true;
}

function emit(): void {
  for (const listener of listeners) {
    listener();
  }
}
