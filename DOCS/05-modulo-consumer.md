# Módulo: Consumer

## Objetivo

Consumir os eventos publicados pelo Webhook no Redis Stream, persistir cada evento no banco de escrita e notificar o dashboard em tempo real de que uma nova estatística de uso chegou — com resiliência a falhas: toda mensagem é persistida antes de ser processada, falhas transitórias são reprocessadas com backoff, e nada é apagado.

## Stack e padrão de código

- **C# / ASP.NET Core**, rodando como processo independente do Webhook e da API GraphQL. Não é um Worker Service "puro" (sem host web) porque o Consumer também precisa **hospedar o endpoint do SignalR Hub** ao qual o frontend se conecta — por isso o projeto é criado como um ASP.NET Core normal (Kestrel), com o consumo do Redis rodando como um `BackgroundService` (`IHostedService`) dentro dele, e o Hub mapeado no mesmo host.
- Mesma organização em **DDD + Use Case** dos demais serviços C#.

## Duas fases: registrar, depois processar

O Consumer não processa o evento direto na leitura do Redis. Ele separa isso em duas etapas independentes, cada uma com sua própria garantia:

### Fase A — Registrar (`RegistrarEventoUseCase`)

1. Lê o Redis Stream via **consumer group** (`XREADGROUP`) — primeiro as próprias pendências do processo (`0`, cobre reinício após queda), depois mensagens novas (`>`).
2. Grava o payload **cru** (string, sem desserializar) numa tabela `usage_event_logs`, com status `Pendente` — **no máximo uma vez por entrada do Redis** (ver [Idempotência](#idempotência)).
3. Só **depois** disso confirma a leitura e remove a entrada da stream (`XACK` + `XDEL`, numa transação `MULTI/EXEC`).

Se o passo 2 falhar (ex: Postgres fora do ar), nada é confirmado — a mensagem continua pendente no Redis e é relida no próximo ciclo. A partir do momento em que a mensagem está em `usage_event_logs`, o Redis já cumpriu seu papel: a garantia de "nada se perde" passa a ser do Postgres, não mais do Redis. É por isso que a entrada pode ser apagada (`XDEL`) — e precisa ser: o backpressure do Webhook usa `XLEN`, que conta entradas confirmadas também (ver [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila)).

Dois detalhes do consumer group:

- Ele é criado a partir do **início** da stream (`0`), não só com mensagens novas (`$`): no primeiro boot, o Webhook pode aceitar eventos antes de o Consumer criar o grupo, e com `$` esses eventos nunca seriam lidos.
- Ao iniciar, o Consumer remove da stream entradas já entregues e confirmadas que tenham ficado para trás (`XTRIM MINID` até a menor pendência, ou até a última entregue se não houver pendências). Isso limpa ambientes criados antes do `XDEL` existir; pendências e mensagens não lidas nunca são tocadas. Se essa limpeza falhar, o worker segue normalmente.

### Fase B — Processar (`ProcessarEventoDeUsoUseCase`)

A cada ciclo, busca em `usage_event_logs` as linhas com status `Pendente` ou `FalhaTransiente` com retry vencido, e para cada uma:

1. Verifica se já existe um `UsageRecord` para esse `usage_event_logs` (idempotência — ver abaixo). Se existir, só marca `Processado` e sai.
2. Desserializa o payload. Se o payload estiver corrompido, é uma **falha permanente** (não adianta tentar de novo).
3. Resolve o `ProjectId` a partir do nome do Project no evento. Se não encontrar, é uma **falha permanente** — o Webhook só publica eventos com um Project que existia no momento da autenticação, então isso só aconteceria em um cenário anômalo (ex: Project apagado entre a validação e o consumo).
4. Persiste o `UsageRecord` e notifica o Hub SignalR.
5. Marca o log como `Processado`.

## Classificação de erro: transiente vs. permanente

| Tipo | Exemplos | O que acontece |
|---|---|---|
| **Permanente** | Payload corrompido, Project inexistente | Marca `FalhaPermanente` **na primeira tentativa**, não entra na fila de retry. Não adianta tentar de novo — o dado é o que é. |
| **Transiente** | Postgres momentaneamente indisponível, qualquer exceção não classificada como permanente | Marca `FalhaTransiente`, incrementa `AttemptCount`, agenda o próximo retry com backoff exponencial (`BaseDelaySeconds * 2^(tentativa-1)`: 10s, 20s, 40s, 80s...) até `MaxAttempts` (padrão 5). Depois disso também vira `FalhaPermanente` — desiste de tentar, mas a linha nunca é apagada. |

"Notificar" um erro permanente, por enquanto, significa: log em nível de erro + a linha ficando visível e consultável com status `FalhaPermanente`. Não há canal de alerta (e-mail/Slack) configurado — fica como possível evolução futura.

## Idempotência

São duas proteções, uma em cada fase — as duas são necessárias para que um mesmo evento nunca seja contado duas vezes no dashboard:

**Fase A — uma entrada do Redis gera no máximo um log.** `usage_event_logs.RedisEntryId` tem índice único, e o registro é um `INSERT ... ON CONFLICT ("RedisEntryId") DO NOTHING`. O cenário que isso cobre: o log foi gravado, mas o `XACK` falhou (Redis caiu, processo morreu entre um e outro). A entrada continua pendente e volta na leitura `0` do próximo ciclo; sem o índice único, viraria um segundo log com outro `Id` — e a proteção da Fase B, que olha o `Id` do log, não pegaria a duplicata. Com o índice, a releitura não grava nada e só refaz a confirmação. O `ON CONFLICT` (em vez de `Add` + `SaveChanges` tratando a exceção) também evita deixar uma entidade rejeitada rastreada no `DbContext` compartilhado pelo lote (ver o cuidado abaixo).

**Fase B — um log gera no máximo um `UsageRecord`.** Cada `UsageRecord` referencia o `usage_event_logs` que o originou (`SourceEventLogId`, com índice único no banco). Antes de criar um `UsageRecord`, o Use Case verifica se já existe um para aquele log — isso protege contra reprocessamento duplicado (ex: o log foi persistido e o `UsageRecord` criado com sucesso, mas o processo caiu antes de marcar o log como `Processado`; no próximo ciclo, o mesmo log seria pego de novo, e sem essa checagem duplicaria o registro).

## Um cuidado de implementação: DbContext compartilhado entre repositórios

Os repositórios que atendem `Fase B` (`IUsageRecordRepository`, `IUsageEventLogRepository`) resolvem o **mesmo** `WriteDbContext` dentro do mesmo escopo (DI). Isso tem uma armadilha real: se um `SaveChangesAsync` falhar (ex: `UsageRecord` rejeitado pelo banco), a entidade que falhou continua **rastreada** pelo change tracker do EF Core — e o próximo `SaveChangesAsync` de **qualquer outro repositório** que reutilize esse mesmo contexto tenta salvar essa entidade de novo, falhando com o mesmo erro.

Duas decisões tomadas por causa disso:

- `EfUsageRecordRepository.AddAsync` limpa o change tracker (`ChangeTracker.Clear()`) se o `SaveChangesAsync` falhar, antes de repropagar a exceção.
- As atualizações de status em `EfUsageEventLogRepository` (`MarkProcessedAsync`, `MarkTransientFailureAsync`, `MarkPermanentFailureAsync`) usam `ExecuteUpdateAsync` — um `UPDATE` direto no banco, que não depende do change tracker. Além de resolver a armadilha acima pela raiz, é uma chamada a menos ao banco (não precisa buscar a entidade antes de atualizar).

## Rede de segurança do worker

O Use Case já trata falha transiente/permanente internamente — mas se algo **inesperado** ainda assim escapar dali (um bug, uma exceção na própria lógica de tratamento de erro), isso não pode derrubar o processo inteiro. Por padrão, uma exceção não tratada dentro de um `BackgroundService` do ASP.NET Core encerra o host inteiro (`BackgroundServiceExceptionBehavior.StopHost`) — ou seja, **um único evento problemático pararia o Consumer inteiro**, o oposto do que se quer aqui. Por isso, o laço que processa cada evento tem um `try/catch` de última instância: loga como erro crítico e segue para o próximo evento, sem nunca deixar o worker parar.

Existe uma segunda camada, mais externa, cobrindo falha de **infraestrutura** (não de um evento específico): o próprio `while` do worker envolve a leitura do Redis e a checagem de pendências do Postgres num `try/catch` que loga e tenta de novo no próximo ciclo. Isso foi descoberto necessário na prática: suspender/hibernar a máquina de desenvolvimento derruba a conexão TCP com o Redis, e a primeira leitura após retomar lança uma exceção de timeout — sem essa camada externa, o worker inteiro morria por causa de uma reconexão de rede, não de um evento malformado.

## Comunicação em tempo real (SignalR)

- O Hub do SignalR expõe um canal (`UsageHub`) ao qual o frontend se conecta após montar a tela do dashboard.
- SignalR foi escolhido em vez de WebSocket "cru" por ser o padrão idiomático do ecossistema ASP.NET Core para esse cenário — evita reimplementar reconexão, fallback de transporte e gerenciamento de conexões manualmente.
- A conexão ao Hub exige o mesmo JWT emitido pelo `login` da API GraphQL (ver [06-modulo-api.md](06-modulo-api.md)). O Consumer valida esse token com a mesma chave de assinatura configurada na API GraphQL (mesmo segredo, configurado de forma independente em cada serviço via variável de ambiente — sem acoplamento de código entre os dois).
- Falha ao notificar (ex: Hub temporariamente sem conexões, erro de rede) **não** é tratada como falha de processamento — o dado já foi persistido com sucesso antes da notificação; perder um "aviso" em tempo real é aceitável, perder o dado não é. O dashboard recupera o histórico completo na próxima consulta de qualquer forma.

## Por que um serviço separado do Webhook

- O Webhook precisa responder rápido ao SDK (aceitar e enfileirar); o Consumer pode levar o tempo que for necessário para processar com segurança (persistir + notificar), sem impactar a latência percebida pela aplicação cliente do SDK.
- Os dois processos escalam de forma independente: em pico de tráfego, aumentar instâncias do Webhook não exige aumentar instâncias do Consumer na mesma proporção — a fila absorve a diferença dentro do limite descrito em [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila).

## Limitações conhecidas (aceitas por ora)

- **Reclamo de pendências entre instâncias diferentes** (`XCLAIM`/`XAUTOCLAIM`) não é implementado — se o Consumer escalar horizontalmente com múltiplos nomes de consumer, uma instância que morre no meio do processamento não tem sua pendência automaticamente assumida por outra. Hoje só há uma instância (`consumer-1`, nome fixo), então isso não é um problema — o reinício da própria instância já recupera suas pendências via leitura `0`. Revisitar se e quando o Consumer escalar horizontalmente.
- **Sem canal de alerta** para falhas permanentes — hoje é log + status consultável no banco, não uma notificação ativa (e-mail, Slack, etc.).
