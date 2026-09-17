import { beforeEach, describe, expect, it, vi } from "vitest";

const openAiState = vi.hoisted(() => ({
  constructorArgs: [] as unknown[],
  createArgs: [] as unknown[],
  impl: (async () => ({})) as (args: unknown) => Promise<unknown>,
}));

vi.mock("openai", () => ({
  OpenAI: class {
    chat = { completions: { create: (args: unknown) => { openAiState.createArgs.push(args); return openAiState.impl(args); } } };
    constructor(options: unknown) {
      openAiState.constructorArgs.push(options);
    }
  },
}));

const { call } = await import("../src/index.js");

const providerResponse = {
  id: "chatcmpl-1",
  object: "chat.completion",
  choices: [{ index: 0, message: { role: "assistant", content: "oi" }, finish_reason: "stop" }],
  usage: { prompt_tokens: 1000, completion_tokens: 500, total_tokens: 1500 },
};

const baseParams = {
  provider: "openai",
  project: "checkout-service",
  apiKey: "ombk_projeto",
  providerApiKey: "sk-provedor",
  model: "gpt-4o-mini",
  webhookUrl: "http://localhost:5100",
  messages: [{ role: "user", content: "ola" }],
  temperature: 0.2,
};

function sentPayload(fetchMock: ReturnType<typeof vi.fn>, index = 0) {
  return JSON.parse(fetchMock.mock.calls[index][1].body);
}

describe("call", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    openAiState.constructorArgs = [];
    openAiState.createArgs = [];
    openAiState.impl = async () => providerResponse;
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  it("retorna exatamente a resposta do provedor, sem alteracao", async () => {
    const result = await call(baseParams);
    expect(result).toBe(providerResponse);
  });

  it("repassa ao provedor a chave do provedor, o modelo e os demais parametros - e nada do OmniAI", async () => {
    await call(baseParams);

    expect(openAiState.constructorArgs).toEqual([{ apiKey: "sk-provedor" }]);
    const args = openAiState.createArgs[0] as Record<string, unknown>;
    expect(args).toEqual({ model: "gpt-4o-mini", messages: baseParams.messages, temperature: 0.2 });
    expect(args).not.toHaveProperty("apiKey");
    expect(args).not.toHaveProperty("project");
    expect(args).not.toHaveProperty("webhookUrl");
    expect(args).not.toHaveProperty("providerApiKey");
  });

  it("envia metrica de sucesso com tokens, custo calculado, latencia e timestamp UTC", async () => {
    await call(baseParams);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    const payload = sentPayload(fetchMock);
    expect(fetchMock.mock.calls[0][1].headers["X-Api-Key"]).toBe("ombk_projeto");
    expect(payload).toMatchObject({
      project: "checkout-service",
      provider: "openai",
      model: "gpt-4o-mini",
      promptTokens: 1000,
      completionTokens: 500,
      totalTokens: 1500,
      status: "success",
    });
    expect(payload.costUsd).toBeCloseTo(0.00045, 12);
    expect(Number.isInteger(payload.latencyMs)).toBe(true);
    expect(payload.latencyMs).toBeGreaterThanOrEqual(0);
    expect(payload.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
    expect(JSON.stringify(payload)).not.toContain("sk-provedor");
  });

  it("modelo sem preco catalogado envia costUsd null", async () => {
    await call({ ...baseParams, model: "gpt-modelo-novo" });
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(sentPayload(fetchMock).costUsd).toBeNull();
  });

  it("latencia medida reflete o tempo real do provedor", async () => {
    openAiState.impl = () => new Promise((r) => setTimeout(() => r(providerResponse), 120));
    await call(baseParams);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(sentPayload(fetchMock).latencyMs).toBeGreaterThanOrEqual(100);
  });

  it("erro do provedor: propaga o MESMO erro e envia metrica de erro zerada", async () => {
    const providerError = Object.assign(new Error("The model `gpt-x` does not exist"), { status: 404 });
    openAiState.impl = async () => {
      throw providerError;
    };

    await expect(call(baseParams)).rejects.toBe(providerError);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    expect(sentPayload(fetchMock)).toMatchObject({
      status: "error",
      promptTokens: 0,
      completionTokens: 0,
      totalTokens: 0,
      costUsd: null,
    });
  });

  it("webhook fora do ar nao afeta o resultado", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    await expect(call(baseParams)).resolves.toBe(providerResponse);
  });

  it("nao espera o webhook responder (fire-and-forget)", async () => {
    fetchMock.mockImplementation(() => new Promise(() => {})); // nunca responde

    const result = await Promise.race([
      call(baseParams),
      new Promise((_, reject) => setTimeout(() => reject(new Error("call bloqueou esperando o webhook")), 500)),
    ]);

    expect(result).toBe(providerResponse);
  });

  it("provedor nao suportado lanca erro claro sem chamar provedor nem webhook", async () => {
    await expect(call({ ...baseParams, provider: "gemini" })).rejects.toThrow("não é suportado");
    expect(openAiState.createArgs).toHaveLength(0);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
