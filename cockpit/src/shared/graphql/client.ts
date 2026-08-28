import { GraphQLClient } from "graphql-request";

export function createGraphQLClient(token: string | null): GraphQLClient {
  return new GraphQLClient(import.meta.env.VITE_GRAPHQL_URL, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
}
