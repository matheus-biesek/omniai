export interface ProviderCallParams {
  providerApiKey: string;
  model: string;
  [key: string]: unknown;
}

export interface ProviderCallResult {
  raw: unknown;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
}

export interface ProviderAdapter {
  call(params: ProviderCallParams): Promise<ProviderCallResult>;
}
