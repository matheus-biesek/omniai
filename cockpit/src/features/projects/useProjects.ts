import { useCallback, useEffect, useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { useAuth } from "../../app/AuthProvider";
import type { ProjectSummary } from "./types";

const PROJECTS_QUERY = gql`
  query Projects {
    projects {
      id
      name
      apiKeys {
        id
        createdAt
        revokedAt
      }
    }
  }
`;

interface ProjectsResponse {
  projects: ProjectSummary[];
}

export function useProjects() {
  const { token } = useAuth();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchProjects = useCallback(async () => {
    if (!token) {
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(token);
      const response = await client.request<ProjectsResponse>(PROJECTS_QUERY);
      setProjects(response.projects);
    } catch {
      setError("Não foi possível carregar os projetos.");
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void fetchProjects();
  }, [fetchProjects]);

  return { projects, loading, error, refetch: fetchProjects };
}
