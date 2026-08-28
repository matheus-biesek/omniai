import type { UsageByProject, UsageByProvider, UsageReceivedEvent, UsageStatistics } from "./types";

function incrementGroup<T extends { costUsd: number; tokens: number; requests: number }>(
  groups: T[],
  key: keyof T,
  keyValue: string,
  costUsd: number,
  tokens: number,
  emptyGroup: T,
): T[] {
  const existing = groups.find((g) => g[key] === keyValue);
  if (!existing) {
    return [...groups, { ...emptyGroup, costUsd, tokens, requests: 1 }];
  }

  return groups.map((g) =>
    g[key] === keyValue
      ? { ...g, costUsd: g.costUsd + costUsd, tokens: g.tokens + tokens, requests: g.requests + 1 }
      : g,
  );
}

export function mergeUsageEvent(current: UsageStatistics, event: UsageReceivedEvent): UsageStatistics {
  const costUsd = event.costUsd ?? 0;
  const tokens = event.totalTokens;

  return {
    totalCostUsd: current.totalCostUsd + costUsd,
    totalTokens: current.totalTokens + tokens,
    totalRequests: current.totalRequests + 1,
    byProvider: incrementGroup<UsageByProvider>(current.byProvider, "provider", event.provider, costUsd, tokens, {
      provider: event.provider,
      costUsd: 0,
      tokens: 0,
      requests: 0,
    }),
    byProject: incrementGroup<UsageByProject>(current.byProject, "project", event.project, costUsd, tokens, {
      project: event.project,
      costUsd: 0,
      tokens: 0,
      requests: 0,
    }),
  };
}
