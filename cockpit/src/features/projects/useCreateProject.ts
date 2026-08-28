import { useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { extractErrorMessage } from "../../shared/graphql/extractErrorMessage";
import { useAuth } from "../../app/AuthProvider";

const CREATE_PROJECT_MUTATION = gql`
  mutation CreateProject($name: String!) {
    createProject(name: $name) {
      projectId
      projectName
      apiKey
    }
  }
`;

interface CreateProjectResponse {
  createProject: { projectId: string; projectName: string; apiKey: string };
}

export function useCreateProject() {
  const { token } = useAuth();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(name: string): Promise<{ projectName: string; apiKey: string } | null> {
    if (!token) {
      return null;
    }

    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(token);
      const response = await client.request<CreateProjectResponse>(CREATE_PROJECT_MUTATION, { name });
      return { projectName: response.createProject.projectName, apiKey: response.createProject.apiKey };
    } catch (err) {
      setError(extractErrorMessage(err, "Não foi possível criar o projeto."));
      return null;
    } finally {
      setLoading(false);
    }
  }

  return { submit, loading, error };
}
