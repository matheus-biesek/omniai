import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

export const GRAPHQL_URL = process.env.E2E_GRAPHQL_URL ?? "http://localhost:5300/graphql";
export const WEBHOOK_URL = process.env.E2E_WEBHOOK_URL ?? "http://localhost:5100";
export const HUB_URL = process.env.E2E_HUB_URL ?? "http://localhost:5200/hub";
export const COCKPIT_URL = process.env.E2E_COCKPIT_URL ?? "http://localhost:8080";
export const DASHBOARD_USERNAME = process.env.E2E_DASHBOARD_USERNAME ?? "admin";
export const DASHBOARD_PASSWORD = process.env.E2E_DASHBOARD_PASSWORD ?? "admin-dev-password";

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export function uniqueName(prefix) {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
}

export async function gql(query, variables = {}, token = null) {
  const res = await fetch(GRAPHQL_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ query, variables }),
  });
  const body = await res.json();
  return { status: res.status, data: body.data, errors: body.errors };
}

// Cada arquivo de teste roda num processo separado. O token fica num arquivo temporario para nao
// gastar uma tentativa de login por arquivo - a API limita tentativas de login por IP.
const TOKEN_CACHE = join(tmpdir(), "omniai-e2e-token.json");
let cachedToken = null;
export async function login() {
  if (cachedToken) return cachedToken;

  try {
    const { token, expiresAt } = JSON.parse(readFileSync(TOKEN_CACHE, "utf8"));
    if (expiresAt - Date.now() > 5 * 60_000 && (await gql("{ projects { id } }", {}, token)).errors === undefined) {
      return (cachedToken = token);
    }
  } catch {
    // sem cache valido - faz login
  }

  const { data, errors } = await gql(
    "mutation($u: String!, $p: String!) { login(username: $u, password: $p) { token } }",
    { u: DASHBOARD_USERNAME, p: DASHBOARD_PASSWORD },
  );
  if (errors) throw new Error(`login falhou: ${JSON.stringify(errors)}`);
  cachedToken = data.login.token;
  const { exp } = JSON.parse(Buffer.from(cachedToken.split(".")[1], "base64url").toString());
  writeFileSync(TOKEN_CACHE, JSON.stringify({ token: cachedToken, expiresAt: exp * 1000 }));
  return cachedToken;
}

export async function createProject(name = uniqueName("e2e")) {
  const token = await login();
  const { data, errors } = await gql(
    "mutation($n: String!) { createProject(name: $n) { projectId projectName apiKey } }",
    { n: name },
    token,
  );
  if (errors) throw new Error(`createProject falhou: ${JSON.stringify(errors)}`);
  return data.createProject;
}

export async function usageStatistics(filter) {
  const token = await login();
  const { data, errors } = await gql(
    `query($f: UsageStatisticsFilterInput) {
      usageStatistics(filter: $f) {
        totalCostUsd totalTokens totalRequests
        byProvider { provider costUsd tokens requests }
        byProject { project costUsd tokens requests }
      }
    }`,
    { f: filter },
    token,
  );
  if (errors) throw new Error(`usageStatistics falhou: ${JSON.stringify(errors)}`);
  return data.usageStatistics;
}

export function metric(overrides = {}) {
  return {
    project: "ignorado-pelo-webhook",
    provider: "openai",
    model: "gpt-4o-mini",
    promptTokens: 1000,
    completionTokens: 500,
    totalTokens: 1500,
    costUsd: 0.00045,
    latencyMs: 321,
    status: "success",
    timestamp: new Date().toISOString(),
    ...overrides,
  };
}

export async function postMetric(apiKey, body, { rawBody } = {}) {
  const res = await fetch(`${WEBHOOK_URL}/events`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(apiKey !== undefined ? { "X-Api-Key": apiKey } : {}),
    },
    body: rawBody ?? JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, text };
}

export async function waitFor(fn, { timeoutMs = 15000, intervalMs = 250, description = "condicao" } = {}) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await fn();
      if (last) return last;
    } catch (err) {
      last = err;
    }
    await sleep(intervalMs);
  }
  throw new Error(`Timeout esperando ${description}. Ultimo valor: ${last instanceof Error ? last.message : JSON.stringify(last)}`);
}

// Consulta direto no Postgres primary via docker exec - so pra inspecionar estado interno
// (ex: status do usage_event_logs), nunca pra montar dados de teste.
export function psql(sql) {
  const out = execFileSync(
    "docker",
    ["exec", "-e", "PGPASSWORD=omniai_dev_password", "omniai-postgres-primary",
      "psql", "-h", "localhost", "-U", "omniai", "-d", "omniai", "-tA", "-F", "|", "-c", sql],
    { encoding: "utf8" },
  );
  return out.trim().split("\n").filter(Boolean).map((line) => line.split("|"));
}

export function redisCli(...args) {
  return execFileSync("docker", ["exec", "omniai-redis", "redis-cli", ...args], { encoding: "utf8" }).trim();
}

export function docker(...args) {
  return execFileSync("docker", args, { encoding: "utf8" }).trim();
}

export function eventLogsByModel(model) {
  return psql(
    `select "Status", "AttemptCount", coalesce("LastError", '') from usage_event_logs where "Payload" like '%${model}%'`,
  ).map(([status, attempts, lastError]) => ({ status: Number(status), attempts: Number(attempts), lastError }));
}

export const EventLogStatus = { Pendente: 0, Processado: 1, FalhaTransiente: 2, FalhaPermanente: 3 };
