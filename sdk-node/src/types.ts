export interface OmniAiCallParams {
  provider: string;
  project: string;
  apiKey: string;
  providerApiKey: string;
  model: string;
  /** Sobrescreve OMNIAI_WEBHOOK_URL para esta chamada, se definida. */
  webhookUrl?: string;
  [key: string]: unknown;
}
