# Banco de Dados

## Tecnologia

**PostgreSQL**, com as instâncias de escrita e leitura separadas fisicamente.

## Modelo de dados

| Entidade | Campos principais | Descrição |
|---|---|---|
| **Project** | `id`, `name`, `createdAt` | Uma aplicação/serviço da empresa instrumentado com o SDK. |
| **ApiKey** | `id`, `projectId`, `keyHash`, `createdAt`, `revokedAt` | Hash da chave usada pelo Projeto para autenticar no Webhook. Uma chave revogada não é apagada, apenas marcada — preserva auditoria. |
| **UsageRecord** | `id`, `projectId`, `sourceEventLogId`, `provider`, `model`, `promptTokens`, `completionTokens`, `totalTokens`, `costUsd` (nulável), `latencyMs`, `status`, `occurredAt` | Um registro por chamada de IA reportada pelo SDK. É a tabela de fatos do sistema — todo agregado exibido no dashboard é calculado a partir dela. `costUsd` vem calculado do SDK (ver [03-modulo-sdk.md](03-modulo-sdk.md)); fica `null` quando o SDK não tinha o par provedor/modelo catalogado em sua tabela de preços — o Consumer só persiste o valor recebido, não recalcula. `sourceEventLogId` (único) referencia o `UsageEventLog` que originou este registro — garante que o mesmo evento nunca gera dois registros (idempotência), mesmo se reprocessado. |
| **UsageEventLog** | `id`, `redisEntryId`, `payload` (cru), `status` (`Pendente`/`Processado`/`FalhaTransiente`/`FalhaPermanente`), `attemptCount`, `lastError`, `receivedAt`, `nextRetryAt`, `processedAt` | Registro de **todo** evento lido da fila, antes de qualquer processamento — a garantia de resiliência do Consumer (ver [05-modulo-consumer.md](05-modulo-consumer.md)). Nunca é apagado, nem em caso de falha definitiva. `redisEntryId` (único) garante que a mesma entrada do Redis, se relida, não gera um segundo log (ver [Idempotência](05-modulo-consumer.md#idempotência)). |

Não existe tabela de usuários do dashboard: a credencial de login é fixa em variável de ambiente (ver [06-modulo-api.md](06-modulo-api.md#login-mutation)), então não há necessidade de armazená-la no banco.

## Separação entre banco de escrita e banco de leitura

**Motivação:** o Consumer grava novos registros de forma constante e precisa de baixa latência para não acumular atraso na fila; o dashboard faz consultas de agregação (somas, agrupamentos por provedor/projeto/período) que podem ser mais pesadas. Rodar as duas cargas na mesma instância faz uma competir por recursos com a outra. Separar fisicamente escrita e leitura isola esse impacto.

## Como a replicação é feita

**Decisão:** usar a **replicação em streaming nativa do PostgreSQL** (streaming replication, assíncrona, com uma réplica *hot standby* somente leitura), em vez de qualquer ferramenta ou biblioteca externa de sincronização de dados.

Motivos:

- É um recurso **nativo do próprio PostgreSQL** — não introduz mais uma peça de infraestrutura (sem CDC externo, sem Debezium, sem workers de sincronização customizados), o que atende diretamente ao princípio de simplicidade do projeto.
- É o mecanismo **mais validado e usado em produção** para esse cenário — qualquer provedor gerenciado de Postgres (RDS, Cloud SQL, Azure Database, etc.) já oferece réplicas de leitura configuradas exatamente dessa forma.
- A replicação acontece **no nível da infraestrutura de banco de dados**, não da aplicação: nenhum serviço C# precisa conhecer ou implementar lógica de sincronização.

### Como a aplicação usa os dois bancos

O acesso a dados é feito através de dois `DbContext` (Entity Framework Core), definidos na biblioteca compartilhada `Shared` (ver [10-estrutura-repositorio.md](10-estrutura-repositorio.md#biblioteca-compartilhada)):

| DbContext | Aponta para | Migrations |
|---|---|---|
| `WriteDbContext` | Postgres de escrita (primary) | Sim — é o único lugar onde migrations rodam. A réplica recebe o schema automaticamente via streaming replication; nunca se roda uma migration diretamente nela. |
| `ReadDbContext` | Postgres de leitura (réplica) | Não |

| Serviço | DbContext usado | Motivo |
|---|---|---|
| Webhook | `WriteDbContext` | Valida a API Key contra `Project`/`ApiKey` no primary, não na réplica. É uma checagem de segurança: uma chave revogada precisa parar de funcionar imediatamente, e a réplica tem uma janela de atraso (ver [Consistência eventual](#consistência-eventual)) incompatível com esse requisito. Como é uma busca pontual por índice (não uma agregação), o custo extra no primary é desprezível. |
| Consumer | `WriteDbContext` | Persiste cada `UsageRecord` recebido da fila. |
| API GraphQL | `ReadDbContext` **e** `WriteDbContext` | `ReadDbContext` serve `usageStatistics` e `projects` (leituras de agregação/exibição). `WriteDbContext` serve `createProject`, `createApiKey` e `revokeApiKey` — são escritas administrativas, de frequência baixíssima (alguém cadastrando um projeto ocasionalmente), então não competem de forma relevante com a ingestão de `UsageRecord` que é o motivo original de separar escrita e leitura. É o único serviço com os dois contextos ao mesmo tempo — cada operação usa o que faz sentido pra ela, não um só por padrão. |

Não existe roteamento dinâmico de query nem um proxy de banco — cada serviço já é configurado, via variável de ambiente, com a connection string do papel que lhe cabe. A escolha de qual `DbContext` usar é uma decisão de código (qual classe é injetada), não uma decisão em tempo de execução.

### Consistência eventual

Por ser replicação assíncrona, existe uma janela pequena (tipicamente milissegundos) entre o Consumer gravar um evento e ele aparecer na réplica de leitura. Isso é aceitável para o domínio do produto — estatísticas de custo e uso não exigem consistência forte imediata — e é o mesmo motivo pelo qual o dashboard também recebe os eventos mais recentes via WebSocket (que não depende da réplica), cobrindo a janela de atraso da replicação com a atualização em tempo real.
