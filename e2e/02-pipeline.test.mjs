import { before, describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  EventLogStatus,
  createProject,
  eventLogsByModel,
  gql,
  login,
  metric,
  postMetric,
  redisCli,
  uniqueName,
  usageStatistics,
  waitFor,
} from "./helpers.mjs";

describe("Pipeline SDK -> Webhook -> Redis -> Consumer -> Postgres -> Replica -> GraphQL", () => {
  let project;

  before(async () => {
    project = await createProject(uniqueName("pipeline"));
  });

  describe("Webhook: autenticacao e validacao", () => {
    it("sem X-Api-Key -> 401", async () => {
      assert.equal((await postMetric(undefined, metric())).status, 401);
    });

    it("X-Api-Key desconhecida -> 401", async () => {
      assert.equal((await postMetric("ombk_nao-existe", metric())).status, 401);
    });

    it("payload invalido -> 400 com detalhes dos campos", async () => {
      const res = await postMetric(project.apiKey, metric({ provider: "gemini", promptTokens: -1, costUsd: -5, status: "ok" }));
      assert.equal(res.status, 400);
      const body = JSON.parse(res.text);
      for (const field of ["Provider", "PromptTokens", "CostUsd", "Status"]) {
        assert.ok(body.errors[field], `esperava erro em ${field}: ${res.text}`);
      }
    });

    it("JSON malformado -> 400", async () => {
      assert.equal((await postMetric(project.apiKey, null, { rawBody: "{nao-e-json" })).status, 400);
    });

    it('"provider": null -> 400 (nao 500)', async () => {
      const res = await postMetric(project.apiKey, metric({ provider: null }));
      assert.equal(res.status, 400, `recebeu ${res.status}: ${res.text.slice(0, 200)}`);
    });

    it('"status": null -> 400 (nao 500)', async () => {
      const res = await postMetric(project.apiKey, metric({ status: null }));
      assert.equal(res.status, 400, `recebeu ${res.status}: ${res.text.slice(0, 200)}`);
    });

    it("sem timestamp -> 400 (campo obrigatorio pela doc do SDK/Webhook)", async () => {
      const body = metric();
      delete body.timestamp;
      const res = await postMetric(project.apiKey, body);
      assert.equal(res.status, 400, `recebeu ${res.status}: evento sem data foi aceito`);
    });
  });

  describe("Fluxo feliz", () => {
    it("eventos aceitos (202) aparecem agregados corretamente na API", async () => {
      // Projeto proprio: os testes de validacao acima podem deixar eventos aceitos indevidamente
      // no projeto compartilhado, o que distorceria a contagem.
      const project = await createProject(uniqueName("pipeline-feliz"));
      const events = [
        metric({ model: "gpt-4o-mini", promptTokens: 1000, completionTokens: 500, totalTokens: 1500, costUsd: 0.00045 }),
        metric({ model: "gpt-4o", promptTokens: 100, completionTokens: 100, totalTokens: 200, costUsd: 0.00125 }),
        metric({ provider: "anthropic", model: "claude-x", promptTokens: 10, completionTokens: 5, totalTokens: 15, costUsd: null }),
        metric({ status: "error", promptTokens: 0, completionTokens: 0, totalTokens: 0, costUsd: null }),
      ];

      for (const e of events) {
        const res = await postMetric(project.apiKey, e);
        assert.equal(res.status, 202, res.text);
      }

      const stats = await waitFor(
        async () => {
          const s = await usageStatistics({ project: project.projectName });
          return s.totalRequests === 4 ? s : null;
        },
        { description: "4 requisicoes do projeto na replica" },
      );

      assert.equal(stats.totalTokens, 1715);
      assert.ok(Math.abs(stats.totalCostUsd - 0.0017) < 1e-12, `custo total ${stats.totalCostUsd}`);
      assert.deepEqual(stats.byProject, [{ project: project.projectName, costUsd: stats.totalCostUsd, tokens: 1715, requests: 4 }]);

      const openai = stats.byProvider.find((p) => p.provider === "openai");
      const anthropic = stats.byProvider.find((p) => p.provider === "anthropic");
      assert.equal(openai.requests, 3);
      assert.equal(openai.tokens, 1700);
      assert.equal(anthropic.requests, 1);
      assert.equal(anthropic.costUsd, 0);

      const soAnthropic = await usageStatistics({ project: project.projectName, provider: "anthropic" });
      assert.equal(soAnthropic.totalRequests, 1);
    });

    it("projeto do evento vem da API Key, nunca do corpo", async () => {
      const outro = await createProject(uniqueName("vitima"));
      const model = uniqueName("model-spoof");
      const res = await postMetric(project.apiKey, metric({ project: outro.projectName, model }));
      assert.equal(res.status, 202);

      await waitFor(() => eventLogsByModel(model).some((l) => l.status === EventLogStatus.Processado), {
        description: "evento processado",
      });
      const doOutro = await usageStatistics({ project: outro.projectName });
      assert.equal(doOutro.totalRequests, 0, "evento foi contabilizado no projeto informado no corpo!");
    });

    it("usage_event_logs guarda o payload cru e o Redis fica sem pendencias", async () => {
      const model = uniqueName("model-log");
      assert.equal((await postMetric(project.apiKey, metric({ model }))).status, 202);

      const [log] = await waitFor(
        () => {
          const logs = eventLogsByModel(model);
          return logs.length && logs[0].status === EventLogStatus.Processado ? logs : null;
        },
        { description: "log processado" },
      );
      assert.equal(log.attempts, 0);

      const pending = redisCli("XPENDING", "usage-events", "consumer-group");
      assert.match(pending, /^0/, `pendencias no consumer group: ${pending}`);
    });
  });

  describe("Revogacao e rotacao de chaves", () => {
    it("chave revogada para de funcionar na hora; chave nova do mesmo projeto continua", async () => {
      const token = await login();
      const p = await createProject(uniqueName("rotacao"));
      const nova = (await gql(
        "mutation($id: UUID!) { createApiKey(projectId: $id) { apiKeyId apiKey } }",
        { id: p.projectId },
        token,
      )).data.createApiKey;
      assert.notEqual(nova.apiKey, p.apiKey);

      assert.equal((await postMetric(p.apiKey, metric())).status, 202);
      assert.equal((await postMetric(nova.apiKey, metric())).status, 202);

      // id da chave original via listagem (replica)
      const original = await waitFor(async () => {
        const { data } = await gql("{ projects { name apiKeys { id } } }", {}, token);
        const proj = data.projects.find((x) => x.name === p.projectName);
        return proj?.apiKeys.find((k) => k.id !== nova.apiKeyId);
      }, { description: "chave original na listagem" });

      const revoke = await gql("mutation($id: UUID!) { revokeApiKey(apiKeyId: $id) }", { id: original.id }, token);
      assert.equal(revoke.data.revokeApiKey, true);

      assert.equal((await postMetric(p.apiKey, metric())).status, 401, "chave revogada ainda aceita");
      assert.equal((await postMetric(nova.apiKey, metric())).status, 202);

      const again = await gql("mutation($id: UUID!) { revokeApiKey(apiKeyId: $id) }", { id: original.id }, token);
      assert.equal(again.data.revokeApiKey, true, "revogar de novo deveria ser idempotente");

      const revokedAt = await waitFor(async () => {
        const { data } = await gql("{ projects { name apiKeys { id revokedAt } } }", {}, token);
        return data.projects.find((x) => x.name === p.projectName).apiKeys.find((k) => k.id === original.id).revokedAt;
      }, { description: "revokedAt na listagem" });
      assert.ok(!Number.isNaN(Date.parse(revokedAt)));
    });
  });

  describe("Consumer: datas e payloads anomalos", () => {
    for (const [nome, timestamp] of [
      ["sem fuso (\"2026-09-16T10:00:00\")", "2026-09-16T10:00:00"],
      ["com offset (\"2026-09-16T10:00:00-03:00\")", "2026-09-16T10:00:00-03:00"],
    ]) {
      it(`timestamp ${nome} aceito com 202 tambem e persistido`, async () => {
        const model = uniqueName("model-ts");
        const res = await postMetric(project.apiKey, metric({ model, timestamp }));
        assert.equal(res.status, 202);

        const logs = await waitFor(() => {
          const l = eventLogsByModel(model);
          return l.length && l[0].status !== EventLogStatus.Pendente ? l : null;
        }, { description: "evento sair de Pendente" });

        assert.equal(
          logs[0].status,
          EventLogStatus.Processado,
          `Webhook respondeu 202 mas o Consumer nao conseguiu gravar: status=${logs[0].status} erro="${logs[0].lastError}"`,
        );
      });
    }

    it("payload corrompido na fila vira FalhaPermanente na primeira tentativa (sem retry)", async () => {
      const marker = uniqueName("corrompido");
      redisCli("XADD", "usage-events", "*", "payload", `{"Project": "${marker}", quebrado`);

      const logs = await waitFor(() => {
        const l = eventLogsByModel(marker);
        return l.length && l[0].status !== EventLogStatus.Pendente ? l : null;
      }, { description: "payload corrompido sair de Pendente" });

      assert.equal(
        logs[0].status,
        EventLogStatus.FalhaPermanente,
        `status=${logs[0].status} tentativas=${logs[0].attempts} erro="${logs[0].lastError}" - deveria ser permanente`,
      );
      assert.equal(logs[0].attempts, 0);
    });

    it("evento de projeto inexistente vira FalhaPermanente na primeira tentativa", async () => {
      const marker = uniqueName("fantasma");
      const payload = JSON.stringify({
        Project: marker, Provider: "openai", Model: "m", PromptTokens: 1, CompletionTokens: 1, TotalTokens: 2,
        CostUsd: null, LatencyMs: 1, Status: "success", Timestamp: new Date().toISOString(),
      });
      redisCli("XADD", "usage-events", "*", "payload", payload);

      const logs = await waitFor(() => {
        const l = eventLogsByModel(marker);
        return l.length && l[0].status !== EventLogStatus.Pendente ? l : null;
      }, { description: "evento fantasma sair de Pendente" });

      assert.equal(logs[0].status, EventLogStatus.FalhaPermanente);
      assert.match(logs[0].lastError, /nao encontrado/);
    });
  });
});
