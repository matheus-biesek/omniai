import { ClientError } from "graphql-request";

export function extractErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ClientError) {
    const message = error.response.errors?.[0]?.message;
    if (message) {
      return message;
    }
  }

  return fallback;
}
