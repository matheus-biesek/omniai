# Módulo: SDK

## Objetivo

Permitir que uma aplicação Node.js chame um provedor de IA através do OmniAI sem precisar mudar a forma como consome a resposta — o SDK é uma camada transparente que **envolve** a chamada real ao provedor e, como efeito colateral, envia métricas de uso para a plataforma.

## Runtime e distribuição

- **Node.js / TypeScript**, publicado como pacote npm.
- Sem dependência de frameworks do lado da aplicação cliente — funciona em qualquer projeto Node (Express, NestJS, script simples, etc.).

## Superfície de API

Uma única função pública é exposta, por exemplo:

```ts
const result = await omniai.call({
  provider: "openai",       // provedor de IA
  project: "checkout-service", // nome do projeto cadastrado no OmniAI
  apiKey: "ombk_xxx",       // API Key do projeto (não confundir com a chave do provedor)
  providerApiKey: "sk-...", // credencial do provedor de IA em si
  model: "gpt-4o-mini",
  ...parametrosDaChamada,   // demais parâmetros normais do provedor (prompt, mensagens, etc.)
});
```

`result` é exatamente o que o provedor de IA retornaria — o SDK não modifica, envolve ou atrasa essa resposta.

## O que o SDK faz internamente

1. **Marca o início** da chamada (timestamp + alta resolução para latência).
2. **Delega a chamada real** ao SDK oficial do provedor (ex: SDK da OpenAI).
3. **Marca o fim** da chamada e calcula a latência.
4. **Extrai tokens** da resposta do provedor (prompt tokens, completion tokens, total).
5. **Calcula o custo estimado** com base em uma tabela de preços por provedor/modelo mantida no próprio SDK.
6. **Envia a métrica ao Webhook** de forma assíncrona e "fire-and-forget": o envio não bloqueia nem pode falhar a chamada original da aplicação. Se o envio falhar (timeout, Webhook fora do ar), o erro é apenas logado — nunca propagado para quem chamou `omniai.call`.
7. **Retorna a resposta original** do provedor à aplicação chamadora, no passo 2.

## Payload enviado ao Webhook

| Campo | Descrição |
|---|---|
| `project` | Nome do projeto cadastrado. |
| `provider` | Provedor de IA usado (ex: `openai`, `anthropic`). |
| `model` | Modelo usado na chamada. |
| `promptTokens` / `completionTokens` / `totalTokens` | Consumo de tokens da chamada. |
| `costUsd` | Custo estimado da chamada, calculado pelo SDK. |
| `latencyMs` | Tempo total da chamada ao provedor. |
| `status` | `success` ou `error` — o SDK também reporta chamadas que falharam no provedor, para refletir uso real. |
| `timestamp` | Data/hora (UTC) em que a chamada foi executada. |

A API Key do projeto é enviada apenas no header (`X-Api-Key`), nunca no corpo da requisição.

## Princípios de design do SDK

- **Nunca falhar a chamada original.** Qualquer erro no envio da métrica é isolado e não deve, em nenhuma hipótese, lançar exceção para o código da aplicação cliente.
- **Sem bloqueio perceptível.** O envio ao Webhook acontece em paralelo (fire-and-forget), não em sequência antes de retornar a resposta.
- **Tabela de preços embutida e versionada.** Como custo por token muda com o tempo e por modelo, a tabela de preços vive no próprio pacote do SDK e é atualizada em novas versões — isso mantém o cálculo de custo simples, sem exigir uma chamada de rede extra para "descobrir o preço".
- **Suporte a múltiplos provedores por adapter.** Cada provedor suportado (OpenAI, Anthropic, etc.) tem um adapter interno responsável por: chamar o SDK oficial do provedor e normalizar a extração de tokens/modelo da resposta (cada provedor retorna esse dado em um formato diferente).
