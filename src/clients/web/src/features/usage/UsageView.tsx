import { useEffect } from "react";
import { useAppStore } from "../../store/appStore";

function formatUsd(value: number): string {
  return new Intl.NumberFormat("es-AR", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
  }).format(value);
}

export function UsageView() {
  const usage = useAppStore((s) => s.usage);
  const loadUsage = useAppStore((s) => s.loadUsage);

  useEffect(() => {
    void loadUsage();
  }, [loadUsage]);

  if (!usage) {
    return (
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 text-sm text-[var(--muted)] shadow-sm">
        Cargando uso…
      </div>
    );
  }

  const budget = usage.monthlyBudgetUsd;
  const spent = usage.estimatedCostUsd;
  const percent = budget > 0 ? Math.min(100, (spent / budget) * 100) : 0;
  const warn = budget > 0 && percent >= usage.warnAtPercent;

  return (
    <div className="h-full min-h-0 space-y-4 overflow-y-auto rounded-2xl border border-[var(--line)] bg-[var(--panel)] p-5 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">Uso y costos</h2>
          <p className="mt-1 text-sm text-[var(--muted)]">
            Desde {new Date(usage.since).toLocaleString("es-AR")}
          </p>
        </div>
        <button
          type="button"
          className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-sm"
          onClick={() => void loadUsage()}
        >
          Actualizar
        </button>
      </div>

      <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-xl bg-slate-50 p-3">
          <dt className="text-xs text-[var(--muted)] uppercase">Turnos</dt>
          <dd className="text-xl font-semibold">{usage.turns}</dd>
        </div>
        <div className="rounded-xl bg-slate-50 p-3">
          <dt className="text-xs text-[var(--muted)] uppercase">Tokens entrada</dt>
          <dd className="text-xl font-semibold">{usage.inputTokens}</dd>
        </div>
        <div className="rounded-xl bg-slate-50 p-3">
          <dt className="text-xs text-[var(--muted)] uppercase">Tokens salida</dt>
          <dd className="text-xl font-semibold">{usage.outputTokens}</dd>
        </div>
        <div className="rounded-xl bg-slate-50 p-3">
          <dt className="text-xs text-[var(--muted)] uppercase">Costo estimado</dt>
          <dd className="text-xl font-semibold">{formatUsd(spent)}</dd>
        </div>
      </dl>

      <div>
        <div className="mb-2 flex items-center justify-between text-sm">
          <span>
            Presupuesto mensual{" "}
            {budget > 0 ? formatUsd(budget) : "(sin tope configurado)"}
          </span>
          {budget > 0 && (
            <span className={warn ? "text-[var(--warn)] font-medium" : ""}>
              {percent.toFixed(1)}%
              {warn ? ` · aviso ≥ ${usage.warnAtPercent}%` : ""}
            </span>
          )}
        </div>
        <div className="h-3 overflow-hidden rounded-full bg-slate-100">
          <div
            className={`h-full rounded-full ${warn ? "bg-[var(--warn)]" : "bg-[var(--accent)]"}`}
            style={{ width: `${budget > 0 ? percent : 0}%` }}
          />
        </div>
      </div>

      <div>
        <h3 className="mb-2 text-sm font-semibold">Desglose por modelo</h3>
        {usage.byModel.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">Sin uso registrado en el período.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[32rem] text-left text-sm">
              <thead className="border-b border-[var(--line)] text-xs text-[var(--muted)] uppercase">
                <tr>
                  <th className="py-2 pr-3 font-medium">Proveedor</th>
                  <th className="py-2 pr-3 font-medium">Modelo</th>
                  <th className="py-2 pr-3 font-medium">In</th>
                  <th className="py-2 pr-3 font-medium">Out</th>
                  <th className="py-2 font-medium">Costo</th>
                </tr>
              </thead>
              <tbody>
                {usage.byModel.map((row) => (
                  <tr key={`${row.provider}:${row.model}`} className="border-b border-[var(--line)]">
                    <td className="py-2 pr-3">{row.provider}</td>
                    <td className="py-2 pr-3">{row.model}</td>
                    <td className="py-2 pr-3">{row.inputTokens}</td>
                    <td className="py-2 pr-3">{row.outputTokens}</td>
                    <td className="py-2">{formatUsd(row.estimatedCostUsd)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
