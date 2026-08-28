import { useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { extractErrorMessage } from "../../shared/graphql/extractErrorMessage";
import { useAuth } from "../../app/AuthProvider";

const CREATE_API_KEY_MUTATION = gql`
  mutation CreateApiKey($projectId: UUID!) {
    createApiKey(projectId: $projectId) {
      apiKeyId
      apiKey
    }
  }
`;

interface CreateApiKeyResponse {
  createApiKey: { apiKeyId: string; apiKey: string };
}

export function useCreateApiKey() {
  const { token } = useAuth();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(projectId: string): Promise<string | null> {
    if (!token) {
      return null;
    }

    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(token);
      const response = await client.request<CreateApiKeyResponse>(CREATE_API_KEY_MUTATION, { projectId });
      return response.createApiKey.apiKey;
    } catch (err) {
      setError(extractErrorMessage(err, "Não foi possível criar a chave de API."));
      return null;
    } finally {
      setLoading(false);
    }
  }

  return { submit, loading, error };
}
