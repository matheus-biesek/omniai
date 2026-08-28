# Módulo: SDK

## Objetivo

Permitir que uma aplicação Node.js chame um provedor de IA através do OmniAI sem precisar mudar a forma como consome a resposta — o SDK é uma camada transparente que **envolve** a chamada real ao provedor e, como efeito colateral, envia métricas de uso para a plataforma.

## Runtime e distribuição

- **Node.js / TypeScript**, publicado como pacote npm.
- Sem dependência de frameworks do lado da aplicação cliente — funciona em qualquer projeto Node (Express, NestJS, script simples, etc.).

```
sdk-node/
├── src/
│   ├── index.ts                     # API pública: export { call }
│   ├── call.ts                      # orquestra: cronometra → adapter → custo → envia métrica → retorna
│   ├── types.ts                     # OmniAiCallParams
│   ├── providers/
│   │   ├── ProviderAdapter.ts        # interface comum a todo provedor
│   │   ├── registry.ts               # provider (string) → adapter; erro claro se não suportado
│   │   └── openai/OpenAiAdapter.ts   # único adapter implementado até agora
│   ├── pricing/
│   │   ├── pricingTable.ts
│   │   └── calculateCost.ts
│   └── webhook/
│       ├── UsageMetricPayload.ts
│       └── sendMetric.ts             # POST fire-and-forget, nunca lança erro
```

- Cada provedor tem seu SDK oficial como **peer dependency opcional** (`openai`, por exemplo) — importado dinamicamente só dentro do respectivo adapter. Quem usa só um provedor não é obrigado a instalar o pacote dos outros.
- Só o adapter da **OpenAI** está implementado por enquanto; Anthropic e demais entram depois, seguindo a mesma interface (`ProviderAdapter`).

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

**O SDK não lê variável de ambiente para nenhum dado de negócio** (provedor, projeto, API Key, modelo) — tudo isso é parâmetro explícito de `omniai.call(...)`, decidido por quem chama, nunca lido de `process.env` por dentro do pacote. Isso mantém o SDK simples de testar (sem estado global escondido) e portável entre runtimes.

A única exceção é a **URL do Webhook** — não é um dado de negócio, é infraestrutura, e não muda entre uma chamada e outra dentro da mesma aplicação. O SDK lê `process.env.OMNIAI_WEBHOOK_URL` uma vez; se quiser sobrescrever numa chamada específica (ex: multi-tenant apontando pra Webhooks diferentes), `webhookUrl` pode ser passado em `omniai.call(...)` e tem prioridade sobre a variável de ambiente.

## O que o SDK faz internamente

1. **Marca o início** da chamada (timestamp + alta resolução para latência).
2. **Delega a chamada real** ao SDK oficial do provedor (ex: SDK da OpenAI).
3. **Marca o fim** da chamada e calcula a latência.
4. **Extrai tokens** da resposta do provedor (prompt tokens, completion tokens, total).
5. **Calcula o custo estimado** com base em uma tabela de preços por provedor/modelo mantida no próprio SDK. Nenhum provedor retorna preço na resposta da chamada — só contagem de tokens — então essa tabela é o SDK que carrega. Se o modelo não estiver catalogado, `costUsd` é enviado como `null` em vez de `0`, para não confundir "sem custo" com "preço desconhecido".
6. **Envia a métrica ao Webhook** (URL resolvida de `webhookUrl`, se passado, senão de `OMNIAI_WEBHOOK_URL`) de forma assíncrona e "fire-and-forget": o envio não bloqueia nem pode falhar a chamada original da aplicação. Se o envio falhar (timeout — 5s — Webhook fora do ar, ou a variável de ambiente nem estar definida), o erro é apenas logado (`console.error`) — nunca propagado para quem chamou `omniai.call`.
7. **Retorna a resposta original** do provedor à aplicação chamadora, no passo 2.

**Se o provedor de IA falhar de verdade** (erro de rede, modelo inexistente, rate limit, etc.), isso é diferente do passo 6: o SDK ainda envia uma métrica (`status: "error"`, tokens zerados, `costUsd: null`) para refletir a tentativa, mas **propaga o erro original do provedor** para quem chamou — a regra "nunca falhar a chamada original" vale só para o envio da métrica, nunca para o resultado real da chamada de IA em si. Testado com uma chamada real à OpenAI usando um modelo inexistente: o erro da OpenAI chega intacto a quem chamou, e a métrica de falha aparece no banco.

**Por que calcular no SDK e não no backend:** manter o cálculo junto de quem já tem os tokens na mão deixa transparente, pra quem usa o SDK, exatamente como o dado de custo é produzido — sem uma etapa "invisível" acontecendo no backend depois que o evento já saiu da aplicação. O trade-off aceito é que a tabela de preços precisa ser atualizada via nova versão do pacote quando um provedor muda preços (ver [Princípios de design do SDK](#princípios-de-design-do-sdk) abaixo).

## Payload enviado ao Webhook

| Campo | Descrição |
|---|---|
| `project` | Nome do projeto cadastrado. |
| `provider` | Provedor de IA usado (ex: `openai`, `anthropic`). |
| `model` | Modelo usado na chamada. |
| `promptTokens` / `completionTokens` / `totalTokens` | Consumo de tokens da chamada. |
| `costUsd` | Custo estimado da chamada, calculado pelo SDK. `null` se o modelo não estiver na tabela de preços do SDK. |
| `latencyMs` | Tempo total da chamada ao provedor. |
| `status` | `success` ou `error` — o SDK também reporta chamadas que falharam no provedor, para refletir uso real. |
| `timestamp` | Data/hora (UTC) em que a chamada foi executada. |

A API Key do projeto é enviada apenas no header (`X-Api-Key`), nunca no corpo da requisição.

## Princípios de design do SDK

- **Nunca falhar a chamada original.** Qualquer erro no envio da métrica é isolado e não deve, em nenhuma hipótese, lançar exceção para o código da aplicação cliente.
- **Sem bloqueio perceptível.** O envio ao Webhook acontece em paralelo (fire-and-forget), não em sequência antes de retornar a resposta.
- **Tabela de preços embutida e versionada.** Como custo por token muda com o tempo e por modelo, a tabela de preços vive no próprio pacote do SDK e é atualizada em novas versões — isso mantém o cálculo de custo simples, sem exigir uma chamada de rede extra para "descobrir o preço", e transparente para quem instala o pacote (o preço usado é código auditável, não uma regra escondida no backend).
- **Suporte a múltiplos provedores por adapter.** Cada provedor suportado (OpenAI, Anthropic, etc.) tem um adapter interno responsável por: chamar o SDK oficial do provedor e normalizar a extração de tokens/modelo da resposta (cada provedor retorna esse dado em um formato diferente).
