# e2e

Testes ponta a ponta do OmniAI contra o sistema **rodando** via `docker compose` — Webhook, Redis, Consumer, Postgres (primary + réplica), API GraphQL, Hub SignalR, SDK e o Cockpit num navegador de verdade.

## Pré-requisitos

- Stack de pé: `docker compose up -d --build` na raiz (com o `.env` padrão de `.env.example`).
- `sdk-node` com dependências instaladas (`npm install` lá) — o teste do SDK compila o `dist/` se ele não existir.
- Google Chrome instalado (o `playwright-core` não baixa navegador). Para usar o Edge: `E2E_BROWSER_CHANNEL=msedge`.

## Rodando

```bash
cd e2e
npm install
npm test                                   # todos, em ordem
node --test --test-reporter=spec 02-pipeline.test.mjs   # um arquivo só
```

Os arquivos rodam em ordem numérica e um de cada vez:

| Arquivo | Cobre |
|---|---|
| `01-api` | Nginx do Cockpit, login/JWT, autenticação obrigatória, projetos e chaves |
| `02-pipeline` | Webhook (auth, validação), processamento no Consumer, agregação na réplica, revogação, datas e payloads anômalos |
| `03-realtime` | Hub SignalR: autenticação e entrega do `UsageReceived` |
| `04-sdk` | SDK real contra um servidor fake da OpenAI (`OPENAI_BASE_URL`) |
| `05-browser` | Cockpit no Chrome headless: login, projetos, dashboard, tempo real, revogação, logout |
| `06-resilience` | **Para e religa o container `omniai-consumer`**; réplica em standby |
| `99-ratelimit` | Esgota o rate limit por IP do Webhook — por **60s** depois disso, o Webhook recusa requisições da sua máquina |

## Observações

- Os testes criam projetos com nomes únicos (`pipeline-<timestamp>`, `sdk-<timestamp>`...) no banco do ambiente local e não os apagam — o sistema não tem exclusão de projeto. Para zerar: `docker compose down -v && docker compose up -d --build`.
- Para rodar de novo logo em seguida, espere 60s por causa do `99-ratelimit`.
- Os testes de integração .NET (`OmniAI/Tests`, categoria `Integration`) também usam o Postgres/Redis do compose, mas num banco próprio (`omniai_tests`) — esses não tocam nos dados do ambiente.
