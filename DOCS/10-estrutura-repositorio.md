# Estrutura do Repositório

O repositório é um **monorepo** com raiz em `proex/`. Cada projeto vive na sua própria pasta ali, junto com a pasta `DOCS/`. Os quatro serviços em C# ficam agrupados dentro de `OmniAI/` (uma solution única); os demais projetos são pastas irmãs, fora dela.

```
proex/                        # raiz do monorepo
├── DOCS/                     # esta documentação
├── docker-compose.yml        # infra local: Postgres primary/réplica + Redis (ver 12-ambiente-local.md)
├── .env.example               # template das variáveis do docker-compose.yml (.env real é gitignored)
├── .gitignore
├── OmniAI/                   # backend .NET
│   ├── OmniAI.slnx           # solution única, reúne os 4 projetos abaixo
│   ├── Webhook/               # ingestão, autenticação por API Key, rate limit, fila (projeto Webhook)
│   ├── Consumer/              # worker de fila + Hub do SignalR (projeto Consumer)
│   ├── ApiGraphQL/            # login (JWT) + estatísticas via GraphQL (projeto ApiGraphQL)
│   └── Shared/                # biblioteca compartilhada — entidades, DbContexts, contrato de evento (projeto Shared)
├── cockpit/                  # Dashboard React (Vite + TypeScript) — ver nota sobre o nome abaixo
└── sdk-node/                  # SDK Node.js/TypeScript (pacote npm "omniai-sdk")
```

**Sobre o nome `cockpit`:** é o projeto React que expõe o dashboard ao usuário final. O nome foi escolhido no lugar de algo genérico como `frontend` — remete à ideia de um painel de controle único de onde se enxerga o consumo de todos os provedores de IA, coerente com o slogan do produto ("Única visão para seus provedores de IA"). Nos demais documentos, "frontend" e "dashboard" continuam sendo usados como termos genéricos para se referir a esse componente — `cockpit/` é especificamente o nome da pasta/projeto.

## Convenções

- Cada pasta de projeto é **autossuficiente**: tem seu próprio arquivo de dependências (`package.json` ou `.csproj`), README de setup local e, se aplicável, testes.
- Os projetos em C# (`Webhook`, `Consumer`, `ApiGraphQL`) seguem a mesma organização interna em camadas (DDD + Use Case), descrita em cada documento de módulo correspondente:
  - `Domain/` — entidades e regras de negócio puras.
  - `Application/` — Use Cases, orquestram o domínio.
  - `Infrastructure/` — acesso a banco, Redis, provedores externos.
  - `Api/` (ou `Presentation/`) — controllers/endpoints/resolvers, tradução da requisição para o Use Case.
- Cada camada é uma **pasta**, não um projeto (`.csproj`) separado — evita referências de projeto internas desnecessárias para o volume de código deste sistema.
- Nomes de projeto e de pasta são os mesmos, em PascalCase (`Webhook`, `Consumer`, `ApiGraphQL`, `Shared`) — sem prefixo `OmniAI.`, já que não há risco de colisão entre eles dentro da mesma solution.
- Nenhum serviço depende do código de **outro serviço** diretamente — a comunicação entre Webhook, Consumer e API GraphQL acontece sempre por rede (HTTP, Redis), nunca por referência de projeto cruzada entre eles. Isso mantém cada serviço deployável de forma independente.

## Biblioteca compartilhada

`Shared` é a exceção à regra acima: é uma biblioteca de classes (não um serviço executável) referenciada por Webhook, Consumer e API GraphQL, contendo o que os três genuinamente compartilham:

