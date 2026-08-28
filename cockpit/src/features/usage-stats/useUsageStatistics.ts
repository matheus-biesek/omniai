import { useCallback, useEffect, useRef, useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { createHubConnection } from "../../shared/realtime/hubConnection";
import { useAuth } from "../../app/AuthProvider";
import { mergeUsageEvent } from "./mergeUsageEvent";
import type { UsageReceivedEvent, UsageStatistics, UsageStatisticsFilter } from "./types";

const USAGE_STATISTICS_QUERY = gql`
  query UsageStatistics($filter: UsageStatisticsFilterInput) {
    usageStatistics(filter: $filter) {
      totalCostUsd
      totalTokens
      totalRequests
      byProvider {
        provider
        costUsd
        tokens
        requests
      }
      byProject {
        project
        costUsd
        tokens
        requests
      }
    }
  }
`;

interface UsageStatisticsResponse {
  usageStatistics: UsageStatistics;
}

export function useUsageStatistics(filter: UsageStatisticsFilter) {
  const { token } = useAuth();
  const [data, setData] = useState<UsageStatistics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const filterRef = useRef(filter);
  filterRef.current = filter;

  const fetchStatistics = useCallback(async () => {
    if (!token) {
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(token);
      const response = await client.request<UsageStatisticsResponse>(USAGE_STATISTICS_QUERY, { filter });
      setData(response.usageStatistics);
    } catch {
      setError("Não foi possível carregar as estatísticas.");
    } finally {
      setLoading(false);
    }
  }, [token, filter]);

  useEffect(() => {
    void fetchStatistics();
  }, [fetchStatistics]);

  useEffect(() => {
    if (!token) {
      return;
    }

    const connection = createHubConnection(token);

    connection.on("UsageReceived", (event: UsageReceivedEvent) => {
      const activeFilter = filterRef.current;
      const matchesProject = !activeFilter.project || activeFilter.project === event.project;
      const matchesProvider = !activeFilter.provider || activeFilter.provider === event.provider;
      if (!matchesProject || !matchesProvider) {
        return;
      }

      setData((current) => (current ? mergeUsageEvent(current, event) : current));
    });

    connection.start().catch(() => {
      // Conexao em tempo real e um extra - se falhar, o dashboard segue funcional com o
      // historico ja carregado, so sem atualizacao ao vivo ate a proxima consulta manual.
      console.error("[cockpit] Falha ao conectar ao Hub de tempo real.");
    });

    return () => {
      void connection.stop();
    };
  }, [token]);

  return { data, loading, error, refetch: fetchStatistics };
}
