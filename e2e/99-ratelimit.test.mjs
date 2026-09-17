import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { WEBHOOK_URL, metric } from "./helpers.mjs";

// Roda por ultimo: esgota a janela de rate limit do IP (100 req / 60s) e o Webhook fica
// recusando requisicoes deste IP ate a janela virar.
describe("Webhook: rate limit por IP", () => {
  it("apos o limite da janela, responde 429 Too Many Requests (distinguivel de fila cheia/503)", async () => {
    const statuses = [];
    for (let i = 0; i < 130; i++) {
      const res = await fetch(`${WEBHOOK_URL}/events`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Api-Key": "ombk_invalida" },
        body: JSON.stringify(metric()),
      });
      statuses.push(res.status);
    }

    const firstRejected = statuses.findIndex((s) => s !== 401);
    // Requisicoes de outros testes na mesma janela tambem contam, entao o corte pode vir antes da 100a.
    assert.ok(firstRejected >= 0 && firstRejected <= 100, `nenhuma requisicao foi limitada: ${[...new Set(statuses)]}`);
    const rejected = statuses.slice(firstRejected);
    assert.ok(rejected.every((s) => s === rejected[0]), `status misturados apos o limite: ${[...new Set(rejected)]}`);
    assert.equal(rejected[0], 429, `rate limit respondeu ${rejected[0]}`);
  });
});