- **Entidades e DbContexts do EF Core** — `Project`, `ApiKey`, `UsageRecord`, além de `WriteDbContext` e `ReadDbContext` (ver [08-banco-de-dados.md](08-banco-de-dados.md#como-a-aplicação-usa-os-dois-bancos)) e as migrations, que vivem exclusivamente aqui.
- **Contrato do evento da fila** — o DTO que representa o payload publicado no Redis Stream, usado pelo Webhook (produtor) e pelo Consumer (consumidor), evitando que os dois definam essa forma de dado de forma independente e ela divirja com o tempo.

| Projeto | Referencia `Shared` para |
|---|---|
| Webhook | `WriteDbContext` (validar API Key) + contrato do evento (montar a mensagem publicada) |
| Consumer | `WriteDbContext` (persistir `UsageRecord`) + contrato do evento (ler a mensagem consumida) |
| ApiGraphQL | `ReadDbContext` (query `usageStatistics`) |

Isso não contradiz a regra de independência entre serviços: nenhum dos três depende do código de **outro serviço**, todos dependem apenas de uma biblioteca comum que nenhum deles "roda" sozinha — um shared kernel, não um acoplamento serviço-a-serviço.

## Como os projetos C# foram criados

Registrado aqui para referência (ex: se for preciso recriar algum projeto). A solution vive em `OmniAI/OmniAI.slnx`, criada apontando **Location** para a pasta `OmniAI/` (dentro da raiz `proex/`) ao criar o primeiro projeto (`Webhook`), com a opção **"Place solution and project in the same directory"** desmarcada.

Os demais projetos foram adicionados à solution existente via botão direito nela, no Solution Explorer → **Add → New Project**, apontando o **Location** de cada um para dentro de `OmniAI/`:

- `Consumer` (ASP.NET Core Web API, vazio — não "Worker Service", ver [05-modulo-consumer.md](05-modulo-consumer.md#stack-e-padrão-de-código))
- `ApiGraphQL` (ASP.NET Core Web API, vazio)
- `Shared` (Class Library — **atenção**: existem dois templates parecidos, "Class Library" e "Class Library (.NET Framework)"; o correto é o primeiro, que expõe um dropdown de **Framework** na tela seguinte, onde se escolhe `.NET 10.0`)

Referências de projeto adicionadas via botão direito em cada projeto → **Add → Project Reference** → marcar `Shared` (feito em `Webhook`, `Consumer` e `ApiGraphQL`).

**Alternativa via linha de comando**, caso precise recriar algo sem depender do assistente do Visual Studio (rodando dentro de `OmniAI/`):

```
dotnet new sln -n OmniAI
dotnet new webapi -n Webhook -o Webhook
dotnet new webapi -n Consumer -o Consumer
dotnet new webapi -n ApiGraphQL -o ApiGraphQL
dotnet new classlib -n Shared -o Shared -f net10.0

dotnet sln add Webhook/Webhook.csproj Consumer/Consumer.csproj ApiGraphQL/ApiGraphQL.csproj Shared/Shared.csproj
dotnet add Webhook reference Shared/Shared.csproj
dotnet add Consumer reference Shared/Shared.csproj
dotnet add ApiGraphQL reference Shared/Shared.csproj
```

## Como o projeto React foi criado

`cockpit/` foi criado com **Vite** (padrão atual para SPA em React — Create React App está descontinuado), rodando a partir da raiz `proex/`:

```
npm create vite@latest cockpit -- --template react-ts
cd cockpit
npm install
```

## Como o SDK Node foi criado

`sdk-node/` é um pacote npm padrão (`omniai-sdk`), TypeScript, sem framework:

```
mkdir sdk-node && cd sdk-node
npm init -y
npm install -D typescript @types/node
```

`package.json` usa `"type": "module"` (ESM) e expõe `dist/index.js` + `dist/index.d.ts` como entrada pública (`npm run build` roda `tsc`). O código-fonte fica em `src/`; `dist/` é gerado no build e não é versionado.

## Infra local (Docker Compose)

`docker-compose.yml` + `.env.example` na raiz sobem Postgres (primary + réplica) e Redis para desenvolvimento — detalhado em [12-ambiente-local.md](12-ambiente-local.md). Não pertence a nenhum projeto específico porque é infraestrutura compartilhada por Webhook, Consumer e ApiGraphQL.

## Onde cada decisão está documentada

Esta pasta (`DOCS/`) é a única fonte de verdade sobre **por que** o sistema é organizado assim. Um README dentro de cada pasta de projeto (`sdk-node/README.md`, etc.) deve conter apenas instruções práticas de setup e execução local — decisões de arquitetura e regra de negócio pertencem exclusivamente aos documentos em `DOCS/`.
