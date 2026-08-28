export interface UsageByProvider {
  provider: string;
  costUsd: number;
  tokens: number;
  requests: number;
}

export interface UsageByProject {
  project: string;
  costUsd: number;
  tokens: number;
  requests: number;
}

export interface UsageStatistics {
  totalCostUsd: number;
  totalTokens: number;
  totalRequests: number;
  byProvider: UsageByProvider[];
  byProject: UsageByProject[];
}

export interface UsageStatisticsFilter {
  project?: string;
  provider?: string;
}

export interface UsageReceivedEvent {
  id: number;
  projectId: string;
  project: string;
  provider: string;
  model: string;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  costUsd: number | null;
  latencyMs: number;
  status: "success" | "error";
  occurredAt: string;
}
