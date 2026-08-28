import { calculateCost } from "./pricing/calculateCost.js";
import { getAdapter } from "./providers/registry.js";
import type { OmniAiCallParams } from "./types.js";
import { sendMetric } from "./webhook/sendMetric.js";

export async function call(params: OmniAiCallParams): Promise<unknown> {
  const { provider, project, apiKey, providerApiKey, model, webhookUrl, ...rest } = params;
  const adapter = getAdapter(provider);

  const start = performance.now();

  try {
    const result = await adapter.call({ providerApiKey, model, ...rest });
    const latencyMs = Math.round(performance.now() - start);
    const costUsd = calculateCost(provider, model, result.promptTokens, result.completionTokens);

    void sendMetric(apiKey, webhookUrl, {
      project,
      provider,
      model,
      promptTokens: result.promptTokens,
      completionTokens: result.completionTokens,
      totalTokens: result.totalTokens,
      costUsd,
      latencyMs,
      status: "success",
      timestamp: new Date().toISOString(),
    });

    return result.raw;
  } catch (error) {
    const latencyMs = Math.round(performance.now() - start);

    void sendMetric(apiKey, webhookUrl, {
      project,
      provider,
      model,
      promptTokens: 0,
      completionTokens: 0,
      totalTokens: 0,
      costUsd: null,
      latencyMs,
      status: "error",
      timestamp: new Date().toISOString(),
    });

    // A falha e do provedor de IA, nao do envio da metrica - propaga pra quem chamou, como
    // qualquer chamada normal ao provedor faria.
    throw error;
  }
}
