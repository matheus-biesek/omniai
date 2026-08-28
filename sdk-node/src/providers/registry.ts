import type { ProviderAdapter } from "./ProviderAdapter.js";
import { OpenAiAdapter } from "./openai/OpenAiAdapter.js";

const adapters: Record<string, () => ProviderAdapter> = {
  openai: () => new OpenAiAdapter(),
};

export function getAdapter(provider: string): ProviderAdapter {
  const factory = adapters[provider.toLowerCase()];
  if (!factory) {
    throw new Error(`Provedor "${provider}" não é suportado pelo SDK.`);
  }

  return factory();
}
