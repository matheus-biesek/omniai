# Segurança

Este documento consolida todas as decisões de segurança do projeto. Os módulos individuais referenciam este documento em vez de repetir a justificativa.

## Autenticação do Webhook (SDK → Webhook)

- **Mecanismo:** API Key por Projeto, enviada no header `X-Api-Key`.
- **Armazenamento:** apenas o **hash HMAC-SHA256** da chave é persistido — nunca a chave em texto plano, nunca de forma reversível.

### API Key: hash em vez de criptografia reversível

O texto original do projeto descrevia a API Key como "criptografada" no banco. A decisão registrada aqui é usar **hash** (função de mão única), não **criptografia reversível** (ex: AES), pelo seguinte motivo:

> A aplicação nunca precisa **ler de volta** a API Key em texto plano depois de criada — ela só precisa **comparar** a chave recebida numa requisição com a que foi cadastrada. Isso é exatamente o mesmo problema de senhas: a solução correta é uma função de hash (com uma chave de assinatura secreta do servidor, um "pepper", para o HMAC), nunca uma criptografia que alguém com acesso ao banco e à chave de criptografia conseguiria reverter.

Consequência prática: a chave em texto plano é exibida ao usuário **uma única vez**, no momento da criação, no dashboard (mutation `createProject`/`createApiKey` — ver [06-modulo-api.md](06-modulo-api.md)). Se for perdida, o fluxo é gerar uma nova chave e revogar a antiga — não existe "mostrar a chave novamente".

O algoritmo de hash (`ApiKeyHasher`) e o gerador da chave em si (`ApiKeyGenerator`, prefixo `ombk_` + 32 bytes aleatórios) vivem no `Shared` — não no Webhook nem na ApiGraphQL isoladamente. É o mesmo algoritmo dos dois lados (quem gera, na ApiGraphQL; quem valida, no Webhook) por construção, sem risco de um dos dois divergir com o tempo.

## Rate limiting

Duas camadas independentes, ambas no Webhook:

