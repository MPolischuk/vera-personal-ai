export function formatPairingCode(raw: string): string {
  const compact = raw.replace(/[\s-]/g, "").toUpperCase();
  if (compact.length !== 8) {
    return raw.trim().toUpperCase();
  }

  return `${compact.slice(0, 4)}-${compact.slice(4)}`;
}

export function maskExternalId(externalId: string): string {
  const tail = externalId.slice(-4);
  return `…${tail}`;
}

export function approvePeerRequest(peerId: string, code: string): { path: string; body: { code: string } } {
  return {
    path: `/api/channels/peers/${peerId}/approve`,
    body: { code: formatPairingCode(code) },
  };
}

export function pairingErrorMessage(status: number, code?: string): string {
  if (code === "peer_limit_reached" || status === 409) {
    return "Ya hay un chat vinculado. Desvinculalo antes de aprobar otro.";
  }

  if (code === "pairing_code_expired" || status === 410) {
    return "Ese código ya venció. Esperá el nuevo en Telegram.";
  }

  if (status === 422) {
    return "El código no cierra. Revisalo.";
  }

  return "No pude guardar el cambio. Probá de nuevo.";
}
