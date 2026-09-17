import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { sendMetric } from "../src/webhook/sendMetric.js";
import type { UsageMetricPayload } from "../src/webhook/UsageMetricPayload.js";

const payload: UsageMetricPayload = {
  project: "checkout-service",
  provider: "openai",
  model: "gpt-4o-mini",
  promptTokens: 10,
  completionTokens: 5,
  totalTokens: 15,
  costUsd: 0.0000045,
  latencyMs: 100,
  status: "success",
  timestamp: "2026-09-16T10:00:00.000Z",
};

describe("sendMetric", () => {
  const fetchMock = vi.fn();
  const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);
    consoleError.mockClear();
    delete process.env.OMNIAI_WEBHOOK_URL;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("faz POST em <url>/events com X-Api-Key no header e payload no corpo", async () => {
    await sendMetric("ombk_123", "http://localhost:5100", payload);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://localhost:5100/events");
    expect(init.method).toBe("POST");
    expect(init.headers["X-Api-Key"]).toBe("ombk_123");
    expect(init.headers["Content-Type"]).toBe("application/json");
    expect(JSON.parse(init.body)).toEqual(payload);
    expect(init.body).not.toContain("ombk_123");
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });

  it("remove barra final da URL", async () => {
    await sendMetric("k", "http://localhost:5100/", payload);
    expect(fetchMock.mock.calls[0][0]).toBe("http://localhost:5100/events");
  });

  it("usa OMNIAI_WEBHOOK_URL quando webhookUrl nao e passado", async () => {
    process.env.OMNIAI_WEBHOOK_URL = "http://env-webhook:5100";
    await sendMetric("k", undefined, payload);
    expect(fetchMock.mock.calls[0][0]).toBe("http://env-webhook:5100/events");
  });

  it("webhookUrl da chamada tem prioridade sobre a variavel de ambiente", async () => {
    process.env.OMNIAI_WEBHOOK_URL = "http://env-webhook:5100";
    await sendMetric("k", "http://override:9999", payload);
    expect(fetchMock.mock.calls[0][0]).toBe("http://override:9999/events");
  });

  it("sem URL nenhuma: loga e nao chama fetch, sem lancar", async () => {
    await expect(sendMetric("k", undefined, payload)).resolves.toBeUndefined();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(consoleError).toHaveBeenCalled();
  });

  it("erro de rede nao e propagado", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    await expect(sendMetric("k", "http://localhost:1", payload)).resolves.toBeUndefined();
    expect(consoleError).toHaveBeenCalled();
  });

  it("resposta nao-2xx (ex: 401) e so logada", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));
    await expect(sendMetric("k", "http://localhost:5100", payload)).resolves.toBeUndefined();
    expect(consoleError.mock.calls[0][0]).toContain("401");
  });

  it("aborta por timeout de 5s sem lancar", async () => {
    // Webhook que nunca responde: so o AbortSignal.timeout(5000) do SDK encerra a espera.
    fetchMock.mockImplementation(
      (_url: string, init: RequestInit) =>
        new Promise((_resolve, reject) => {
          init.signal!.addEventListener("abort", () => reject(init.signal!.reason));
        }),
    );

    const started = Date.now();
    await expect(sendMetric("k", "http://localhost:5100", payload)).resolves.toBeUndefined();
    const elapsed = Date.now() - started;
    expect(elapsed).toBeGreaterThanOrEqual(4900);
    expect(elapsed).toBeLessThan(7000);
    expect(consoleError).toHaveBeenCalled();
  }, 10_000);
});
