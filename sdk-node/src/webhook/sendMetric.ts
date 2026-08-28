import type { UsageMetricPayload } from "./UsageMetricPayload.js";

const REQUEST_TIMEOUT_MS = 5000;

export async function sendMetric(
  apiKey: string,
  webhookUrl: string | undefined,
  payload: UsageMetricPayload,
): Promise<void> {
  const url = webhookUrl ?? process.env.OMNIAI_WEBHOOK_URL;

  if (!url) {
    console.error("[omniai-sdk] OMNIAI_WEBHOOK_URL não definida e webhookUrl não informado - métrica não enviada.");
    return;
  }

  try {
    const response = await fetch(`${url.replace(/\/$/, "")}/events`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Api-Key": apiKey,
      },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    });

    if (!response.ok) {
      console.error(`[omniai-sdk] Webhook respondeu ${response.status} ao enviar métrica.`);
    }
  } catch (error) {
    console.error("[omniai-sdk] Falha ao enviar métrica ao Webhook.", error);
  }
}
