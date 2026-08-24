# Segurança

Este documento consolida todas as decisões de segurança do projeto. Os módulos individuais referenciam este documento em vez de repetir a justificativa.

## Autenticação do Webhook (SDK → Webhook)

- **Mecanismo:** API Key por Projeto, enviada no header `X-Api-Key`.
- **Armazenamento:** apenas o **hash HMAC-SHA256** da chave é persistido — nunca a chave em texto plano, nunca de forma reversível.

### API Key: hash em vez de criptografia reversível

O texto original do projeto descrevia a API Key como "criptografada" no banco. A decisão registrada aqui é usar **hash** (função de mão única), não **criptografia reversível** (ex: AES), pelo seguinte motivo:

> A aplicação nunca precisa **ler de volta** a API Key em texto plano depois de criada — ela só precisa **comparar** a chave recebida numa requisição com a que foi cadastrada. Isso é exatamente o mesmo problema de senhas: a solução correta é uma função de hash (com uma chave de assinatura secreta do servidor, um "pepper", para o HMAC), nunca uma criptografia que alguém com acesso ao banco e à chave de criptografia conseguiria reverter.

Consequência prática: a chave em texto plano é exibida ao usuário **uma única vez**, no momento da criação, no dashboard. Se for perdida, o fluxo é gerar uma nova chave e revogar a antiga — não existe "mostrar a chave novamente".

## Rate limiting

Duas camadas independentes, ambas no Webhook:

1. **Por IP de origem** — middleware nativo do ASP.NET Core (`Microsoft.AspNetCore.RateLimiting`), limitando requisições por janela de tempo por IP. Mitiga abuso e tentativas de força bruta contra a API Key.
2. **Backpressure da fila** — verificação do tamanho atual do Redis Stream (`XLEN`, O(1)) antes de aceitar um novo evento. Detalhado em [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila). Não é um rate limit por identidade, e sim uma proteção de capacidade do sistema como um todo.

## Validação de entrada

Toda borda do sistema que recebe dado externo valida antes de processar:

- **Webhook:** valida schema do payload (tipos, campos obrigatórios, valores permitidos como `provider` conhecido, `costUsd`/tokens não-negativos) antes de publicar na fila.
- **API GraphQL:** validação de tipos é garantida pelo próprio schema GraphQL; regras de negócio adicionais (ex: intervalo de datas do filtro) são validadas no Use Case correspondente.

## Autenticação do Dashboard (login)

- Credencial única (usuário/senha) definida em variável de ambiente do serviço da API — não há tabela de usuários no banco, então não há senha para vazar em caso de comprometimento do banco de dados.
- Login bem-sucedido emite um **JWT de curta duração**, exigido em todas as demais chamadas GraphQL e na conexão SignalR.
- Mensagens de erro de login são genéricas (não revelam se o usuário ou a senha estavam incorretos), para dificultar enumeração.

## Segredos e configuração

Duas situações diferentes, com regras diferentes — a distinção importa especialmente por este ser um projeto Open Source, onde alguém clonando o repositório precisa enxergar quais chaves de configuração existem sem precisar caçar em nenhum lugar escondido:

### Em desenvolvimento local

`appsettings.Development.json` de cada serviço C# é **versionado** e contém valores reais — porque não são segredos de verdade, são credenciais descartáveis que só existem no Postgres/Redis do Docker Compose local (mesmos valores de `.env.example`, ver [12-ambiente-local.md](12-ambiente-local.md)). O objetivo é onboarding: `git clone` + `docker compose up` + `dotnet run` funcionam de primeira, sem nenhum passo manual de configurar segredo.

| Configuração | Onde vive (dev) |
|---|---|
| Connection strings (Postgres escrita/leitura, Redis) | `appsettings.Development.json` de cada serviço |
| Chave HMAC (pepper) para hash de API Keys | `appsettings.Development.json` do Webhook |
| Usuário/senha do login do dashboard, chave de assinatura do JWT | `appsettings.Development.json` da API GraphQL |

### Em produção

`appsettings.json` (o arquivo base, sem sufixo de ambiente) **nunca** contém valor real para nada da tabela acima — só a estrutura/chaves quando fizer sentido documentar o formato esperado. Os valores reais são fornecidos por **variável de ambiente** no ambiente de deploy (o ASP.NET Core sobrescreve configuração automaticamente via variáveis como `ConnectionStrings__WriteDatabase`, sem precisar de código extra para isso) — nunca em arquivo, nunca em `appsettings.Production.json` versionado.

Resumindo a regra: **segredo de desenvolvimento pode e deve estar no repositório, porque não é segredo de verdade; segredo de produção nunca está no repositório, em hipótese nenhuma.**

## Transporte

- Toda comunicação entre SDK, Webhook, API GraphQL e Frontend deve ocorrer sobre **HTTPS/WSS** em qualquer ambiente que não seja desenvolvimento local.

## Superfícies de ataque consideradas

| Ameaça | Mitigação |
|---|---|
| Vazamento do banco de dados expõe API Keys | Apenas hash é armazenado — não há chave para vazar em texto plano. |
| Força bruta de API Key | Rate limit por IP no Webhook. |
| Sobrecarga do Consumer/fila (DoS por volume) | Backpressure via `XLEN` rejeita novos eventos além do limite configurado. |
| Enumeração de credenciais de login | Mensagem de erro genérica no `login`. |
| Payload malformado ou malicioso no Webhook | Validação de schema antes de qualquer processamento ou persistência. |
