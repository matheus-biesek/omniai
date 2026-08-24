# OmniAI — Documentação

> **Única visão para seus provedores de IA.**

Este diretório é a **fonte de verdade** do projeto OmniAI. Qualquer decisão de arquitetura, regra de negócio ou fluxo descrito aqui deve ser considerada válida até que este documento seja atualizado. Código que divergir da documentação é considerado bug de implementação ou motivo para atualizar a documentação — nunca as duas coisas ficam divergentes por muito tempo.

## Índice

| Documento | Conteúdo |
|---|---|
| [01-visao-geral.md](01-visao-geral.md) | Problema, público-alvo, proposta de valor e escopo do produto |
| [02-arquitetura.md](02-arquitetura.md) | Visão macro do sistema, componentes, fluxo de dados ponta a ponta |
| [03-modulo-sdk.md](03-modulo-sdk.md) | SDK Node.js — instrumentação das chamadas de IA |
| [04-modulo-webhook.md](04-modulo-webhook.md) | Webhook ASP.NET — ingestão, autenticação, rate limit, fila |
| [05-modulo-consumer.md](05-modulo-consumer.md) | Consumer — processamento assíncrono, persistência, tempo real |
| [06-modulo-api.md](06-modulo-api.md) | API GraphQL — login e consulta de estatísticas |
| [07-modulo-frontend.md](07-modulo-frontend.md) | Dashboard React — telas, estrutura e fluxo de dados |
| [08-banco-de-dados.md](08-banco-de-dados.md) | Modelo de dados, Postgres, separação leitura/escrita |
| [09-seguranca.md](09-seguranca.md) | Autenticação, criptografia, rate limiting, superfícies de ataque |
| [10-estrutura-repositorio.md](10-estrutura-repositorio.md) | Organização de pastas do monorepo |
| [11-glossario.md](11-glossario.md) | Termos e siglas usados no projeto |
| [12-ambiente-local.md](12-ambiente-local.md) | Docker Compose — Postgres (primary/réplica) e Redis para desenvolvimento local |

## Como ler esta documentação

1. Comece por [01-visao-geral.md](01-visao-geral.md) para entender o **porquê** do projeto.
2. Leia [02-arquitetura.md](02-arquitetura.md) para entender o **fluxo completo** antes de entrar em qualquer módulo específico.
3. Os documentos de 03 a 07 detalham cada componente na ordem em que os dados fluem pelo sistema (SDK → Webhook → Consumer → API → Frontend).
4. [09-seguranca.md](09-seguranca.md) consolida todas as decisões de segurança citadas nos módulos anteriores — é a referência única para esse assunto.

## Princípios do projeto

Estes princípios guiam toda decisão técnica registrada nesta documentação. Ao propor uma mudança, ela deve ser avaliada contra esta lista:

- **Simplicidade sobre flexibilidade.** Preferir a solução mais simples que resolve o problema atual. Não construir para requisitos hipotéticos futuros.
- **Um único papel de usuário.** Não há RBAC, múltiplos perfis ou fluxo de troca de senha — autenticação do dashboard é validada contra credenciais fixas em variável de ambiente.
- **Sem dependências pesadas.** Preferir recursos nativos do framework (ex: rate limiting do ASP.NET Core, replicação nativa do Postgres) a bibliotecas de terceiros ou infraestrutura extra, quando o nativo resolve.
- **Sem vulnerabilidades conhecidas.** Segredos nunca em texto plano, validação de entrada em toda borda do sistema, princípio do menor privilégio em credenciais de banco.
- **Cada camada faz uma coisa.** SDK mede e envia; Webhook recebe e enfileira; Consumer processa e persiste; API expõe leitura; Frontend exibe. Nenhuma camada assume responsabilidade de outra.
