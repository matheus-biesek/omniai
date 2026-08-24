# Glossário

| Termo | Significado |
|---|---|
| **Projeto** | Uma aplicação/serviço da empresa instrumentado com o SDK do OmniAI. Entidade `Project` no banco de dados. |
| **Provedor** | Um fornecedor de IA (ex: OpenAI, Anthropic, Google). |
| **UsageRecord** | Registro de uma chamada de IA reportada pelo SDK: provedor, modelo, tokens, custo, latência e timestamp. |
| **API Key** | Chave secreta associada a um Projeto, usada para autenticar requisições ao Webhook. Armazenada apenas como hash. |
| **Webhook** | Serviço ASP.NET que recebe os eventos do SDK, autentica, valida e publica na fila. |
| **Consumer** | Serviço .NET (Worker) que processa a fila, persiste os dados e notifica o dashboard em tempo real. |
| **Redis Stream** | Estrutura de fila do Redis usada entre Webhook e Consumer, com suporte nativo a consumer groups. |
| **Backpressure** | Mecanismo que impede o Webhook de aceitar mais eventos quando a fila está no limite configurado. |
| **Banco de escrita (primary)** | Instância Postgres onde o Consumer grava novos registros. |
| **Banco de leitura (réplica)** | Instância Postgres, réplica assíncrona do primary, usada exclusivamente pela API GraphQL para consultas. |
| **SignalR** | Recurso do ASP.NET Core para comunicação em tempo real (WebSocket) entre o Consumer e o Frontend. |
| **JWT** | Token emitido no login do dashboard, usado para autenticar as demais chamadas GraphQL e a conexão SignalR. |
| **DDD** | Domain-Driven Design — organização do código em torno do domínio de negócio (Domain, Application, Infrastructure). |
| **Use Case** | Classe que representa uma ação de negócio específica (ex: `ReceberMetricaUseCase`), orquestrando entidades de domínio. |