1. **Por IP de origem** — middleware nativo do ASP.NET Core (`Microsoft.AspNetCore.RateLimiting`), limitando requisições por janela de tempo por IP. Mitiga abuso e tentativas de força bruta contra a API Key.
2. **Backpressure da fila** — verificação do tamanho atual do Redis Stream (`XLEN`, O(1)) antes de aceitar um novo evento. Detalhado em [04-modulo-webhook.md](04-modulo-webhook.md#backpressure-da-fila). Não é um rate limit por identidade, e sim uma proteção de capacidade do sistema como um todo.

Na API GraphQL, o `login` tem seu próprio limite de tentativas por IP — ver [Autenticação do Dashboard](#autenticação-do-dashboard-login).

## Validação de entrada

Toda borda do sistema que recebe dado externo valida antes de processar:

- **Webhook:** valida schema do payload (tipos, campos obrigatórios, valores permitidos como `provider` conhecido, `costUsd`/tokens não-negativos) antes de publicar na fila.
- **API GraphQL:** validação de tipos é garantida pelo próprio schema GraphQL; regras de negócio adicionais (ex: intervalo de datas do filtro) são validadas no Use Case correspondente.

## Autenticação do Dashboard (login)

- Credencial única (usuário/senha) definida em variável de ambiente do serviço da API — não há tabela de usuários no banco, então não há senha para vazar em caso de comprometimento do banco de dados.
- Login bem-sucedido emite um **JWT de curta duração**, exigido em todas as demais chamadas GraphQL e na conexão SignalR.
- Mensagens de erro de login são genéricas (não revelam se o usuário ou a senha estavam incorretos), para dificultar enumeração.
- Credenciais comparadas em tempo constante, e **limite de tentativas de login por IP** (padrão 10 a cada 60s) — sem ele, a senha única do dashboard podia ser testada sem limite nenhum. Detalhes em [06-modulo-api.md](06-modulo-api.md#login-mutation). Mesma ressalva do rate limit do Webhook: atrás de um proxy reverso, todos os clientes chegam com o IP do proxy e dividem o mesmo limite — quem colocar o OmniAI atrás de um proxy precisa configurar `ForwardedHeaders` para o IP real do cliente ser usado.

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

## Autenticação do Cockpit (frontend): limitação conhecida

O token JWT emitido pelo `login` é guardado no `sessionStorage` do navegador (`cockpit/src/features/auth/authStorage.ts`) e enviado em todas as chamadas GraphQL/SignalR via header `Authorization: Bearer`. Essa é uma decisão consciente, não um descuido — e vale documentar o porquê e o trade-off, porque este é um projeto Open Source e quem for rodar em produção precisa decidir com informação.

**O que isso significa na prática:**

- O token fica acessível a qualquer JavaScript executado na página — se existir uma vulnerabilidade de XSS no Cockpit (ou em uma dependência dele), o token pode ser roubado. Um token em cookie `httpOnly` não teria esse problema, porque JavaScript não consegue lê-lo.
- Em compensação, `sessionStorage` some ao fechar a aba e não é enviado automaticamente pelo navegador em toda requisição (diferente de cookie), o que elimina a superfície de CSRF por completo — não existe cookie de sessão para um site malicioso "andar de carona".
- Não há refresh token: o JWT expira em 60 minutos (mesmo valor do `ExpirationMinutes` em [06-modulo-api.md](06-modulo-api.md)) e o usuário precisa logar de novo. Não há sessão persistente entre abas ou entre fechar/abrir o navegador.

**Por que não implementamos a alternativa mais robusta agora:** a alternativa correta para produção — access token + cookie `httpOnly` de refresh token + validação CSRF via double-submit (token replicado num header customizado, comparado com o valor do cookie) — exige que frontend e backend compartilhem um domínio pai comum (ex: `app.empresa.com` e `api.empresa.com` sob `empresa.com`), porque cookies não atravessam domínios não relacionados. Essa topologia de domínio é **específica de cada empresa que for self-host o OmniAI** — não existe um valor padrão razoável para "qual é o seu domínio pai" num projeto Open Source genérico. Forçar essa arquitetura agora tornaria o setup local e o primeiro deploy mais difíceis sem necessidade.

**Recomendação para produção:** empresas que forem colocar o Cockpit em produção com requisitos de segurança mais estritos devem migrar para o esquema acima:

1. Refresh token opaco, armazenado no Redis (permite revogação imediata — algo que um JWT autocontido não permite), entregue **apenas** pela rota de refresh, nunca pelo endpoint de login.
2. Refresh token em cookie `httpOnly` + `Secure` + `SameSite=Strict`, escopado ao domínio pai.
3. CSRF token de dupla submissão: um valor gerado no login, devolvido tanto em cookie (não-`httpOnly`, para o JS conseguir ler) quanto esperado num header customizado (ex: `X-CSRF-Token`) em toda mutation — o backend rejeita se os dois não baterem.
4. Access token (JWT) continua de vida curta, mas agora renovável silenciosamente via refresh, sem precisar logar de novo a cada hora.

Essa é uma extensão da autenticação existente, não uma reescrita — o `LoginUseCase`, o JWT e o `RequireAuthenticated()` (ver [06-modulo-api.md](06-modulo-api.md)) continuam os mesmos; o que muda é como o token chega e se renova no cliente.

## Superfícies de ataque consideradas

| Ameaça | Mitigação |
|---|---|
| Vazamento do banco de dados expõe API Keys | Apenas hash é armazenado — não há chave para vazar em texto plano. |
| Força bruta de API Key | Rate limit por IP no Webhook. |
| Sobrecarga do Consumer/fila (DoS por volume) | Backpressure via `XLEN` rejeita novos eventos além do limite configurado. |
| Enumeração de credenciais de login | Mensagem de erro genérica no `login`. |
| Força bruta da senha do dashboard | Limite de tentativas de login por IP + comparação de credenciais em tempo constante. |
| Payload malformado ou malicioso no Webhook | Validação de schema antes de qualquer processamento ou persistência. |
| Roubo do JWT via XSS no Cockpit | Aceito como limitação conhecida do MVP (token em `sessionStorage`); mitigação recomendada para produção documentada acima. |
