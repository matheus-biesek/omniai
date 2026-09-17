import { describe, expect, it } from "vitest";
import { mergeUsageEvent } from "./mergeUsageEvent";
import type { UsageReceivedEvent, UsageStatistics } from "./types";

function event(overrides: Partial<UsageReceivedEvent> = {}): UsageReceivedEvent {
  return {
    id: 1,
    projectId: "00000000-0000-0000-0000-000000000001",
    project: "checkout",
    provider: "openai",
    model: "gpt-4o-mini",
    promptTokens: 100,
    completionTokens: 50,
    totalTokens: 150,
    costUsd: 0.5,
    latencyMs: 200,
    status: "success",
    occurredAt: "2026-09-16T10:00:00Z",
    ...overrides,
  };
}

const base: UsageStatistics = {
  totalCostUsd: 10,
  totalTokens: 1000,
  totalRequests: 4,
  byProvider: [
    { provider: "openai", costUsd: 8, tokens: 800, requests: 3 },
    { provider: "anthropic", costUsd: 2, tokens: 200, requests: 1 },
  ],
  byProject: [{ project: "checkout", costUsd: 10, tokens: 1000, requests: 4 }],
};

describe("mergeUsageEvent", () => {
  it("soma o evento aos totais", () => {
    const merged = mergeUsageEvent(base, event());

    expect(merged.totalCostUsd).toBeCloseTo(10.5);
    expect(merged.totalTokens).toBe(1150);
    expect(merged.totalRequests).toBe(5);
  });

  it("incrementa o grupo existente de provedor e projeto sem mexer nos outros", () => {
    const merged = mergeUsageEvent(base, event());

    expect(merged.byProvider).toEqual([
      { provider: "openai", costUsd: 8.5, tokens: 950, requests: 4 },
      { provider: "anthropic", costUsd: 2, tokens: 200, requests: 1 },
    ]);
    expect(merged.byProject).toEqual([{ project: "checkout", costUsd: 10.5, tokens: 1150, requests: 5 }]);
  });

  it("cria grupo novo quando provedor/projeto ainda nao aparece", () => {
    const merged = mergeUsageEvent(base, event({ provider: "google", project: "search", costUsd: 1, totalTokens: 10 }));

    expect(merged.byProvider).toContainEqual({ provider: "google", costUsd: 1, tokens: 10, requests: 1 });
    expect(merged.byProject).toContainEqual({ project: "search", costUsd: 1, tokens: 10, requests: 1 });
    expect(merged.byProvider).toHaveLength(3);
    expect(merged.byProject).toHaveLength(2);
  });

  it("custo nulo conta como zero (igual a API), mas a requisicao conta", () => {
    const merged = mergeUsageEvent(base, event({ costUsd: null }));

    expect(merged.totalCostUsd).toBe(10);
    expect(merged.totalRequests).toBe(5);
    expect(merged.byProvider[0].costUsd).toBe(8);
  });

  it("parte de estatisticas vazias", () => {
    const empty: UsageStatistics = { totalCostUsd: 0, totalTokens: 0, totalRequests: 0, byProvider: [], byProject: [] };

    const merged = mergeUsageEvent(empty, event());

    expect(merged).toEqual({
      totalCostUsd: 0.5,
      totalTokens: 150,
      totalRequests: 1,
      byProvider: [{ provider: "openai", costUsd: 0.5, tokens: 150, requests: 1 }],
      byProject: [{ project: "checkout", costUsd: 0.5, tokens: 150, requests: 1 }],
    });
  });

  it("nao muta o estado atual (React depende de referencia nova)", () => {
    const snapshot = structuredClone(base);

    const merged = mergeUsageEvent(base, event());

    expect(base).toEqual(snapshot);
    expect(merged).not.toBe(base);
    expect(merged.byProvider).not.toBe(base.byProvider);
    expect(merged.byProject).not.toBe(base.byProject);
  });
});
