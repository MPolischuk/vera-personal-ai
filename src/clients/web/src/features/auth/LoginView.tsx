import { useState, type FormEvent } from "react";
import { login } from "../../api/session";

export function LoginView() {
  const [password, setPassword] = useState("");
  const [deviceName, setDeviceName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPending(true);
    setError(null);
    try {
      await login(password, deviceName);
    } catch {
      setError("No se pudo entrar.");
    } finally {
      setPending(false);
    }
  }

  return (
    <form
      onSubmit={(event) => void onSubmit(event)}
      className="w-full max-w-md space-y-4 rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-6 shadow-sm"
    >
      <div>
        <p className="text-xs tracking-[0.14em] text-[var(--muted)] uppercase">
          Asistente personal
        </p>
        <h1 className="text-lg font-semibold">Entrar</h1>
      </div>

      <label className="block space-y-1 text-sm">
        <span>Contraseña</span>
        <input
          type="password"
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          className="w-full rounded-lg border border-[var(--line)] bg-white px-3 py-2"
        />
      </label>

      <label className="block space-y-1 text-sm">
        <span>Nombre del dispositivo</span>
        <input
          type="text"
          name="deviceName"
          autoComplete="off"
          value={deviceName}
          onChange={(event) => setDeviceName(event.target.value)}
          className="w-full rounded-lg border border-[var(--line)] bg-white px-3 py-2"
        />
      </label>

      {error && <p className="text-sm text-[var(--danger)]">{error}</p>}

      <button
        type="submit"
        disabled={pending}
        className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm text-white disabled:opacity-50"
      >
        Entrar
      </button>
    </form>
  );
}
