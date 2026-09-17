import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import { chromium } from "playwright-core";
import { COCKPIT_URL, DASHBOARD_PASSWORD, DASHBOARD_USERNAME, metric, postMetric, uniqueName } from "./helpers.mjs";

// Cockpit num navegador de verdade (Chrome/Edge ja instalado na maquina - playwright-core nao baixa
// navegador). Troque com E2E_BROWSER_CHANNEL=msedge, se preferir.
const channel = process.env.E2E_BROWSER_CHANNEL ?? "chrome";

describe("Cockpit no navegador", () => {
  let browser;
  let page;
  const consoleErrors = [];
  const projectName = uniqueName("browser");
  let apiKey;

  const stat = (title) => page.locator(".card", { has: page.locator("h3", { hasText: title }) }).locator(".stat-value");

  before(async () => {
    browser = await chromium.launch({ channel, headless: true });
    page = await browser.newPage();
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(msg.text());
    });
  });

  after(async () => {
    await browser?.close();
  });

  it("rota protegida sem sessao redireciona para /login", async () => {
    await page.goto(`${COCKPIT_URL}/projects`);
    await page.waitForURL("**/login");
  });

  it("login com senha errada mostra erro generico e continua no /login", async () => {
    await page.fill("#username", DASHBOARD_USERNAME);
    await page.fill("#password", "senha-errada");
    await page.click("button[type=submit]");
    await page.getByText("Usuário ou senha inválidos.").waitFor();
    assert.match(page.url(), /\/login$/);
  });

  it("login correto leva ao dashboard com os cards de estatistica", async () => {
    await page.fill("#password", DASHBOARD_PASSWORD);
    await page.click("button[type=submit]");
    await page.waitForURL("**/dashboard");
    await stat("Custo total").waitFor();
    await stat("Requisições").waitFor();
  });

  it("cria projeto pela tela e exibe a API Key uma vez", async () => {
    await page.getByRole("link", { name: "Projetos" }).click();
    await page.fill("#new-project-name", projectName);
    await page.getByRole("button", { name: "Criar projeto" }).click();

    const reveal = page.locator(".api-key-reveal");
    await reveal.waitFor();
    apiKey = (await reveal.textContent()).trim();
    assert.match(apiKey, /^ombk_/);
    await page.getByText("Copie esta chave agora. Ela não será exibida novamente.").waitFor();

    const card = page.locator(".card", { has: page.locator("h3", { hasText: projectName }) });
    await card.getByText("Ativa").waitFor();
  });

  it("dashboard atualiza EM TEMPO REAL (SignalR) quando chega um evento, sem recarregar", async () => {
    await page.getByRole("link", { name: "Dashboard" }).click();
    await page.getByPlaceholder("Todos").first().fill(projectName);
    await page.waitForFunction(
      () => [...document.querySelectorAll(".card")].some((c) => c.textContent.startsWith("Requisições") && c.querySelector(".stat-value")?.textContent === "0"),
    );
    await page.waitForTimeout(2000); // tempo para a conexao com o Hub abrir

    assert.equal((await postMetric(apiKey, metric({ totalTokens: 1234 }))).status, 202);

    try {
      await page.waitForFunction(
        () => [...document.querySelectorAll(".card")].some((c) => c.textContent.startsWith("Requisições") && c.querySelector(".stat-value")?.textContent === "1"),
        null,
        { timeout: 10000 },
      );
    } catch {
      assert.fail(`Dashboard nao atualizou em tempo real em 10s. Erros no console do navegador:\n${consoleErrors.join("\n")}`);
    }
  });

  it("apos recarregar, o historico (GraphQL) mostra o evento", async () => {
    await page.reload();
    await page.getByPlaceholder("Todos").first().fill(projectName);
    await page.waitForFunction(
      () => [...document.querySelectorAll(".card")].some((c) => c.textContent.startsWith("Requisições") && c.querySelector(".stat-value")?.textContent === "1"),
      null,
      { timeout: 10000 },
    );
    assert.equal(await stat("Tokens totais").textContent(), "1.234");
  });

  it("revoga a chave pela tela", async () => {
    await page.getByRole("link", { name: "Projetos" }).click();
    const card = page.locator(".card", { has: page.locator("h3", { hasText: projectName }) });
    await card.getByRole("button", { name: "Revogar" }).click();
    await card.getByText(/Revogada em/).waitFor();
    assert.equal((await postMetric(apiKey, metric())).status, 401);
  });

  it("sair encerra a sessao", async () => {
    await page.getByRole("button", { name: "Sair" }).click();
    await page.waitForURL("**/login");
    await page.goto(`${COCKPIT_URL}/dashboard`);
    await page.waitForURL("**/login");
  });
});
