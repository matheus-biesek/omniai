import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { COCKPIT_URL, DASHBOARD_PASSWORD, DASHBOARD_USERNAME, createProject, gql, login, uniqueName } from "./helpers.mjs";

describe("Cockpit (nginx)", () => {
  it("serve o index.html", async () => {
    const res = await fetch(COCKPIT_URL);
    assert.equal(res.status, 200);
    assert.match(await res.text(), /<div id="root">/);
  });

  it("faz fallback de rotas da SPA para o index.html", async () => {
    for (const path of ["/login", "/dashboard", "/projects", "/qualquer/coisa"]) {
      const res = await fetch(`${COCKPIT_URL}${path}`);
      assert.equal(res.status, 200, path);
      assert.match(await res.text(), /<div id="root">/, path);
    }
  });
});

describe("API GraphQL - autenticacao", () => {
  it("ping responde sem autenticacao", async () => {
    const { data } = await gql("{ ping }");
    assert.equal(data.ping, "pong");
  });

  it("login com credenciais corretas emite JWT", async () => {
    // login direto, sem o cache do helper, para checar a expiracao de um token recem-emitido
    const { data, errors } = await gql(
      "mutation($u: String!, $p: String!) { login(username: $u, password: $p) { token } }",
      { u: DASHBOARD_USERNAME, p: DASHBOARD_PASSWORD },
    );
    assert.equal(errors, undefined, JSON.stringify(errors));
    const token = data.login.token;
    assert.equal(token.split(".").length, 3);
    const claims = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString());
    assert.equal(claims.sub, "admin");
    assert.equal(claims.iss, "OmniAI");
    assert.equal(claims.aud, "OmniAI.Dashboard");
    const ttlMin = (claims.exp * 1000 - Date.now()) / 60000;
    assert.ok(ttlMin > 58 && ttlMin <= 60, `expiracao inesperada: ${ttlMin} min`);
  });

  it("login errado retorna a MESMA mensagem generica para usuario ou senha errados", async () => {
    const q = "mutation($u: String!, $p: String!) { login(username: $u, password: $p) { token } }";
    const senhaErrada = await gql(q, { u: "admin", p: "errada" });
    const usuarioErrado = await gql(q, { u: "root", p: "admin-dev-password" });
    assert.equal(senhaErrada.errors[0].message, "Usuário ou senha inválidos.");
    assert.equal(usuarioErrado.errors[0].message, "Usuário ou senha inválidos.");
    assert.equal(senhaErrada.data?.login ?? null, null);
  });

  for (const [nome, query] of [
    ["usageStatistics", "{ usageStatistics { totalRequests } }"],
    ["projects", "{ projects { id } }"],
    ["createProject", 'mutation { createProject(name: "hack") { apiKey } }'],
    ["createApiKey", 'mutation { createApiKey(projectId: "00000000-0000-0000-0000-000000000000") { apiKey } }'],
    ["revokeApiKey", 'mutation { revokeApiKey(apiKeyId: "00000000-0000-0000-0000-000000000000") }'],
  ]) {
    it(`${nome} exige autenticacao`, async () => {
      const semToken = await gql(query);
      assert.equal(semToken.errors?.[0]?.message, "Autenticação necessária.");

      const tokenInvalido = await gql(query, {}, "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhZG1pbiJ9.assinatura-falsa");
      assert.equal(tokenInvalido.errors?.[0]?.message, "Autenticação necessária.");
    });
  }

  it("nenhum projeto foi criado pelas tentativas nao autenticadas", async () => {
    const { data } = await gql("{ projects { name } }", {}, await login());
    assert.ok(!data.projects.some((p) => p.name === "hack"));
  });
});

describe("API GraphQL - projetos e chaves", () => {
  it("createProject retorna a chave em texto plano uma unica vez e projects lista so metadados", async () => {
    const name = uniqueName("api-proj");
    const created = await createProject(name);
    assert.equal(created.projectName, name);
    assert.match(created.apiKey, /^ombk_[A-Za-z0-9]{30,}$/);

    // replica: pode levar alguns ms
    let project;
    for (let i = 0; i < 40 && !project; i++) {
      const { data } = await gql("{ projects { id name apiKeys { id createdAt revokedAt } } }", {}, await login());
      project = data.projects.find((p) => p.name === name);
      if (!project) await new Promise((r) => setTimeout(r, 100));
    }
    assert.ok(project, "projeto nao apareceu em projects");
    assert.equal(project.id, created.projectId);
    assert.equal(project.apiKeys.length, 1);
    assert.equal(project.apiKeys[0].revokedAt, null);

    const raw = JSON.stringify(project);
    assert.ok(!raw.includes(created.apiKey), "listagem nao pode expor a chave");
  });

  it("nome duplicado retorna erro claro", async () => {
    const name = uniqueName("dup");
    await createProject(name);
    const { errors } = await gql(
      "mutation($n: String!) { createProject(name: $n) { apiKey } }",
      { n: name },
      await login(),
    );
    assert.equal(errors[0].message, `Já existe um projeto chamado '${name}'.`);
  });

  it("createApiKey para projeto inexistente retorna erro claro", async () => {
    const id = "11111111-1111-1111-1111-111111111111";
    const { errors } = await gql(`mutation { createApiKey(projectId: "${id}") { apiKey } }`, {}, await login());
    assert.equal(errors[0].message, `Projeto '${id}' não encontrado.`);
  });

  it("revokeApiKey para chave inexistente retorna erro claro", async () => {
    const id = "22222222-2222-2222-2222-222222222222";
    const { errors } = await gql(`mutation { revokeApiKey(apiKeyId: "${id}") }`, {}, await login());
    assert.equal(errors[0].message, `API Key '${id}' não encontrada.`);
  });

  it("usageStatistics aceita filtro de periodo (from/to) sem erro", async () => {
    const { data, errors } = await gql(
      `{ usageStatistics(filter: { from: "2020-01-01T00:00:00Z", to: "2100-01-01T00:00:00Z" }) { totalRequests } }`,
      {},
      await login(),
    );
    assert.equal(errors, undefined, JSON.stringify(errors));
    assert.equal(typeof data.usageStatistics.totalRequests, "number");
  });
});
