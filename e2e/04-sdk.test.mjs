import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { existsSync } from "node:fs";
import { execSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { WEBHOOK_URL, createProject, psql, uniqueName, usageStatistics, waitFor } from "./helpers.mjs";

// SDK real (dist compilado) contra um servidor fake com o contrato da API da OpenAI.
const sdkDir = fileURLToPath(new URL("../sdk-node/", import.meta.url));
if (!existsSync(new URL("../sdk-node/dist/index.js", import.meta.url))) {
  execSync("npm run build", { cwd: sdkDir, stdio: "inherit" });
}
const omniai = await import("../sdk-node/dist/index.js");

describe("SDK Node de ponta a ponta", () => {
  let server;
  let requests = [];
  let project;

  before(async () => {
    server = createServer((req, res) => {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        const parsed = JSON.parse(body);
        requests.push({ url: req.url, auth: req.headers.authorization, body: parsed });
        res.setHeader("Content-Type", "application/json");
        if (parsed.model === "modelo-inexistente") {
          res.statusCode = 404;
          res.end(JSON.stringify({ error: { message: "The model `modelo-inexistente` does not exist", type: "invalid_request_error", code: "model_not_found" } }));
          return;
        }
        res.end(JSON.stringify({
          id: "chatcmpl-e2e",
          object: "chat.completion",
          created: 1,
          model: parsed.model,
          choices: [{ index: 0, message: { role: "assistant", content: "resposta fake" }, finish_reason: "stop" }],
          usage: { prompt_tokens: 1000, completion_tokens: 500, total_tokens: 1500 },
        }));
      });
    });
    await new Promise((r) => server.listen(0, "127.0.0.1", r));
    process.env.OPENAI_BASE_URL = `http://127.0.0.1:${server.address().port}/v1`;
    project = await createProject(uniqueName("sdk"));
  });

  after(() => server?.close());

  const params = () => ({
    provider: "openai",
    project: project.projectName,
    apiKey: project.apiKey,
    providerApiKey: "sk-fake-provedor",
    webhookUrl: WEBHOOK_URL,
    messages: [{ role: "user", content: "ola" }],
  });

  it("chamada com sucesso: resposta intacta + metrica com custo calculado chega ao dashboard", async () => {
    requests = [];
    const result = await omniai.call({ ...params(), model: "gpt-4o-mini" });

    assert.equal(result.choices[0].message.content, "resposta fake");
    assert.equal(requests.length, 1);
    assert.equal(requests[0].auth, "Bearer sk-fake-provedor");
    assert.deepEqual(Object.keys(requests[0].body).sort(), ["messages", "model"]);

    const stats = await waitFor(async () => {
      const s = await usageStatistics({ project: project.projectName });
      return s.totalRequests === 1 ? s : null;
    }, { description: "metrica do SDK no dashboard" });
    assert.equal(stats.totalTokens, 1500);
    assert.ok(Math.abs(stats.totalCostUsd - 0.00045) < 1e-12, `custo ${stats.totalCostUsd}`);
  });

  it("erro do provedor: erro original propagado + metrica status=error registrada", async () => {
    await assert.rejects(
      () => omniai.call({ ...params(), model: "modelo-inexistente" }),
      (err) => err.status === 404 && /does not exist/.test(err.message),
    );

    const rows = await waitFor(() => {
      const r = psql(
        `select r."Status", r."TotalTokens", coalesce(r."CostUsd"::text, 'null') from usage_records r
         join projects p on p."Id" = r."ProjectId" where p."Name" = '${project.projectName}' and r."Model" = 'modelo-inexistente'`,
      );
      return r.length ? r : null;
    }, { description: "metrica de erro no banco" });
    assert.deepEqual(rows[0], ["error", "0", "null"]);
  });

  it("webhook fora do ar nao afeta a chamada da aplicacao", async () => {
    const originalError = console.error;
    console.error = () => {};
    try {
      const result = await omniai.call({ ...params(), model: "gpt-4o", webhookUrl: "http://127.0.0.1:1" });
      assert.equal(result.id, "chatcmpl-e2e");
    } finally {
      await new Promise((r) => setTimeout(r, 300));
      console.error = originalError;
    }
  });
});
