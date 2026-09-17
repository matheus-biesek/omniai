# Módulo: API GraphQL

## Objetivo

Expor ao frontend tudo que o dashboard precisa: autenticar o usuário, consultar as estatísticas de uso agregadas, e gerenciar os Projetos e API Keys que o SDK usa para se autenticar no Webhook (ver [09-seguranca.md](09-seguranca.md)).

## Stack e padrão de código

- **C# / ASP.NET Core** com **HotChocolate** (biblioteca GraphQL de referência no ecossistema .NET).
- Mesma organização em **DDD + Use Case** dos demais serviços C#: cada resolver (query/mutation) delega a um Use Case; a camada GraphQL é apenas a porta de entrada.

```
ApiGraphQL/
├── Domain/
│   ├── UsageStatisticsFilter.cs / UsageStatisticsResult.cs
│   ├── ProjectDirectory.cs                   # ProjectSummary, ApiKeySummary (modelo de leitura p/ listagem)
│   ├── Exceptions/                            # ProjectNameAlreadyExists, ProjectNotFound, ApiKeyNotFound
│   └── Abstractions/
│       ├── IJwtTokenGenerator.cs
│       ├── IUsageStatisticsRepository.cs
│       ├── IProjectRepository.cs              # escrita (WriteDbContext)
│       ├── IApiKeyRepository.cs                # escrita (WriteDbContext)
│       ├── IProjectDirectoryQuery.cs           # leitura (ReadDbContext)
│       └── IUnitOfWork.cs
├── Application/
│   ├── Login/
│   │   ├── DashboardCredentialsOptions.cs   # config Dashboard:Username/Password
│   │   ├── InvalidCredentialsException.cs
│   │   └── LoginUseCase.cs
│   ├── UsageStatistics/GetUsageStatisticsUseCase.cs
│   ├── CreateProject/CreateProjectUseCase.cs   # cria Project + primeira ApiKey, numa transação só
│   ├── CreateApiKey/CreateApiKeyUseCase.cs      # nova chave para um Project existente
│   ├── RevokeApiKey/RevokeApiKeyUseCase.cs
│   └── ListProjects/ListProjectsUseCase.cs
├── Infrastructure/
│   ├── JwtOptions.cs / JwtTokenGenerator.cs
│   ├── EfUsageStatisticsRepository.cs        # ReadDbContext, 3 agregações (total, por provedor, por projeto)
│   ├── EfProjectRepository.cs / EfApiKeyRepository.cs   # WriteDbContext
│   ├── EfProjectDirectoryQuery.cs             # ReadDbContext
│   └── EfUnitOfWork.cs
└── Types/
    ├── Query.cs (ping, usageStatistics, projects)
    ├── Mutation.cs (login, createProject, createApiKey, revokeApiKey)
    ├── ClaimsPrincipalExtensions.cs            # RequireAuthenticated(), reutilizado em todo resolver protegido
    └── Inputs/UsageStatisticsFilterInput.cs
```

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
- A comparação de usuário e senha é de **tempo constante** (hash SHA-256 dos dois lados + `CryptographicOperations.FixedTimeEquals`, sem curto-circuito entre usuário e senha): o tempo de resposta não revela se o usuário estava certo nem quanto da senha bate.
- **Limite de tentativas por IP** (`LoginRateLimit:PermitLimit` por `LoginRateLimit:WindowSeconds`, padrão 10 por 60s, janela fixa). Toda tentativa conta, certa ou errada, e o limite é checado **antes** das credenciais — durante o bloqueio, até a senha certa é recusada com `"Muitas tentativas de login. Aguarde um minuto e tente novamente."`; do contrário, quem está chutando senhas continuaria descobrindo quando acertou. O limite é aplicado dentro do `LoginUseCase` (`ILoginAttemptLimiter`), não pelo middleware de rate limit do ASP.NET Core: o GraphQL tem um único endpoint, e limitar o `/graphql` inteiro travaria o dashboard, que dispara uma query a cada tecla digitada no filtro.

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

### `createProject` (mutation)

```graphql
mutation {
  createProject(name: "checkout-service") {
    projectId
    projectName
    apiKey
  }
}
```

