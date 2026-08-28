import { pricingTable } from "./pricingTable.js";

export function calculateCost(
  provider: string,
  model: string,
  promptTokens: number,
  completionTokens: number,
): number | null {
  const entry = pricingTable[provider.toLowerCase()]?.[model.toLowerCase()];
  if (!entry) {
    return null;
  }

  const promptCost = (promptTokens / 1_000_000) * entry.promptPerMillion;
  const completionCost = (completionTokens / 1_000_000) * entry.completionPerMillion;
  return promptCost + completionCost;
}
