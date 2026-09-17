import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { createProject, docker, metric, postMetric, redisCli, uniqueName, usageStatistics, waitFor } from "./helpers.mjs";

describe("Resiliencia", () => {
  it("Consumer parado: Webhook continua aceitando, e nada se perde quando o Consumer volta", async () => {
    const project = await createProject(uniqueName("resiliencia"));

    docker("stop", "omniai-consumer");
    try {
      for (let i = 0; i < 3; i++) {
        assert.equal((await postMetric(project.apiKey, metric({ totalTokens: 10 }))).status, 202);
      }
      assert.ok(Number(redisCli("XLEN", "usage-events")) >= 3);
      assert.equal((await usageStatistics({ project: project.projectName })).totalRequests, 0);
    } finally {
      docker("start", "omniai-consumer");
    }

    const stats = await waitFor(async () => {
      const s = await usageStatistics({ project: project.projectName });
      return s.totalRequests === 3 ? s : null;
    }, { timeoutMs: 60000, intervalMs: 1000, description: "3 eventos processados apos o Consumer voltar" });
    assert.equal(stats.totalTokens, 30);

    // Entradas processadas saem da stream - senao o XLEN do backpressure so cresce e o Webhook
    // passa a recusar tudo ao atingir MaxQueueSize.
    await waitFor(() => redisCli("XLEN", "usage-events") === "0", { description: "stream do Redis esvaziar" });
  });

  it("replica de leitura esta em modo standby (replicacao nativa ativa)", () => {
    const out = docker("exec", "-e", "PGPASSWORD=omniai_dev_password", "omniai-postgres-replica",
      "psql", "-h", "localhost", "-U", "omniai", "-d", "omniai", "-tAc", "select pg_is_in_recovery()");
    assert.equal(out, "t");
  });
});
