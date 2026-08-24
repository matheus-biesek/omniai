# Módulo: Webhook

## Objetivo

Receber os eventos de métrica enviados pelo SDK, validar quem está enviando e o que está sendo enviado, e publicar o evento em uma fila (Redis Stream) o mais rápido possível — sem processamento pesado e sem gravação direta no banco de dados.

## Stack e padrão de código

- **C# / ASP.NET Core** (Minimal API ou Controllers — a escolher na implementação, mantendo o endpoint único e enxuto).
- Organização em **DDD + Use Case**: o endpoint HTTP apenas traduz a requisição em um comando e delega a um Use Case (`ReceberMetricaUseCase` ou equivalente); toda regra de negócio (validação de API Key, validação de payload, decisão de enfileirar) vive na camada de aplicação/domínio, não no controller.

## Pipeline de uma requisição

A requisição passa pelas etapas abaixo, nesta ordem. Se qualquer etapa falhar, a requisição é rejeitada imediatamente e as etapas seguintes não são executadas.

1. **Rate limit por IP** — middleware nativo do ASP.NET Core (`Microsoft.AspNetCore.RateLimiting`), limitando quantas requisições um mesmo IP pode fazer por janela de tempo. Protege contra abuso e ataques de força bruta na API Key.
2. **Autenticação da API Key** — ver [Autenticação](#autenticação-por-api-key) abaixo.
3. **Validação do payload** — os campos descritos em [03-modulo-sdk.md](03-modulo-sdk.md#payload-enviado-ao-webhook) são obrigatórios e validados quanto a tipo/formato (ex: `costUsd` não pode ser negativo quando presente, `provider` precisa ser um valor conhecido).
4. **Verificação de backpressure da fila** — ver [Backpressure da fila](#backpressure-da-fila) abaixo.
5. **Publicação no Redis Stream** — o evento é serializado e publicado via `XADD`. O Webhook responde `202 Accepted` ao SDK assim que a publicação é confirmada.

## Autenticação por API Key

- Cada **Projeto** cadastrado no OmniAI possui uma API Key própria.
- A requisição envia a chave no header `X-Api-Key`.
- **A API Key nunca é armazenada em texto plano nem de forma reversível.** O banco armazena apenas um **hash HMAC-SHA256** da chave (chave de assinatura do HMAC mantida como segredo do servidor, fora do banco). Na validação, o Webhook calcula o hash da chave recebida e compara com o hash salvo — nunca há necessidade de "descriptografar" a API Key, o que reduz a superfície de risco em caso de vazamento do banco.
  - A chave em texto plano só existe uma vez: no momento em que é gerada e exibida ao usuário no dashboard. Depois disso, não pode mais ser recuperada — apenas revogada e substituída por uma nova.
  - Justificativa dessa escolha vs. criptografia reversível está detalhada em [09-seguranca.md](09-seguranca.md#api-key-hash-em-vez-de-criptografia-reversível).
- Se a chave não corresponder a nenhum Projeto ativo, a requisição é rejeitada com `401 Unauthorized`.

## Backpressure da fila

**Problema a resolver:** se o Consumer não conseguir processar os eventos na mesma velocidade em que chegam, a fila do Redis cresce indefinidamente. Precisamos impedir que o Webhook aceite mais eventos quando a fila já está no limite, sem adicionar lógica pesada ao caminho crítico da requisição.

**Solução:** antes de publicar um novo evento, o Webhook consulta o **tamanho atual da fila** com o comando `XLEN` do Redis Stream. Esse comando é **O(1)** — não percorre a fila, apenas lê um contador mantido internamente pelo Redis — então a verificação é praticamente instantânea e não adiciona custo perceptível à requisição.

```
tamanhoAtual = XLEN stream:usage-events

se tamanhoAtual >= LIMITE_MAXIMO_FILA:
    responder 503 Service Unavailable (fila cheia, tentar novamente mais tarde)
senão:
    XADD stream:usage-events ...evento
    responder 202 Accepted
```

- `LIMITE_MAXIMO_FILA` é configurável via variável de ambiente, ajustado conforme a capacidade de processamento do Consumer.
- Essa checagem **não precisa de nenhum algoritmo complexo, cache adicional ou serviço externo** — o próprio Redis já mantém o contador de tamanho da stream de forma eficiente; o Webhook só faz uma leitura desse contador a cada requisição.
- Um segundo rate limit (distinto do rate limit por IP do passo 1) pode ser aplicado especificamente sobre a taxa de publicação na fila, se necessário — mas a checagem de `XLEN` já cobre o cenário principal (fila cheia) sem exigir uma camada extra.

## Por que Redis Streams (e não uma lista simples)

- `XLEN` dá o tamanho da fila em O(1), igual uma lista (`LLEN`), mas o Stream também já suporta **consumer groups** nativamente — o que o Consumer usa para processar os eventos com confirmação (`XACK`) e sem perda em caso de reinício do serviço (ver [05-modulo-consumer.md](05-modulo-consumer.md)).
- Evita a necessidade de uma fila dedicada (RabbitMQ, Kafka) para um volume e uma necessidade que o Redis já resolve de forma nativa, mantendo a infraestrutura simples.
