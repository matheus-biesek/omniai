import { useMemo, useState } from "react";
import { useUsageStatistics } from "../../features/usage-stats/useUsageStatistics";
import { Card } from "../../shared/ui/Card";
import { AppShell } from "../../app/AppShell";

function formatCurrency(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "USD" });
}

function formatNumber(value: number): string {
  return value.toLocaleString("pt-BR");
}

export function DashboardPage() {
  const [project, setProject] = useState("");
  const [provider, setProvider] = useState("");
  const filter = useMemo(
    () => ({ project: project || undefined, provider: provider || undefined }),
    [project, provider],
  );
  const { data, loading, error } = useUsageStatistics(filter);

  return (
    <AppShell>
      <div className="filters">
        <label className="field">
          <span className="field-label">Projeto</span>
          <input
            className="field-input"
            value={project}
            onChange={(e) => setProject(e.target.value)}
            placeholder="Todos"
          />
        </label>
        <label className="field">
          <span className="field-label">Provedor</span>
          <input
            className="field-input"
            value={provider}
            onChange={(e) => setProvider(e.target.value)}
            placeholder="Todos"
          />
        </label>
      </div>

      {loading && !data ? <p>Carregando...</p> : null}
      {error ? <p className="form-error">{error}</p> : null}

      {data ? (
        <>
          <div className="stat-grid">
            <Card title="Custo total">
              <p className="stat-value">{formatCurrency(data.totalCostUsd)}</p>
            </Card>
            <Card title="Tokens totais">
              <p className="stat-value">{formatNumber(data.totalTokens)}</p>
            </Card>
            <Card title="Requisições">
              <p className="stat-value">{formatNumber(data.totalRequests)}</p>
            </Card>
          </div>

          <div className="table-grid">
            <Card title="Por provedor">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Provedor</th>
                    <th>Custo</th>
                    <th>Tokens</th>
                    <th>Requisições</th>
                  </tr>
                </thead>
                <tbody>
                  {data.byProvider.map((row) => (
                    <tr key={row.provider}>
                      <td>{row.provider}</td>
                      <td>{formatCurrency(row.costUsd)}</td>
                      <td>{formatNumber(row.tokens)}</td>
                      <td>{formatNumber(row.requests)}</td>
                    </tr>
                  ))}
                  {data.byProvider.length === 0 ? (
                    <tr>
                      <td colSpan={4}>Nenhum dado ainda.</td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </Card>

            <Card title="Por projeto">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Projeto</th>
                    <th>Custo</th>
                    <th>Tokens</th>
                    <th>Requisições</th>
                  </tr>
                </thead>
                <tbody>
                  {data.byProject.map((row) => (
                    <tr key={row.project}>
                      <td>{row.project}</td>
                      <td>{formatCurrency(row.costUsd)}</td>
                      <td>{formatNumber(row.tokens)}</td>
                      <td>{formatNumber(row.requests)}</td>
                    </tr>
                  ))}
                  {data.byProject.length === 0 ? (
                    <tr>
                      <td colSpan={4}>Nenhum dado ainda.</td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </Card>
          </div>
        </>
      ) : null}
    </AppShell>
  );
}
