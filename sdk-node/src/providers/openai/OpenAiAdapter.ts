import type { ProviderAdapter, ProviderCallParams, ProviderCallResult } from "../ProviderAdapter.js";

export class OpenAiAdapter implements ProviderAdapter {
  async call(params: ProviderCallParams): Promise<ProviderCallResult> {
    const { OpenAI } = await import("openai");
    const { providerApiKey, model, ...rest } = params;

    const client = new OpenAI({ apiKey: providerApiKey });
    const response = await client.chat.completions.create({
      model,
      ...rest,
    } as Parameters<typeof client.chat.completions.create>[0]);

    const usage = "usage" in response ? response.usage : undefined;

    return {
      raw: response,
      promptTokens: usage?.prompt_tokens ?? 0,
      completionTokens: usage?.completion_tokens ?? 0,
      totalTokens: usage?.total_tokens ?? 0,
    };
  }
}
