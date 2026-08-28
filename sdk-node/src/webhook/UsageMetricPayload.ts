export interface UsageMetricPayload {
  project: string;
  provider: string;
  model: string;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  costUsd: number | null;
  latencyMs: number;
  status: "success" | "error";
  timestamp: string;
}
