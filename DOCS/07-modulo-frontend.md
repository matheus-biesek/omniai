# Módulo: Frontend (Dashboard)

## Objetivo

Interface web onde um líder técnico visualiza, de forma simples, o consumo de IA da empresa — geral, por aplicação (projeto) e por provedor — sem exigir conhecimento prévio das regras de negócio da plataforma.

## Stack

- **React + Vite + TypeScript**, com **react-router-dom** para roteamento entre telas.
- **`graphql-request`** como client GraphQL — não Apollo Client nem TanStack Query. Decisão deliberada: o Cockpit só precisa de query/mutation simples e uma assinatura em tempo real via SignalR (não via GraphQL Subscriptions), então o cache/normalização de um client mais pesado não paga o custo de complexidade extra. Cada necessidade de dado vira um hook próprio em `features/*/use*.ts` (`useProjects`, `useUsageStatistics`, etc.), que chama `client.request(...)` diretamente.
- **`@microsoft/signalr`** para a conexão em tempo real com o Hub do [Consumer](05-modulo-consumer.md#comunicação-em-tempo-real-signalr).

## Telas

### Login

- Formulário simples (usuário e senha), em `pages/login/LoginPage.tsx`.
- Envia a mutation `login` via `features/auth/useLogin.ts`; em caso de sucesso, armazena o JWT (`features/auth/authStorage.ts`) e redireciona ao dashboard.
- Em caso de falha, exibe mensagem genérica de erro (ver [06-modulo-api.md](06-modulo-api.md#login-mutation)).
- Rotas de dashboard/projetos são protegidas por `app/ProtectedRoute.tsx`, que redireciona para `/login` se não houver token.

### Dashboard

- **Ao montar a tela** (`features/usage-stats/useUsageStatistics.ts`):
  1. Executa a query `usageStatistics` para obter o histórico agregado (consumo total, por projeto, por provedor).
  2. Renderiza os dados recebidos.
  3. Abre a conexão WebSocket (SignalR) autenticada com o JWT.
- **Após a conexão em tempo real estabelecida:** cada evento `UsageReceived` do Hub é mesclado ao estado local de forma incremental (`features/usage-stats/mergeUsageEvent.ts`) — soma ao total já exibido e ao grupo de projeto/provedor correspondente, sem refazer a query GraphQL inteira. Se a conexão SignalR falhar ao abrir, o dashboard segue funcional com o histórico já carregado (falha silenciosa, sem quebrar a tela).
- **Filtros:** o usuário pode filtrar a visualização por projeto e provedor. Aplicar um filtro reexecuta a query `usageStatistics` com os argumentos correspondentes; o filtro ativo também é usado para decidir se um evento em tempo real recebido pertence à visualização atual antes de mesclá-lo.

### Projetos

- Lista os projetos existentes e suas API Keys (`features/projects/useProjects.ts`), com status de cada chave (ativa / revogada em `<data>`).
- Formulário de criação de projeto (`features/projects/useCreateProject.ts`, mutation `createProject`) e botão de gerar nova chave por projeto (`features/projects/useCreateApiKey.ts`, mutation `createApiKey`).
- A chave em texto plano retornada por `createProject`/`createApiKey` é exibida **uma única vez**, em destaque, com aviso de que não será mostrada novamente (consistente com a decisão de hash irreversível — ver [09-seguranca.md](09-seguranca.md#api-key-hash-em-vez-de-criptografia-reversível)).
- Botão de revogação por chave (`features/projects/useRevokeApiKey.ts`, mutation `revokeApiKey`).

## Estrutura de pastas (as construída)

```
src/
  app/
    AuthProvider.tsx     # contexto de autenticação (token, login, logout)
    ProtectedRoute.tsx   # guarda de rota — redireciona para /login sem token
    AppShell.tsx          # layout comum às telas autenticadas (cabeçalho, navegação, sair)
  pages/
    login/LoginPage.tsx
    dashboard/DashboardPage.tsx
    projects/ProjectsPage.tsx
  features/
    auth/                 # authStorage (sessionStorage), useLogin
    usage-stats/           # tipos, mergeUsageEvent (puro), useUsageStatistics
    projects/               # tipos, useProjects, useCreateProject, useCreateApiKey, useRevokeApiKey
  shared/
    graphql/                 # client GraphQL (graphql-request) e extração de mensagem de erro
    realtime/                 # factory da conexão SignalR
    ui/                         # Button, Input, Card — design system mínimo, sem regra de negócio
  App.tsx                        # roteamento (react-router-dom) + AuthProvider
  main.tsx                        # bootstrap
```

- **`features/`** agrupa por caso de uso de negócio (autenticação, estatísticas de uso, projetos), não por tipo técnico — evita que um componente de UI fique desacoplado da lógica que o alimenta. Cada hook de `features/*` já encapsula client GraphQL, estado de loading/erro e a chamada em si; as páginas em `pages/` só compõem hooks e componentes de `shared/ui`.
- **`shared/ui/`** guarda apenas peças de interface sem regra de negócio, reutilizáveis entre telas.

## Variáveis de ambiente

O frontend não guarda segredo nenhum — tudo que roda no navegador é, por definição, público. As variáveis de ambiente aqui existem só para apontar para onde estão os serviços de backend, e mudam entre desenvolvimento e produção:

| Variável | Uso |
|---|---|
| `VITE_GRAPHQL_URL` | Endpoint da [API GraphQL](06-modulo-api.md) (`/graphql`) |
| `VITE_HUB_URL` | Endpoint do Hub SignalR do [Consumer](05-modulo-consumer.md#comunicação-em-tempo-real-signalr) (`/hub`) |

Prefixo `VITE_` é exigido pelo Vite — só variáveis com esse prefixo são expostas ao código do navegador (`import.meta.env.VITE_...`); qualquer outra fica só disponível durante o build, nunca no bundle final.

`cockpit/.env` é **versionado**, com os valores de desenvolvimento local (mesmas portas de [12-ambiente-local.md](12-ambiente-local.md#portas-dos-serviços-fora-do-docker-compose)) — não é segredo, então não há razão para escondê-lo, e quem clona o repositório já sobe com tudo funcionando. Em produção, o valor é definido no build/deploy (variável de ambiente do serviço de hospedagem do frontend), sobrescrevendo o `.env` do repositório.

## Armazenamento do token

O JWT é guardado em `sessionStorage` (`features/auth/authStorage.ts`) e enviado via header `Authorization: Bearer` em toda chamada GraphQL e na conexão SignalR. Essa é uma limitação conhecida e documentada, não um descuido — ver [09-seguranca.md](09-seguranca.md#autenticação-do-cockpit-frontend-limitação-conhecida) para o raciocínio completo e o caminho de evolução recomendado para produção.

## Princípios de UX

- Uma única tela concentra a informação principal — sem necessidade de navegação entre múltiplos dashboards para responder "quanto estamos gastando com IA".
- Filtros são complementares, não obrigatórios: a tela já nasce útil sem que o usuário precise configurar nada.
- Atualizações em tempo real são visuais e não disruptivas (não recarregam a tela nem perdem o estado de filtros aplicados).
