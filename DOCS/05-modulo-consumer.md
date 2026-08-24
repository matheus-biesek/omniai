# Módulo: Consumer

## Objetivo

Consumir os eventos publicados pelo Webhook no Redis Stream, persistir cada evento no banco de escrita e notificar o dashboard em tempo real de que uma nova estatística de uso chegou.

## Stack e padrão de código

- **C# / ASP.NET Core**, rodando como processo independente do Webhook e da API GraphQL. Não é um Worker Service "puro" (sem host web) porque o Consumer também precisa **hospedar o endpoint do SignalR Hub** ao qual o frontend se conecta — por isso o projeto é criado como um ASP.NET Core normal (Kestrel), com o consumo do Redis rodando como um `BackgroundService` (`IHostedService`) dentro dele, e o Hub mapeado no mesmo host.
- Mesma organização em **DDD + Use Case** dos demais serviços C#: o `BackgroundService` traduz a mensagem recebida do Redis em um comando e delega a um Use Case (`ProcessarEventoDeUsoUseCase` ou equivalente).

## Consumo da fila

O Consumer lê o Redis Stream através de um **consumer group** (`XREADGROUP`), o que garante:

- **Processamento único por evento** — mesmo que existam múltiplas instâncias do Consumer rodando (escala horizontal), o Redis distribui as mensagens entre elas sem duplicar o processamento.
- **Confirmação explícita (`XACK`)** — o evento só é removido da fila pendente depois que o Consumer confirma que terminou de processá-lo. Se o Consumer cair no meio do processamento, o evento continua pendente e é reentregue, evitando perda de dados.

## Fluxo de processamento de cada evento

1. Ler o próximo evento pendente do Stream (via consumer group).
2. Validar e desserializar o payload (`UsageEventMessage`, ver [10-estrutura-repositorio.md](10-estrutura-repositorio.md#biblioteca-compartilhada)).
3. Persistir o evento como um registro de uso (`UsageRecord`) no Postgres de **escrita** — `costUsd` é gravado exatamente como veio do SDK (ver [03-modulo-sdk.md](03-modulo-sdk.md)), o Consumer não recalcula nada.
4. Publicar o evento recém-persistido no **SignalR Hub**, para que o dashboard conectado receba a atualização em tempo real.
5. Confirmar o processamento (`XACK`) junto ao Redis.

Se qualquer etapa entre 2 e 4 falhar, o `XACK` não é enviado — o evento permanece pendente na fila para nova tentativa, evitando perda de dado por falha transitória (ex: banco temporariamente indisponível).

## Comunicação em tempo real (SignalR)

- O Consumer usa **SignalR** (recurso nativo do ASP.NET Core para comunicação em tempo real) para publicar cada novo evento de uso assim que ele é persistido.
- O Hub do SignalR expõe um canal (`UsageHub`) ao qual o frontend se conecta após montar a tela do dashboard.
- SignalR foi escolhido em vez de WebSocket "cru" por ser o padrão idiomático do ecossistema ASP.NET Core para esse cenário — evita reimplementar reconexão, fallback de transporte e gerenciamento de conexões manualmente.
- A conexão ao Hub exige o mesmo JWT emitido pelo `login` da API GraphQL (ver [06-modulo-api.md](06-modulo-api.md)). O Consumer valida esse token com a mesma chave de assinatura configurada na API GraphQL (mesmo segredo, configurado de forma independente em cada serviço via variável de ambiente — sem acoplamento de código entre os dois).

## Por que um serviço separado do Webhook

- O Webhook precisa responder rápido ao SDK (aceitar e enfileirar); o Consumer pode levar o tempo que for necessário para processar com segurança (persistir + notificar), sem impactar a latência percebida pela aplicação cliente do SDK.
- Os dois processos escalam de forma independente: em pico de tráfego, aumentar instâncias do Webhook não exige aumentar instâncias do Consumer na mesma proporção — a fila absorve a diferença dentro do limite descrito em [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila).
