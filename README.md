# OmniAI

> Única visão para seus provedores de IA.

OmniAI é uma plataforma open-source de observabilidade de custo e uso de IA. Times que chamam múltiplos provedores (OpenAI, e outros por vir) instrumentam suas aplicações com um SDK leve e passam a enxergar, num único dashboard, quanto estão gastando — no total, por projeto e por provedor — em tempo real.

## Como rodar

Requisito: Docker e Docker Compose.

```bash
cp .env.example .env
docker compose up -d --build
```

Depois de alguns segundos:

1. Abra `http://localhost:8080`.
2. Faça login (`admin` / `admin-dev-password`, valores padrão do `.env.example`).
3. Vá em **Projetos**, crie um projeto e copie a API Key exibida — ela só aparece uma vez.
4. Instale o SDK na sua aplicação, configure a chave e a URL do Webhook (`http://localhost:5100`), e comece a chamar seu provedor de IA normalmente. O consumo aparece no dashboard em tempo real.

Detalhes de configuração, portas e troubleshooting: [DOCS/12-ambiente-local.md](DOCS/12-ambiente-local.md).

## Arquitetura, em uma frase por peça

```
SDK (sua aplicação) → Webhook → Redis → Consumer → Postgres → API GraphQL → Cockpit (dashboard)
```

- **SDK** (`sdk-node/`) — instrumenta chamadas a provedores de IA, calcula custo, envia a métrica.
- **Webhook** (`OmniAI/Webhook`) — recebe a métrica, autentica por API Key, publica na fila.
- **Consumer** (`OmniAI/Consumer`) — processa a fila com retry, persiste, notifica o dashboard em tempo real.
- **API GraphQL** (`OmniAI/ApiGraphQL`) — login e consulta de estatísticas de uso.
- **Cockpit** (`cockpit/`) — dashboard React onde o consumo é visualizado.

## Documentação

A pasta [DOCS/](DOCS/README.md) é a fonte de verdade do projeto: por que cada decisão de arquitetura foi tomada, como cada módulo funciona por dentro, modelo de dados, segurança, estrutura do repositório. Comece por [DOCS/01-visao-geral.md](DOCS/01-visao-geral.md).

Este README fica só na porta de entrada — "o que é isso" e "como eu rodo". Regra de negócio e decisão técnica vivem em `DOCS/` (ou no próprio código).

## Licença

[MIT](LICENSE).
