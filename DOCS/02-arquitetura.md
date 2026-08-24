# Arquitetura Geral

## Visão macro

O sistema é composto por 5 componentes que formam um pipeline de dados unidirecional, do momento em que uma aplicação da empresa chama um provedor de IA até o dado aparecer em tempo real no dashboard:

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Aplicação    │────▶│  SDK (Node)  │────▶│  Webhook     │────▶│  Redis       │
│  da empresa   │     │              │     │  (ASP.NET)   │     │  Stream      │
└──────────────┘     └──────────────┘     └──────────────┘     └──────┬───────┘
                             │                                        │
                             │ resposta do provedor                   ▼
                             │ (sem alteração)                 ┌──────────────┐
                             ▼                                 │  Consumer    │
                      ┌──────────────┐                         │  (Worker)    │
                      │  Provedor    │                         └──────┬───────┘
                      │  de IA       │                                │
                      └──────────────┘                    ┌───────────┼───────────┐
                                                            ▼                       ▼
                                                    ┌──────────────┐       ┌──────────────┐
                                                    │  Postgres    │       │  SignalR Hub │
                                                    │  (escrita)   │       │  (WebSocket) │
                                                    └──────┬───────┘       └──────┬───────┘
                                                           │ replicação           │
                                                           │ nativa (streaming)   │
                                                           ▼                      │
                                                    ┌──────────────┐              │
                                                    │  Postgres    │              │
                                                    │  (leitura)   │              │
                                                    └──────┬───────┘              │
                                                           │                      │
                                                           ▼                      ▼
                                                    ┌─────────────────────────────────┐
                                                    │      API GraphQL (ASP.NET)      │
                                                    └──────────────┬───────────────────┘
                                                                   │
                                                                   ▼
                                                    ┌─────────────────────────────────┐
                                                    │        Dashboard (React)        │
                                                    │  histórico (query) + tempo real │
                                                    │           (WebSocket)           │
                                                    └─────────────────────────────────┘
```

## Componentes e responsabilidades

| Componente | Linguagem/Stack | Responsabilidade única |
|---|---|---|
| **SDK** | Node.js / TypeScript | Envolver a chamada ao provedor de IA, medir latência/tokens/custo e enviar essa métrica ao Webhook, sem alterar a resposta que a aplicação recebe. |
| **Webhook** | C# / ASP.NET Core | Autenticar a requisição, validar o payload e publicar o evento em uma fila (Redis Stream) o mais rápido possível. |
| **Consumer** | C# / ASP.NET Core (`BackgroundService` + Hub SignalR) | Consumir a fila, persistir o evento no banco de escrita e notificar o dashboard em tempo real. |
| **API GraphQL** | C# / ASP.NET Core (HotChocolate) | Autenticar o usuário do dashboard (login) e expor consultas de estatísticas agregadas, lendo do banco de leitura. |
| **Frontend** | React | Tela de login e dashboard: carrega o histórico via GraphQL e depois recebe atualizações em tempo real via WebSocket (SignalR). |

## Por que este desenho

- **Webhook enxuto e a fila como amortecedor.** O Webhook não faz processamento pesado nem grava direto no banco — ele apenas valida e enfileira. Isso mantém o tempo de resposta ao SDK baixo (o SDK não deve atrasar a chamada real de IA da aplicação cliente) e evita que picos de tráfego cheguem direto ao banco de dados.
- **Consumer isolado do caminho de escrita síncrono.** Ao separar quem recebe (Webhook) de quem persiste (Consumer), cada um escala de forma independente: em um pico de requisições, o Webhook aceita mais rápido do que o Consumer processa, e a fila absorve essa diferença dentro de um limite configurado (ver [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila)).
- **Leitura e escrita em bancos separados.** O dashboard faz leituras de agregação (potencialmente pesadas) que não devem competir por recursos com a escrita de novos eventos, que precisa ser rápida e constante. Ver [08-banco-de-dados.md](08-banco-de-dados.md).
- **Tempo real via WebSocket, histórico via GraphQL.** A tela do dashboard não fica com um polling constante: ela busca o histórico uma vez (query GraphQL) e depois só recebe incrementos (WebSocket), o que é mais simples e mais barato do que refazer a query inteira a cada novo dado.

## Fluxo detalhado de uma requisição

1. A aplicação da empresa chama a função do SDK, passando provedor, projeto, API Key e os parâmetros normais da chamada de IA.
2. O SDK repassa a chamada ao provedor real e retorna a resposta à aplicação, sem alterações — do ponto de vista de quem chamou, é uma chamada HTTP normal ao provedor.
3. Em paralelo, sem bloquear a resposta ao chamador, o SDK monta o payload de métricas e envia via HTTPS ao Webhook, com a API Key no header.
4. O Webhook valida rate limit por IP, autentica a API Key, valida o payload, verifica se a fila tem espaço e publica o evento no Redis Stream. Responde `202 Accepted` ao SDK.
5. O Consumer lê o Redis Stream (via consumer group), persiste o evento no Postgres de escrita e publica o evento no SignalR Hub.
6. O Postgres replica os dados de forma assíncrona e nativa para o banco de leitura.
7. O dashboard, ao abrir, consulta a API GraphQL (que lê do banco de leitura) para montar o histórico, e mantém uma conexão WebSocket aberta para receber os eventos publicados pelo Consumer em tempo real.

Cada uma dessas etapas está detalhada no documento do módulo correspondente.
