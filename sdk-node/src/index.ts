export interface OmniAiCallParams {
  provider: string;
  project: string;
  apiKey: string;
  providerApiKey: string;
  model: string;
  [key: string]: unknown;
}

export async function call(params: OmniAiCallParams): Promise<unknown> {
  throw new Error("not implemented");
}
