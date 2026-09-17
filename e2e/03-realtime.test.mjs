import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import * as signalR from "@microsoft/signalr";
import { HUB_URL, createProject, login, metric, postMetric, uniqueName, waitFor } from "./helpers.mjs";

function connection(token) {
  const builder = new signalR.HubConnectionBuilder().configureLogging(signalR.LogLevel.None);
  return (token === undefined
    ? builder.withUrl(HUB_URL)
    : builder.withUrl(HUB_URL, { accessTokenFactory: () => token })
  ).build();
}

describe("Hub SignalR (Consumer)", () => {
  let project;
  let conn;
  const received = [];

  before(async () => {
    project = await createProject(uniqueName("realtime"));
    conn = connection(await login());
    conn.on("UsageReceived", (e) => received.push(e));
    await conn.start();
  });

  after(async () => {
    await conn?.stop();
  });

  it("recusa conexao sem token", async () => {
    await assert.rejects(() => connection(undefined).start(), /401/);
  });

  it("recusa conexao com token invalido", async () => {
    await assert.rejects(() => connection("token.invalido.qualquer").start(), /401/);
  });

  it("entrega UsageReceived com os dados do evento persistido", async () => {
    const model = uniqueName("model-rt");
    const sentAt = Date.now();
    assert.equal((await postMetric(project.apiKey, metric({ model, costUsd: 0.25, totalTokens: 42 }))).status, 202);

    const event = await waitFor(() => received.find((e) => e.model === model), { description: "evento no Hub" });
    const latencyMs = Date.now() - sentAt;

    assert.equal(event.project, project.projectName);
    assert.equal(event.projectId, project.projectId);
    assert.equal(event.provider, "openai");
    assert.equal(event.totalTokens, 42);
    assert.equal(event.costUsd, 0.25);
    assert.equal(event.status, "success");
    assert.ok(Number.isInteger(event.id) && event.id > 0);
    assert.ok(!Number.isNaN(Date.parse(event.occurredAt)));
    assert.ok(latencyMs < 5000, `evento demorou ${latencyMs}ms`);
  });
});
