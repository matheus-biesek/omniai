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

- **Webhook:** valida schema do payload (tipos, campos obrigatórios, valores permitidos como `provider` conhecido, `costUsd` não-negativo) antes de publicar na fila.
- **API GraphQL:** validação de tipos é garantida pelo próprio schema GraphQL; regras de negócio adicionais (ex: intervalo de datas do filtro) são validadas no Use Case correspondente.

## Autenticação do Dashboard (login)

- Credencial única (usuário/senha) definida em variável de ambiente do serviço da API — não há tabela de usuários no banco, então não há senha para vazar em caso de comprometimento do banco de dados.
- Login bem-sucedido emite um **JWT de curta duração**, exigido em todas as demais chamadas GraphQL e na conexão SignalR.
- Mensagens de erro de login são genéricas (não revelam se o usuário ou a senha estavam incorretos), para dificultar enumeração.

## Segredos e configuração

Todo segredo do sistema vive em **variável de ambiente**, nunca em código-fonte ou arquivo versionado:

| Segredo | Onde é usado |
|---|---|
| Chave HMAC (pepper) para hash de API Keys | Webhook, dashboard (ao gerar novas chaves) |
| Usuário/senha do login do dashboard | API GraphQL |
| Chave de assinatura do JWT | API GraphQL |
| Connection strings (Postgres escrita/leitura, Redis) | Webhook, Consumer, API GraphQL |

Em desenvolvimento local, as credenciais de Postgres/Redis do Docker Compose vivem em `.env` (gitignored, gerado a partir de `.env.example`) — ver [12-ambiente-local.md](12-ambiente-local.md). São credenciais de uso exclusivamente local; ainda assim nunca versionadas, para manter o hábito consistente com o que vale em produção.

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