- Requer autenticação.
- Cria o `Project` e a **primeira** `ApiKey` associada a ele, numa única transação — um projeto sem nenhuma chave não serve pra nada, então os dois são criados juntos ou nenhum dos dois (ver [`CreateProjectUseCase`](#estrutura-de-implementação) e o `IUnitOfWork`).
- `apiKey` no retorno é a chave em **texto plano** — é a única vez que ela existe assim; o banco só guarda o hash (ver [09-seguranca.md](09-seguranca.md#api-key-hash-em-vez-de-criptografia-reversível)). É essa chave que o SDK usa no header `X-Api-Key` ao chamar o Webhook.
- Nome de projeto duplicado retorna um erro claro ("Já existe um projeto chamado '...'") em vez de deixar vazar a exceção de constraint do banco.

### `createApiKey` (mutation)

```graphql
mutation {
  createApiKey(projectId: "...") {
    apiKeyId
    apiKey
  }
}
```

- Requer autenticação. Gera uma **chave adicional** para um Project já existente (ex: girar a chave sem derrubar a anterior imediatamente, ou uma chave por ambiente). `projectId` inexistente retorna erro claro.

### `revokeApiKey` (mutation)

```graphql
mutation {
  revokeApiKey(apiKeyId: "...")
}
```

- Requer autenticação. Marca a chave como revogada (`revokedAt`) — nunca apaga a linha, preserva auditoria (ver [08-banco-de-dados.md](08-banco-de-dados.md)). O efeito é imediato: o Webhook usa o mesmo banco de escrita para validar, então a próxima requisição com essa chave já é rejeitada (`401`) — testado na prática durante a implementação. `apiKeyId` inexistente retorna erro claro; revogar uma chave já revogada não faz nada (idempotente).

### `projects` (query)

```graphql
query {
  projects {
    id
    name
    apiKeys { id createdAt revokedAt }
  }
}
```

- Requer autenticação. Lista todos os Projects com suas API Keys (nunca a chave em si — só metadados: quando foi criada, se/quando foi revogada). É como o dashboard mostra o que existe e permite revogar/girar chaves.
- Lê do banco de leitura (réplica), como `usageStatistics` — é uma consulta de exibição, não uma checagem de segurança, então a réplica é apropriada aqui (diferente da validação do Webhook, que usa o banco de escrita — ver [08-banco-de-dados.md](08-banco-de-dados.md#como-a-aplicação-usa-os-dois-bancos)).

## Autenticação das operações subsequentes

- O JWT emitido pelo `login` é exigido em todas as demais operações GraphQL e na conexão inicial do SignalR Hub.
- Não há refresh token nem renovação automática nesta primeira versão — expirando o token, o usuário refaz o login. Mantém o fluxo de autenticação no mínimo necessário, coerente com o princípio de simplicidade do projeto.

### Como a exigência de autenticação é implementada

Todo resolver que exige login (`usageStatistics`, `projects`, `createProject`, `createApiKey`, `revokeApiKey`) recebe um `ClaimsPrincipal` injetado diretamente como parâmetro (suporte nativo do HotChocolate, preenchido a partir do `HttpContext.User` que o middleware `UseAuthentication()` já popula a partir do JWT) e chama `claimsPrincipal.RequireAuthenticated()` — um método de extensão (`Types/ClaimsPrincipalExtensions.cs`) que lança uma exceção genérica se não autenticado. Centralizar nessa extensão evita repetir a mesma checagem em cada resolver.

**Isso não é o padrão mais idiomático do HotChocolate** — o esperado seria o atributo `[HotChocolate.Authorization.Authorize]` no resolver, com `.AddAuthorizationCore()` no builder do GraphQL Server. Essa abordagem foi tentada primeiro e descartada: com o pacote `HotChocolate.Authorization` 16.6.1 (mesma versão do `HotChocolate.AspNetCore` usado aqui), habilitar `AddAuthorizationCore()` — mesmo sem nenhum `[Authorize]` em uso — faz **toda e qualquer query falhar** com `"Unexpected Execution Error"` (HTTP 500), incluindo campos triviais sem relação nenhuma com autorização. O erro não aparece em log nenhum (nem em `ILogger`, nem no filtro de erro registrado via `AddErrorFilter` — a falha acontece num estágio anterior à execução dos resolvers). Não foi encontrada uma combinação de configuração que fizesse `AddAuthorizationCore()` funcionar nessa versão.

A checagem manual via `ClaimsPrincipal` evita esse subsistema por completo, com o mesmo resultado prático (campo continua exigindo um JWT válido) e bem menos código do que seria necessário para investigar a fundo um possível bug da biblioteca. Se o pacote `HotChocolate.Authorization` receber uma correção em versão futura, vale reavaliar — a mensagem de erro genérica (`"Autenticação necessária."`) já é mapeada no `AddErrorFilter`, então trocar de mecanismo não muda o contrato observado pelo cliente.
