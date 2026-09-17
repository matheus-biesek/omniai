import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { DASHBOARD_PASSWORD, DASHBOARD_USERNAME, gql } from "./helpers.mjs";

// Roda perto do fim: esgota as tentativas de login deste IP e o login fica bloqueado ate a janela
// virar (60s por padrao). Os testes anteriores reaproveitam um token em cache, entao nao sao afetados.
const LOGIN = "mutation($u: String!, $p: String!) { login(username: $u, password: $p) { token } }";
const BLOQUEADO = "Muitas tentativas de login. Aguarde um minuto e tente novamente.";

describe("API GraphQL: limite de tentativas de login por IP", () => {
  it("depois de muitas tentativas, bloqueia o login - inclusive com a senha certa", async () => {
    const mensagens = [];
    for (let i = 0; i < 30; i++) {
      const { errors } = await gql(LOGIN, { u: DASHBOARD_USERNAME, p: `chute-${i}` });
      mensagens.push(errors?.[0]?.message);
    }

    const primeiroBloqueio = mensagens.indexOf(BLOQUEADO);
    assert.ok(primeiroBloqueio >= 0, `30 tentativas erradas seguidas e nenhuma foi bloqueada: ${[...new Set(mensagens)]}`);
    assert.ok(primeiroBloqueio <= 10, `bloqueou so na tentativa ${primeiroBloqueio + 1}`);
    assert.ok(mensagens.slice(primeiroBloqueio).every((m) => m === BLOQUEADO));

    const comSenhaCerta = await gql(LOGIN, { u: DASHBOARD_USERNAME, p: DASHBOARD_PASSWORD });
    assert.equal(comSenhaCerta.errors?.[0]?.message, BLOQUEADO, "senha certa passou durante o bloqueio - o limite nao protege contra forca bruta");
    assert.equal(comSenhaCerta.data?.login ?? null, null);
  });
});
