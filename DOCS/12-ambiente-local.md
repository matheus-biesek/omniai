# Ambiente Local (Docker Compose)

## Objetivo

Subir a infraestrutura de dados (Postgres primary + réplica, Redis) localmente com um único comando, para desenvolvimento dos serviços C# e testes do SDK — sem exigir que cada desenvolvedor configure replicação de banco manualmente.

## Serviços

`docker-compose.yml` (raiz de `proex/`) define três serviços:

| Serviço | Imagem | Porta local | Papel |
|---|---|---|---|
| `postgres-primary` | `bitnami/postgresql:latest` | `5432` | Banco de escrita — ver [08-banco-de-dados.md](08-banco-de-dados.md) |
| `postgres-replica` | `bitnami/postgresql:latest` | `5433` | Banco de leitura, réplica assíncrona do primary |
| `redis` | `redis:7-alpine` | `6379` | Fila (Redis Streams) — ver [04-modulo-webhook.md](04-modulo-webhook.md) |

## Por que a imagem Bitnami

A imagem `bitnami/postgresql` configura streaming replication (primary/réplica) inteiramente via variáveis de ambiente (`POSTGRESQL_REPLICATION_MODE`, `POSTGRESQL_MASTER_HOST`, etc.), sem exigir escrever scripts de inicialização manuais para `pg_basebackup`, `pg_hba.conf` ou `postgresql.conf` — coerente com a decisão em [08-banco-de-dados.md](08-banco-de-dados.md#como-a-replicação-é-feita) de usar replicação nativa do Postgres sem complexidade extra de aplicação.

**Nota sobre a tag da imagem:** desde 2025 a Bitnami restringiu, no Docker Hub gratuito, a disponibilidade de tags de versão específica (ex: `:17`) — apenas `:latest` está disponível sem assinatura paga (Bitnami Secure Images). O compose usa `:latest` propositalmente; para produção, avalie se vale fixar por outro meio (imagem própria, digest, ou Postgres gerenciado do provedor de nuvem).

## Variáveis de ambiente

`.env.example` (versionado) documenta todas as variáveis que o `docker-compose.yml` espera. Para rodar localmente:

```
cp .env.example .env
docker compose up -d
```

`.env` é ignorado pelo git (`.gitignore` na raiz) — mesmo contendo apenas credenciais de desenvolvimento local, sem uso em produção, o hábito de nunca versionar `.env` é deliberado (ver [09-seguranca.md](09-seguranca.md#segredos-e-configuração)).

| Variável | Uso |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Credenciais e nome do banco criado no primary |
| `POSTGRES_REPLICATION_USER` / `POSTGRES_REPLICATION_PASSWORD` | Usuário dedicado que a réplica usa para se conectar ao primary via streaming replication |
| `POSTGRES_PRIMARY_PORT` / `POSTGRES_REPLICA_PORT` | Portas expostas no host (`5432`/`5433`), para conectar com um client de banco durante o desenvolvimento |
| `REDIS_PORT` | Porta exposta no host (`6379`) |

Os serviços C# (Webhook, Consumer, ApiGraphQL) apontam para esses mesmos containers via suas próprias connection strings, configuradas em `appsettings.Development.json` ou variáveis de ambiente do processo — não leem o `.env` do Docker Compose diretamente (são processos separados, fora do compose).

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
docker compose up -d       # sobe os três serviços em background
docker compose ps          # status
docker compose down        # derruba (mantém os volumes/dados)
docker compose down -v     # derruba e apaga os dados (recomeça do zero)
```

Os dados de cada serviço ficam em volumes nomeados do Docker (`postgres_primary_data`, `postgres_replica_data`, `redis_data`) — não em pastas do repositório, então não aparecem no `git status` nem precisam de entrada própria no `.gitignore`.
