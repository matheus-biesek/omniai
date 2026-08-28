import { useState } from "react";
import { gql } from "graphql-request";
import { createGraphQLClient } from "../../shared/graphql/client";
import { useAuth } from "../../app/AuthProvider";

const LOGIN_MUTATION = gql`
  mutation Login($username: String!, $password: String!) {
    login(username: $username, password: $password) {
      token
    }
  }
`;

interface LoginResponse {
  login: { token: string };
}

export function useLogin() {
  const { login } = useAuth();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(username: string, password: string): Promise<boolean> {
    setLoading(true);
    setError(null);

    try {
      const client = createGraphQLClient(null);
      const response = await client.request<LoginResponse>(LOGIN_MUTATION, { username, password });
      login(response.login.token);
      return true;
    } catch {
      setError("Usuário ou senha inválidos.");
      return false;
    } finally {
      setLoading(false);
    }
  }

  return { submit, loading, error };
}
