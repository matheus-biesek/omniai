# Ambiente Local / Self-Host (Docker Compose)

## Objetivo

Subir o sistema **inteiro** — infraestrutura de dados e os quatro serviços da aplicação — com um único comando, sem exigir nenhum passo manual de configuração. Esse é o requisito central: alguém clonando o repositório roda `docker compose up`, faz login, cria um projeto, copia a API Key e já está pronto para instrumentar uma aplicação própria com o [SDK](03-modulo-sdk.md) — a única dificuldade deveria ser a aplicação da própria pessoa, não "como faço esse software de observabilidade rodar".

## Serviços

`docker-compose.yml` (raiz de `proex/`) define:

| Serviço | Imagem/build | Porta local (padrão) | Papel |
|---|---|---|---|
| `postgres-primary` | `bitnami/postgresql:latest` | `5432` | Banco de escrita — ver [08-banco-de-dados.md](08-banco-de-dados.md) |
| `postgres-replica` | `bitnami/postgresql:latest` | `5433` | Banco de leitura, réplica assíncrona do primary |
| `redis` | `redis:7-alpine` | `6379` | Fila (Redis Streams) — ver [04-modulo-webhook.md](04-modulo-webhook.md) |
| `migrator` | build de `OmniAI/Migrator` | — (não expõe porta) | Aplica as EF Core migrations e encerra — ver [10-estrutura-repositorio.md](10-estrutura-repositorio.md#migrator-aplica-as-migrations-e-encerra) |
| `webhook` | build de `OmniAI/Webhook` | `5100` | Ingestão de eventos de uso — ver [04-modulo-webhook.md](04-modulo-webhook.md) |
| `consumer` | build de `OmniAI/Consumer` | `5200` | Processamento da fila + Hub SignalR (`/hub`) — ver [05-modulo-consumer.md](05-modulo-consumer.md) |
| `apigraphql` | build de `OmniAI/ApiGraphQL` | `5300` | Login + GraphQL (`/graphql`) — ver [06-modulo-api.md](06-modulo-api.md) |
| `cockpit` | build de `cockpit/` (Nginx servindo o build estático) | `8080` | Dashboard — ver [07-modulo-frontend.md](07-modulo-frontend.md) |

A ordem de subida é garantida por `depends_on` com `condition: service_healthy` / `service_completed_successfully`: os bancos de dados e o Redis precisam responder ao healthcheck antes do `migrator` rodar, e o `migrator` precisa terminar com sucesso antes de `webhook`, `consumer` e `apigraphql` subirem — ninguém tenta ler ou escrever num schema que ainda não existe.

## Por que a imagem Bitnami

A imagem `bitnami/postgresql` configura streaming replication (primary/réplica) inteiramente via variáveis de ambiente (`POSTGRESQL_REPLICATION_MODE`, `POSTGRESQL_MASTER_HOST`, etc.), sem exigir escrever scripts de inicialização manuais para `pg_basebackup`, `pg_hba.conf` ou `postgresql.conf` — coerente com a decisão em [08-banco-de-dados.md](08-banco-de-dados.md#como-a-replicação-é-feita) de usar replicação nativa do Postgres sem complexidade extra de aplicação.

**Nota sobre a tag da imagem:** desde 2025 a Bitnami restringiu, no Docker Hub gratuito, a disponibilidade de tags de versão específica (ex: `:17`) — apenas `:latest` está disponível sem assinatura paga (Bitnami Secure Images). O compose usa `:latest` propositalmente; para produção, avalie se vale fixar por outro meio (imagem própria, digest, ou Postgres gerenciado do provedor de nuvem).

## Como subir

```bash
cp .env.example .env
docker compose up -d --build
```

Isso sobe tudo. Depois de alguns segundos (o Postgres primary precisa ficar saudável, o `migrator` aplicar as migrations e só então os serviços da aplicação subirem):

1. Abra `http://localhost:8080` (porta de `COCKPIT_PORT` no `.env`).
2. Faça login com `DASHBOARD_USERNAME`/`DASHBOARD_PASSWORD` do `.env` (padrão: `admin` / `admin-dev-password`).
3. Vá em **Projetos**, crie um projeto e copie a API Key exibida (ela não aparece de novo — ver [09-seguranca.md](09-seguranca.md#api-key-hash-em-vez-de-criptografia-reversível)).
4. Configure o [SDK](03-modulo-sdk.md) na sua aplicação com essa chave e a URL do Webhook (`http://localhost:5100` por padrão) e comece a chamar seu provedor de IA normalmente — o consumo aparece no Dashboard em tempo real.

`.env` é ignorado pelo git (`.gitignore` na raiz) — mesmo contendo apenas credenciais de desenvolvimento local, sem uso em produção, o hábito de nunca versionar `.env` é deliberado (ver [09-seguranca.md](09-seguranca.md#segredos-e-configuração)).

## Variáveis de ambiente

`.env.example` (versionado) documenta todas as variáveis que o `docker-compose.yml` espera:

| Variável | Uso |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Credenciais e nome do banco criado no primary |
| `POSTGRES_REPLICATION_USER` / `POSTGRES_REPLICATION_PASSWORD` | Usuário dedicado que a réplica usa para se conectar ao primary via streaming replication |
| `POSTGRES_PRIMARY_PORT` / `POSTGRES_REPLICA_PORT` | Portas expostas no host (`5432`/`5433`), para conectar com um client de banco durante o desenvolvimento |
| `REDIS_PORT` | Porta exposta no host (`6379`) |
| `WEBHOOK_PORT` / `CONSUMER_PORT` / `APIGRAPHQL_PORT` / `COCKPIT_PORT` | Portas expostas no host para cada serviço da aplicação |
| `DASHBOARD_USERNAME` / `DASHBOARD_PASSWORD` | Credencial única de login do Dashboard (ver [09-seguranca.md](09-seguranca.md#autenticação-do-dashboard-login)) |
| `JWT_SIGNING_KEY` | Chave HMAC de assinatura do JWT do Dashboard — mínimo 32 caracteres |
| `API_KEY_PEPPER_SECRET` | Pepper usado no hash HMAC das API Keys de projeto (ver [09-seguranca.md](09-seguranca.md#api-key-hash-em-vez-de-criptografia-reversível)) |

Os serviços C# recebem esses valores como variáveis de ambiente no próprio `docker-compose.yml` (ex: `ConnectionStrings__WriteDatabase`, `Jwt__SigningKey`) — o padrão de nomenclatura com `__` é o jeito nativo do ASP.NET Core de sobrescrever configuração via variável de ambiente, sem exigir nenhum código extra (mesma regra descrita em [09-seguranca.md](09-seguranca.md#em-produção)). O `cockpit` é uma exceção: por ser uma SPA estática, sua URL do GraphQL e do Hub SignalR são resolvidas em **tempo de build** (`VITE_GRAPHQL_URL`/`VITE_HUB_URL`, passadas como build args a partir de `APIGRAPHQL_PORT`/`CONSUMER_PORT`) — trocar a porta depois de já ter buildado a imagem exige rebuildar o `cockpit`.

**Importante para quem for expor isso além do `localhost`:** os valores de `DASHBOARD_PASSWORD`, `JWT_SIGNING_KEY` e `API_KEY_PEPPER_SECRET` no `.env.example` são só para começar a rodar localmente — trocar os três antes de qualquer deploy real é obrigatório (ver [09-seguranca.md](09-seguranca.md#em-produção)).

## CORS entre o Cockpit e a API/Hub

O Cockpit é servido de uma origem própria (porta do Nginx) e chama a API GraphQL e o Hub SignalR do navegador — cada um desses dois serviços precisa liberar essa origem via CORS. Isso é configurado por `Cors__AllowedOrigin` (ApiGraphQL e Consumer), calculado a partir de `COCKPIT_PORT` no compose. Se você mudar `COCKPIT_PORT` no `.env`, o CORS acompanha automaticamente — não precisa editar nada além do `.env`.

## Como verificar que a replicação está funcionando

Sanity check manual, útil depois de qualquer mudança no `docker-compose.yml`:

```bash
# grava no primary
docker exec -e PGPASSWORD=$POSTGRES_PASSWORD omniai-postgres-primary \
  psql -U $POSTGRES_USER -d $POSTGRES_DB -c "CREATE TABLE IF NOT EXISTS t (id serial, msg text); INSERT INTO t (msg) VALUES ('ok');"

# lê da réplica (deve aparecer em poucos segundos)
docker exec -e PGPASSWORD=$POSTGRES_PASSWORD omniai-postgres-replica \
  psql -U $POSTGRES_USER -d $POSTGRES_DB -c "SELECT * FROM t;"
```

Se a linha aparecer na réplica, a replicação está ativa. `SELECT pg_is_in_recovery();` na réplica também deve retornar `t` (true) — é assim que o Postgres identifica que aquela instância é uma standby, não um primary independente.

**Nota:** consultar `pg_stat_replication` no primary como um usuário não-superuser (o `POSTGRES_USER` configurado aqui) retorna a linha do processo de replicação, mas com a maioria das colunas (`state`, `*_lsn`, etc.) vazias — é uma restrição de visibilidade do próprio Postgres para não-superusers, não um indício de que a replicação está quebrada. O teste de escrever e ler de volta acima é a forma confiável de verificar.

## Redis: sanity check rápido

```bash
docker exec omniai-redis redis-cli XADD test-stream '*' field value
docker exec omniai-redis redis-cli XLEN test-stream
docker exec omniai-redis redis-cli DEL test-stream
```

## Subindo e derrubando

```bash
docker compose up -d --build   # builda as imagens da aplicação (se necessário) e sobe tudo em background
docker compose ps              # status de cada serviço
docker compose logs -f <nome>  # acompanhar log de um serviço específico (ex: consumer)
docker compose down            # derruba (mantém os volumes/dados)
docker compose down -v         # derruba e apaga os dados (recomeça do zero, inclusive as migrations)
```

Os dados de cada serviço ficam em volumes nomeados do Docker (`postgres_primary_data`, `postgres_replica_data`, `redis_data`) — não em pastas do repositório, então não aparecem no `git status` nem precisam de entrada própria no `.gitignore`.

## Desenvolvendo sem o Docker Compose (opcional)

Rodar cada serviço direto na máquina (fora de container) continua funcionando para quem estiver desenvolvendo ativamente um dos serviços C# ou o `cockpit` — nesse caso, é só o Postgres/Redis que precisam estar no compose; os serviços C# usam `dotnet run` e o `cockpit` usa `npm run dev`, apontando para `localhost` via `appsettings.Development.json`/`cockpit/.env`. Portas fixadas em `Properties/launchSettings.json` de cada serviço, para não depender do valor aleatório que o template do Visual Studio gera:

| Serviço | Porta (HTTP, dev) |
|---|---|
| Webhook | `5100` |
| Consumer | `5200` (inclui o Hub do SignalR em `/hub`) |
| ApiGraphQL | `5300` (GraphQL em `/graphql`) |
| cockpit (Vite dev server) | `5173` (padrão do Vite) |

Nesse modo, aplicar migrations é manual (ver [10-estrutura-repositorio.md](10-estrutura-repositorio.md#gerando-e-aplicando-migrations)) — o `migrator` só roda dentro do `docker compose up`.
