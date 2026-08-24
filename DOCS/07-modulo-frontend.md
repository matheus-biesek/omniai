# Módulo: Frontend (Dashboard)

## Objetivo

Interface web onde um líder técnico visualiza, de forma simples, o consumo de IA da empresa — geral, por aplicação (projeto) e por provedor — sem exigir conhecimento prévio das regras de negócio da plataforma.

## Stack

- **React**, seguindo padrões modernos de organização de aplicação (componentização por responsabilidade, separação clara de camadas de UI, dados e regras de apresentação).
- Client GraphQL para consumir a [API GraphQL](06-modulo-api.md) e client SignalR para a conexão em tempo real.

## Telas

### Login

- Formulário simples (usuário e senha).
- Envia a mutation `login`; em caso de sucesso, armazena o JWT e redireciona ao dashboard.
- Em caso de falha, exibe mensagem genérica de erro (ver [06-modulo-api.md](06-modulo-api.md#login-mutation)).

### Dashboard

- **Ao montar a tela:**
  1. Executa a query `usageStatistics` para obter o histórico agregado (consumo total, por projeto, por provedor).
  2. Renderiza os dados recebidos.
  3. Abre a conexão WebSocket (SignalR) autenticada com o JWT.
- **Após a conexão em tempo real estabelecida:** cada evento de uso recebido do Hub atualiza o estado local de forma incremental — soma ao total já exibido, sem refazer a query GraphQL inteira.
- **Filtros:** o usuário pode filtrar a visualização por projeto, provedor e período. Aplicar um filtro reexecuta a query `usageStatistics` com os argumentos correspondentes.

## Estrutura de pastas (proposta)

```
src/
  app/                # bootstrap da aplicação, providers (GraphQL client, auth, roteamento)
  pages/
    login/
    dashboard/
  components/         # componentes de UI reutilizáveis (ex: cartão de estatística, tabela, filtro)
  features/
    auth/             # lógica de autenticação (login, guarda de rota, armazenamento de token)
    usage-stats/       # query GraphQL, hook de assinatura em tempo real, tipos de domínio
  shared/
    graphql/           # client GraphQL, queries/mutations tipadas
    realtime/          # client SignalR e hooks de assinatura
    ui/                 # design system básico (botão, input, layout)
```

- **`features/`** agrupa por caso de uso de negócio (autenticação, estatísticas de uso), não por tipo técnico — evita que um componente de UI fique desacoplado da lógica que o alimenta.
- **`components/`** e **`shared/ui/`** guardam apenas peças de interface sem regra de negócio, reutilizáveis entre telas.

## Variáveis de ambiente

O frontend não guarda segredo nenhum — tudo que roda no navegador é, por definição, público. As variáveis de ambiente aqui existem só para apontar para onde estão os serviços de backend, e mudam entre desenvolvimento e produção:

| Variável | Uso |
|---|---|
| `VITE_GRAPHQL_URL` | Endpoint da [API GraphQL](06-modulo-api.md) (`/graphql`) |
| `VITE_HUB_URL` | Endpoint do Hub SignalR do [Consumer](05-modulo-consumer.md#comunicação-em-tempo-real-signalr) (`/hub`) |

Prefixo `VITE_` é exigido pelo Vite — só variáveis com esse prefixo são expostas ao código do navegador (`import.meta.env.VITE_...`); qualquer outra fica só disponível durante o build, nunca no bundle final.

`cockpit/.env` é **versionado**, com os valores de desenvolvimento local (mesmas portas de [12-ambiente-local.md](12-ambiente-local.md#portas-dos-serviços-fora-do-docker-compose)) — não é segredo, então não há razão para escondê-lo, e quem clona o repositório já sobe com tudo funcionando. Em produção, o valor é definido no build/deploy (variável de ambiente do serviço de hospedagem do frontend), sobrescrevendo o `.env` do repositório.

## Princípios de UX

- Uma única tela concentra a informação principal — sem necessidade de navegação entre múltiplos dashboards para responder "quanto estamos gastando com IA".
- Filtros são complementares, não obrigatórios: a tela já nasce útil sem que o usuário precise configurar nada.
- Atualizações em tempo real são visuais e não disruptivas (não recarregam a tela nem perdem o estado de filtros aplicados).
