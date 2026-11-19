import { ApiError, getApiBaseUrl, getBearerToken } from "../../api/rest";
import { clearSession, getAccessToken, refresh } from "../../api/session";

export interface PushSubscriptionInput {
  endpoint: string;
  keys: {
    p256dh: string;
    auth: string;
  };
}

export interface PushSubscriptionSummary {
  id: string;
  userAgentFamily: string | null;
  status: string;
  createdAt: string;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
}

async function request<T>(path: string, init?: RequestInit, retry = true): Promise<T> {
  const token = getAccessToken();
  if (!token) {
    clearSession();
    throw new ApiError("No se pudo entrar.", 401);
  }

  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${token}`,
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...(init?.headers ?? {}),
    },
  });

  if (response.status === 401 && retry && getBearerToken().length === 0) {
    if (await refresh()) {
      return request(path, init, false);
    }

    clearSession();
    throw new ApiError("No se pudo entrar.", 401);
  }

  if (!response.ok) {
    throw await readApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function readApiError(response: Response): Promise<ApiError> {
  let code: string | undefined;
  try {
    const body = (await response.json()) as { code?: unknown };
    if (typeof body.code === "string") {
      code = body.code;
    }
  } catch {
    code = undefined;
  }

  return new ApiError(`HTTP ${response.status}`, response.status, code);
}

export async function getVapidPublicKey(): Promise<string | null> {
  try {
    const body = await request<{ publicKey?: string }>("/api/push/vapid-public-key");
    const key = body.publicKey?.trim() ?? "";
    return key.length > 0 ? key : null;
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }

    throw error;
  }
}

export function putPushSubscription(body: PushSubscriptionInput): Promise<void> {
  return request("/api/push/subscriptions", {
    method: "PUT",
    body: JSON.stringify({ endpoint: body.endpoint, keys: body.keys }),
  });
}

export async function listPushSubscriptions(): Promise<PushSubscriptionSummary[]> {
  const body = await request<{ subscriptions?: PushSubscriptionSummary[] }>("/api/push/subscriptions");
  return body.subscriptions ?? [];
}

export function deleteCurrentPushSubscription(): Promise<void> {
  return request("/api/push/subscriptions/current", { method: "DELETE" });
}

export function sendTestPush(): Promise<void> {
  return request("/api/push/test", { method: "POST" });
}

export function pushErrorMessage(error: unknown, action: "sync" | "test" | "disable" = "sync"): string {
  if (error instanceof ApiError && error.code === "device_required") {
    return "Para activar las notificaciones tenés que iniciar sesión con un dispositivo.";
  }

  if (error instanceof ApiError && error.status === 404) {
    return "Las notificaciones push están apagadas en el servidor.";
  }

  if (action === "test") {
    return "No pude mandar la prueba.";
  }

  if (action === "disable") {
    return "No pude desactivar este dispositivo.";
  }

  return "No pude actualizar las notificaciones.";
}
