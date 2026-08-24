# Visão Geral do Produto

## O que é o OmniAI

OmniAI é uma plataforma **Open Source** de observabilidade de custo e uso de Inteligência Artificial para empresas que consomem **múltiplos provedores de IA** (OpenAI, Anthropic, Google, etc.) em diferentes aplicações internas ou produtos.

**Slogan:** Única visão para seus provedores de IA.

## Problema

Empresas que usam múltiplos provedores de IA em diferentes projetos não têm uma visão centralizada de **quanto estão gastando** e **como estão usando** esses provedores. As soluções existentes no mercado resolvem esse problema, mas com um custo de complexidade alto: exigem frameworks completos de observabilidade, configuração extensa e dashboards com dezenas de métricas que não respondem à pergunta principal de forma direta.

**Diferencial do OmniAI:** uma única tela mostra o consumo geral, com filtros simples para detalhar gastos por aplicação e por provedor — sem exigir que o usuário estude a ferramenta antes de usá-la.

## Público-alvo

- **Empresas de software** que utilizam diferentes provedores de IA em seus SaaS ou produtos internos.
- **Líderes técnicos** (CTOs, tech leads, engineering managers) que precisam enxergar estatísticas de uso de IA entre projetos e provedores sem precisar entender as regras de negócio internas da ferramenta.

## Proposta de valor

Uma plataforma onde um cargo de liderança técnica abre uma página, vê o panorama geral de custo/uso de IA da empresa e, se precisar, filtra por aplicação ou provedor — sem curva de aprendizado.

## Escopo do produto

O projeto é dividido em dois módulos funcionais:

1. **SDK de coleta de dados** — instrumenta as aplicações que chamam provedores de IA e captura, de forma transparente, os metadados de cada chamada: provedor, modelo, tokens, custo, latência e data/hora de execução.
2. **Plataforma de observabilidade** — recebe os dados enviados pelo SDK, armazena e exibe, em uma interface web, o consumo total, por aplicação e por provedor.

### Fora de escopo (deliberadamente)

Para manter a promessa de simplicidade, os itens abaixo **não** fazem parte do escopo inicial e não devem ser adicionados sem uma decisão explícita que revise este documento:

- Múltiplos papéis de usuário ou controle de acesso granular (RBAC).
- Fluxo de recuperação/troca de senha via aplicação (credencial única via variável de ambiente).
- Dashboards configuráveis, múltiplos gráficos ou customização visual pelo usuário.
- Multi-tenancy entre empresas diferentes na mesma instância (cada instância do OmniAI atende a uma empresa; múltiplas aplicações dessa empresa são registradas como "Projetos" dentro da mesma instância).

## Glossário rápido de domínio

| Termo | Significado |
|---|---|
| **Projeto** | Uma aplicação/serviço da empresa que foi instrumentado com o SDK do OmniAI. |
| **Provedor** | Um fornecedor de IA (ex: OpenAI, Anthropic). |
| **Estatística de uso** | Um registro de uma chamada de IA (provedor, modelo, tokens, custo, latência, timestamp) associado a um Projeto. |

Consulte [11-glossario.md](11-glossario.md) para a lista completa de termos técnicos.
