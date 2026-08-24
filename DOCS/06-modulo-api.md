# Módulo: API GraphQL

## Objetivo

Expor ao frontend as duas únicas operações necessárias para o dashboard funcionar: autenticar o usuário e consultar as estatísticas de uso agregadas.

## Stack e padrão de código

- **C# / ASP.NET Core** com **HotChocolate** (biblioteca GraphQL de referência no ecossistema .NET).
- Mesma organização em **DDD + Use Case** dos demais serviços C#: cada resolver (query/mutation) delega a um Use Case; a camada GraphQL é apenas a porta de entrada.

## Por que GraphQL (e não REST) mesmo em um escopo simples

O escopo inicial tem apenas duas operações, o que por si só não exigiria GraphQL. A escolha é deliberada e olhando para a extensão natural do produto:

- O dashboard precisa evoluir com **filtros combináveis** (por período, projeto, provedor, modelo) sem multiplicar endpoints REST (`/stats/by-project`, `/stats/by-provider`, `/stats/by-period`...). Uma única query GraphQL com argumentos resolve isso de forma mais simples do que uma REST API que cresce em endpoints a cada novo filtro.
- Mantém consistência: login e estatísticas ficam sob a mesma API, um único schema, um único client GraphQL no frontend.

## Operações expostas

### `login` (mutation)

```graphql
mutation {
  login(username: "...", password: "...") {
    token
  }
}
```

- Valida as credenciais recebidas contra **usuário e senha fixos, definidos em variável de ambiente** — não há tabela de usuários, não há fluxo de cadastro ou troca de senha.
- Em caso de sucesso, retorna um **JWT** de curta duração, usado nas demais operações (header `Authorization: Bearer`) e na conexão do SignalR.
- Em caso de falha, retorna erro genérico (não indica se foi o usuário ou a senha que estava incorreta), para não facilitar enumeração de credenciais.

### `usageStatistics` (query)

```graphql
query {
  usageStatistics(filter: { project: "checkout-service", provider: "openai", from: "...", to: "..." }) {
    totalCostUsd
    totalTokens
    totalRequests
    byProvider { provider, costUsd, tokens, requests }
    byProject { project, costUsd, tokens, requests }
  }
}
```

- Requer autenticação (JWT válido).
- Lê exclusivamente do **banco de leitura** (réplica) — nunca do banco de escrita. Ver [08-banco-de-dados.md](08-banco-de-dados.md).
- Todos os filtros são opcionais; sem filtro, retorna o consumo total da empresa.
- É a query chamada pelo frontend uma única vez, ao montar a tela do dashboard, para popular o histórico inicial. Atualizações após esse ponto chegam via WebSocket (SignalR), não por nova chamada a esta query (ver [05-modulo-consumer.md](05-modulo-consumer.md#comunicação-em-tempo-real-signalr)).

## Autenticação das operações subsequentes

- O JWT emitido pelo `login` é exigido em todas as demais operações GraphQL e na conexão inicial do SignalR Hub.
- Não há refresh token nem renovação automática nesta primeira versão — expirando o token, o usuário refaz o login. Mantém o fluxo de autenticação no mínimo necessário, coerente com o princípio de simplicidade do projeto.
