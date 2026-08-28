import { useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { extractErrorMessage } from "../../shared/graphql/extractErrorMessage";
import { useAuth } from "../../app/AuthProvider";

const REVOKE_API_KEY_MUTATION = gql`
  mutation RevokeApiKey($apiKeyId: UUID!) {
    revokeApiKey(apiKeyId: $apiKeyId)
  }
`;

export function useRevokeApiKey() {
  const { token } = useAuth();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(apiKeyId: string): Promise<boolean> {
    if (!token) {
      return false;
    }

    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(token);
      await client.request(REVOKE_API_KEY_MUTATION, { apiKeyId });
      return true;
    } catch (err) {
      setError(extractErrorMessage(err, "Não foi possível revogar a chave de API."));
      return false;
    } finally {
      setLoading(false);
    }
  }

  return { submit, loading, error };
}
