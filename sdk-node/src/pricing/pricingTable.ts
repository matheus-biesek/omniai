export interface PricingEntry {
  promptPerMillion: number;
  completionPerMillion: number;
}

export const pricingTable: Record<string, Record<string, PricingEntry>> = {
  openai: {
    "gpt-4o": { promptPerMillion: 2.5, completionPerMillion: 10 },
    "gpt-4o-mini": { promptPerMillion: 0.15, completionPerMillion: 0.6 },
  },
};
