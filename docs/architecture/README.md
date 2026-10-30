# Arquitetura

Ponto de entrada da documentação de arquitetura da App de Reservas de Campos: o que há, em que ordem ler, a galeria de todos os diagramas, as decisões registadas e como editar e verificar tudo.

## 1. Para que serve

Estes documentos descrevem a arquitetura da aplicação tal como está no código: quem a usa, de que partes é feita, como essas partes se organizam, como colaboram durante um pedido, onde correm e porque foram feitas assim. Servem a quem chega ao projeto (incluindo quem o avalia num portfólio) e a quem o vai mudar.

**O que descrevem.** O sistema que existe: um Next.js com três áreas (jogador, dono, admin) sobre uma API ASP.NET Core e uma base PostgreSQL, com um worker de emails dentro da API. A produção ainda não existe: tudo o que lhe diz respeito está marcado como **planeado (T06)** (ids `PL-`, contornos tracejados) e a [ADR 0009](adr/0009-alvo-de-implantacao.md) é uma **proposta**, não uma decisão.

**Estado descrito.**

| Parte | Estado |
| --- | --- |
| Contexto, contentores, vista lógica, vista de processo, vista de desenvolvimento, desenvolvimento e CI da vista física, cenários | **Construído**: verificado contra o código. |
| Produção (diagramas `*-prod`, secção 3 da vista física, ADR 0009) | **Planeado (T06)**: nada implantado, sujeito a verificação com os fornecedores. |

**Verificado contra o commit `f4057ca`** (último commit de código da `main`, 2026-10-29: «chore: pre-deploy cleanup and public rate limit»). O merge destes documentos (`2e4e113`, PR #26) não mudou código. A [vista de desenvolvimento](05-development-view.md) cita ainda `9b45eed` nas contagens de testes: é um commit anterior ao squash, que já não está na `main`. Quando o código mudar, o inventário e os diagramas afetados mudam no mesmo pull request (ver [Atualização](#63-atualização)).

**Fontes de verdade.** As regras estão no [backlog](../backlog.md); as escolhas técnicas, na [Stack](../stack.md); o mapa do código, no [AGENTS.md](../../AGENTS.md). Estes documentos não repetem regras: mostram como as partes se ligam e apontam para onde está o detalhe.

## 2. Como está organizada

### 2.1 Duas formas de ver a mesma coisa

- O **modelo C4** (contexto, contentores, componentes, código) vai do mais geral ao mais detalhado: cada nível abre uma caixa do nível anterior.
- As **vistas 4+1** de Kruchten (lógica, de processo, de desenvolvimento, física e, ao centro, os cenários) separam preocupações diferentes para leitores diferentes: quem programa, quem opera, quem avalia.

Não são concorrentes. O C4 dá os níveis de zoom; o 4+1 dá os pontos de vista. Cada ficheiro diz, no cabeçalho, a que nível e a que vista pertence.

### 2.2 Um ficheiro, uma pergunta

| Ficheiro | C4 | Vista 4+1 | Pergunta a que responde | Para quem |
| --- | --- | --- | --- | --- |
| [00-inventory.md](00-inventory.md) | — (catálogo) | — (catálogo) | Que elementos existem, com que id, e onde estão no código? | Toda a gente que lê um diagrama e quer chegar ao código |
| [01-context.md](01-context.md) | Nível 1 · Contexto do sistema | Cenários (contexto de uso) | Quem usa o sistema e com que sistemas externos fala? | Quem avalia, produto, recém-chegados |
| [02-containers.md](02-containers.md) | Nível 2 · Contentores | De processo (unidades que correm) | De que aplicações e armazenamentos é feito, e como comunicam? | Recém-chegados, quem opera |
| [03-logical-view.md](03-logical-view.md) | Nível 3 · Componentes e nível 4 · Código | Lógica | Como se organiza o código em camadas e componentes, e que conceitos, invariantes e estados tem o domínio? | Quem programa |
| [04-process-view.md](04-process-view.md) | Diagramas dinâmicos (em sequência) | De processo | Que processos e fios correm, o que corre ao mesmo tempo, e como colaboram as partes num pedido, numa transação e em segundo plano? | Quem programa e quem depura |
| [05-development-view.md](05-development-view.md) | — (sem equivalente direto no C4) | De desenvolvimento | Como está organizado o repositório, como se constrói, testa e verifica, e como se acrescenta uma funcionalidade? | Quem vai mudar o código |
| [06-physical-view.md](06-physical-view.md) | Diagrama de implantação | Física | Onde corre cada contentor (desenvolvimento, CI, produção planeada), o que é público e em que se confia? | Quem opera e faz deploy |
| [07-scenarios.md](07-scenarios.md) | Diagramas dinâmicos (por caso de uso) | Cenários (+1) | Que casos de uso atravessam todas as vistas e as validam? | Todos: é o fio que liga as vistas |
| [adr/](adr/README.md) | — | — (justificação transversal) | Que decisões estão registadas, porquê, e em que estado? | Quem quer saber o «porquê» |

As ADRs têm uma tabela própria na [secção 5](#5-as-decisões-adr) deste ficheiro.

### 2.3 Níveis de abstração e porque as vistas se dividem assim

Os níveis do C4 respondem a «de que tamanho é a pergunta»:

1. **Contexto**: o sistema é uma caixa; só importam as pessoas e os sistemas à volta.
2. **Contentores**: abre-se a caixa em aplicações e armazenamentos que correm e falam por protocolos.
3. **Componentes**: abre-se um contentor (a API, o front end) nas peças com responsabilidade própria.
4. **Código**: abre-se um componente nas classes que contam, só onde ajuda (o agregado `Booking`, os slots).

As vistas 4+1 dividem-se por **preocupação**, não por tamanho: a lógica diz o que existe e que regras o domínio guarda; o processo diz o que acontece quando se executa (e a concorrência); o desenvolvimento diz como se trabalha no repositório; a física diz onde corre. Misturar tudo num só diagrama daria um desenho que ninguém lê. Por isso cada diagrama tem no máximo cerca de 15 elementos e uma só pergunta, e os cenários reúnem as vistas num caso concreto.

| Vista | Mostra | Não mostra |
| --- | --- | --- |
| 01 Contexto | Pessoas (`P-`), o sistema como uma caixa, sistemas externos (`X-`). | Tecnologias, contentores, código. |
| 02 Contentores | Contentores (`C-`), sistemas externos, protocolos e formatos nas setas. | Classes, pastas, componentes internos. |
| 03 Vista lógica | Camadas (`L-`); componentes de um contentor de cada vez (`API-`, `CC-` ou `WEB-`) e as suas dependências; entidades e valores (`D-`) com relações, invariantes e estados; o código de duas peças centrais. | Classes individuais nos diagramas de componentes, salvo quando são o próprio componente; controllers, HTTP e ecrãs no modelo do domínio. |
| 04 Vista de processo | Processos e fios; o pipeline da API; uma sequência por diagrama (pedido, transação, trinco, outbox, worker), com cerca de 8 participantes no máximo. | Detalhe interno das classes; recusas que não mudam a sequência (ficam no texto). |
| 05 Vista de desenvolvimento | Pastas do repositório, projetos e referências, pacotes principais, composição da injeção de dependências, pipeline de CI, estratégia de testes, convenções (`DEV-`). | Classes, endpoints e regras de negócio. |
| 06 Vista física | Nós (`DEP-`, e `PL-` para produção), contentores implantados, portas, volumes, redes, o que é público, TLS, IP do cliente, papéis da base de dados, variáveis de configuração que mudam o comportamento. | Lógica de negócio. |
| 07 Cenários | Um caso de uso de ponta a ponta, com as vistas que atravessa. | Detalhe que já está nas outras vistas (aponta para elas). |

## 3. Guia de leitura

Escolhe o percurso pelo que precisas de fazer. Em qualquer um, quando um diagrama mostra um elemento, procura o id no [inventário](00-inventory.md) para saber onde está o código.

**5 minutos: perceber o que é.**
1. [01 · Contexto](01-context.md): quem usa e com que sistemas fala.
2. [02 · Contentores](02-containers.md): as peças que correm e como comunicam.

**30 minutos: perceber como funciona.** Os dois anteriores, mais:
3. [03 · Vista lógica](03-logical-view.md): camadas, componentes da API e do front end, modelo do domínio e máquinas de estados (secções 1 a 5; o código, secção 6, só se for preciso).
4. [04 · Vista de processo](04-process-view.md): o pipeline de um pedido, a reserva de um slot (2.1) e a concorrência (secção 3).
5. Um cenário à escolha em [07 · Cenários](07-scenarios.md), por exemplo o SC-07, a corrida pelo mesmo slot.

**Para mudar o código.**
1. [05 · Vista de desenvolvimento](05-development-view.md): estrutura, projetos e referências, convenções e «[Como se acrescenta uma funcionalidade](05-development-view.md#como-se-acrescenta-uma-funcionalidade)».
2. [07 · Cenários](07-scenarios.md): o cenário mais próximo do que vais mexer mostra as vistas e as regras que atravessas.
3. As [ADRs](adr/README.md) das peças que tocas (por exemplo a [0003](adr/0003-exclusion-constraint-reservas.md) antes de mexer em reservas e a [0004](adr/0004-outbox-para-emails.md) antes de mexer em emails).
4. O [inventário](00-inventory.md) para o caminho exato de cada elemento, e as secções «Notas» de cada vista para os desvios conhecidos.

**Para fazer deploy (planeado, T06).**
1. [06 · Vista física](06-physical-view.md): o que é público, TLS, uma só origem, o IP do cliente (4.4), a base de dados (4.6), segredos (4.7), migrações (4.8) e os [riscos da T06](06-physical-view.md#5-riscos-e-pontos-em-aberto-para-a-t06).
2. [ADR 0009 · Alvo de implantação](adr/0009-alvo-de-implantacao.md): a proposta (Render, Supabase, Brevo) e o que substitui da Stack. É uma proposta: confirma o seu estado antes de agir.

## 4. Galeria de diagramas

Os 40 diagramas, pela ordem dos ficheiros, cada um com uma linha de legenda e a ligação à secção que o explica. Os diagramas largos estão reduzidos para caberem na página; abre o `.svg` na pasta [diagrams/](diagrams/) para o ver ao tamanho real (e o `.puml` ao lado é a fonte).

### 01 · Contexto

![Contexto do sistema](diagrams/01-context.svg)

Quem usa o sistema e os sistemas externos com que fala. Explicado em [01 · Diagrama](01-context.md#diagrama).

### 02 · Contentores

![Contentores em desenvolvimento](diagrams/02-containers-dev.svg)

Os contentores do docker compose e as duas entradas em desenvolvimento (pelo Next.js e diretamente na API). Explicado em [02 · Desenvolvimento](02-containers.md#desenvolvimento-docker-compose).

![Contentores em produção, planeado (T06)](diagrams/02-containers-prod.svg)

Os contentores previstos para produção, a tracejado. Explicado em [02 · Produção, planeado](02-containers.md#produção-planeado-t06).

### 03 · Vista lógica

![Camadas do back end](diagrams/03-logical-layers.svg)

As quatro camadas e o sentido das dependências (Api → Infrastructure → Application → Domain). Explicado em [03 · 1. Camadas](03-logical-view.md#1-camadas).

<img src="diagrams/03-logical-api-overview.svg" alt="Componentes da API, visão geral" width="900">

Os componentes da API por funcionalidade e as peças transversais. Explicado em [03 · Visão geral por funcionalidade](03-logical-view.md#visão-geral-por-funcionalidade).

<img src="diagrams/03-logical-api-bookings.svg" alt="Componentes das reservas" width="900">

As partes das reservas: uma reserva, série semanal, as minhas reservas e o lado do dono. Explicado em [03 · Reservas](03-logical-view.md#reservas-uma-série-semanal-e-do-dono).

<img src="diagrams/03-logical-api-accounts.svg" alt="Componentes das contas" width="900">

Registo, login, sessões e recuperação de palavra-passe. Explicado em [03 · Contas, sessões e recuperação](03-logical-view.md#contas-sessões-e-recuperação-de-palavra-passe).

<img src="diagrams/03-logical-api-notifications.svg" alt="Componentes das notificações" width="900">

O outbox e o worker: pôr, processar, compor e enviar. Explicado em [03 · Notificações](03-logical-view.md#notificações-outbox-e-worker).

<img src="diagrams/03-logical-web.svg" alt="Componentes do front end" width="900">

As rotas, as guardas e os caminhos do front end até à API. Explicado em [03 · Rotas, guardas e caminhos](03-logical-view.md#rotas-guardas-e-caminhos-até-à-api).

<img src="diagrams/03-logical-web-features.svg" alt="Áreas e funcionalidades do front end" width="800">

As áreas do front end e as pastas de funcionalidades de cada uma. Explicado em [03 · Áreas e funcionalidades](03-logical-view.md#áreas-e-funcionalidades).

<img src="diagrams/03-logical-domain-booking.svg" alt="Modelo do domínio: reservas" width="800">

Clubes, campos, horários, bloqueios, faixas de preço e reservas, com as relações. Explicado em [03 · Clubes, campos, bloqueios e reservas](03-logical-view.md#clubes-campos-bloqueios-e-reservas).

<img src="diagrams/03-logical-domain-accounts.svg" alt="Modelo do domínio: contas" width="800">

Contas, refresh tokens, histórico dos donos e mensagens do outbox, com a camada de cada um. Explicado em [03 · Contas, sessões, histórico e outbox](03-logical-view.md#contas-sessões-histórico-dos-donos-e-outbox).

<img src="diagrams/03-logical-state-booking.svg" alt="Estados de uma reserva" width="900">

Os estados de uma reserva e as transições permitidas. Explicado em [03 · Reserva](03-logical-view.md#reserva).

<img src="diagrams/03-logical-state-outbox.svg" alt="Estados de uma mensagem do outbox" width="800">

Os estados de uma mensagem: por enviar, enviada, ignorada ou falhada, com novas tentativas. Explicado em [03 · Mensagem do outbox](03-logical-view.md#mensagem-do-outbox).

<img src="diagrams/03-logical-state-owner.svg" alt="Estados de um dono" width="800">

Dono ativo e desativado, e quem muda o estado. Explicado em [03 · Dono](03-logical-view.md#dono).

<img src="diagrams/03-logical-code-booking.svg" alt="Código: Booking" width="900">

Nível 4: a entidade `Booking` e os resultados dos seus métodos. Explicado em [03 · O agregado Booking](03-logical-view.md#o-agregado-booking).

<img src="diagrams/03-logical-code-slots.svg" alt="Código: slots" width="900">

Nível 4: geração de slots, definição de «livre» e preço de um slot. Explicado em [03 · Geração de slots, «livre» e preço](03-logical-view.md#geração-de-slots-livre-e-preço).

### 04 · Vista de processo

<img src="diagrams/04-process-runtime.svg" alt="Processos e fios de execução" width="900">

O que corre ao mesmo tempo: processos, fios e o worker dentro da API. Explicado em [04 · 1. Processos e fios](04-process-view.md#1-processos-fios-e-o-que-corre-ao-mesmo-tempo).

![Pipeline de um pedido na API](diagrams/04-process-pipeline.svg)

A ordem dos middlewares de um pedido na API. Explicado em [04 · Pipeline de um pedido](04-process-view.md#pipeline-de-um-pedido-na-api).

<img src="diagrams/04-process-book.svg" alt="Jogador reserva um slot" width="900">

Sequência da reserva de um slot, com o trinco do campo e a exclusion constraint. Explicado em [04 · 2.1 Jogador reserva um slot](04-process-view.md#21-jogador-reserva-um-slot).

<img src="diagrams/04-process-series.svg" alt="Série semanal" width="900">

Sequência da série semanal: uma transação por data e um resumo no fim. Explicado em [04 · 2.2 Série semanal](04-process-view.md#22-série-semanal).

<img src="diagrams/04-process-cancel-player.svg" alt="Jogador cancela" width="800">

O jogador cancela, dentro da janela de cancelamento. Explicado em [04 · 2.3 Pelo jogador](04-process-view.md#pelo-jogador).

<img src="diagrams/04-process-cancel-club.svg" alt="Dono cancela" width="900">

O dono cancela com motivo e o email segue na mesma transação. Explicado em [04 · 2.3 Pelo dono](04-process-view.md#pelo-dono).

<img src="diagrams/04-process-login.svg" alt="Login e logout" width="800">

Login com uma só resposta para qualquer falha, e logout. Explicado em [04 · 2.4 Login e logout](04-process-view.md#login-e-logout).

<img src="diagrams/04-process-refresh.svg" alt="Refresh na API" width="800">

Refresh com rotação e deteção de reutilização. Explicado em [04 · 2.4 Refresh com rotação](04-process-view.md#refresh-com-rotação-e-deteção-de-reutilização).

<img src="diagrams/04-process-refresh-client.svg" alt="Refresh no browser" width="800">

No browser: Web Lock entre separadores e refresh silencioso em `/login`. Explicado em [04 · 2.4 No browser](04-process-view.md#no-browser-web-lock-entre-separadores-e-refresh-silencioso-em-login).

<img src="diagrams/04-process-reset-request.svg" alt="Pedir nova palavra-passe" width="900">

Pedir nova palavra-passe: resposta sempre igual e o trabalho fica no outbox. Explicado em [04 · 2.5 Pedido](04-process-view.md#pedido).

<img src="diagrams/04-process-reset-confirm.svg" alt="Definir a nova palavra-passe" width="800">

Confirmar a nova palavra-passe e revogar as sessões. Explicado em [04 · 2.5 Confirmação](04-process-view.md#confirmação).

<img src="diagrams/04-process-outbox.svg" alt="Worker de emails" width="800">

O worker reclama, envia, adia ou desiste. Explicado em [04 · 2.6 Reclamar, enviar, adiar, desistir](04-process-view.md#reclamar-enviar-adiar-desistir).

<img src="diagrams/04-process-outbox-skip.svg" alt="Avisos ao dono e lembretes" width="800">

Lembretes e avisos que já não se aplicam são ignorados, não falhados. Explicado em [04 · 2.6 Ignorar o que já não se aplica](04-process-view.md#ignorar-o-que-já-não-se-aplica).

<img src="diagrams/04-process-guard.svg" alt="Página com guarda" width="900">

Uma página de uma área, com a guarda no servidor. Explicado em [04 · 2.7 Página com guarda](04-process-view.md#27-página-de-uma-área-com-guarda-no-servidor).

<img src="diagrams/04-process-owner-deactivation.svg" alt="Desativar um dono" width="800">

O admin desativa um dono numa transação, com o histórico só de acrescentar. Explicado em [04 · 2.8 Desativar um dono](04-process-view.md#28-desativar-um-dono).

### 05 · Vista de desenvolvimento

<img src="diagrams/05-dev-repo.svg" alt="Estrutura do repositório" width="900">

As pastas do repositório e o que cada uma contém. Explicado em [05 · 1. Estrutura do repositório](05-development-view.md#1-estrutura-do-repositório).

<img src="diagrams/05-dev-backend.svg" alt="Projetos do back end" width="800">

Os projetos .NET e as suas referências. Explicado em [05 · Projetos e referências](05-development-view.md#projetos-e-referências).

<img src="diagrams/05-dev-di.svg" alt="Composição da injeção de dependências" width="900">

Como cada camada regista os seus serviços e o `Program.cs` só compõe. Explicado em [05 · Injeção de dependências](05-development-view.md#como-a-injeção-de-dependências-é-composta).

<img src="diagrams/05-dev-frontend.svg" alt="Pastas do front end" width="900">

As pastas do front end e a fronteira `server-only`. Explicado em [05 · 3. Front end](05-development-view.md#3-front-end).

![Do controller aos tipos](diagrams/05-dev-api-types.svg)

Do controller ao OpenAPI e aos tipos TypeScript gerados. Explicado em [05 · Tipos gerados da API](05-development-view.md#tipos-gerados-da-api).

<img src="diagrams/05-dev-ci.svg" alt="Pipeline de CI" width="900">

Os jobs de CI e o que cada um verifica. Explicado em [05 · 4. Pipeline de build e qualidade](05-development-view.md#4-pipeline-de-build-e-qualidade).

### 06 · Vista física

![Implantação em desenvolvimento](diagrams/06-physical-dev.svg)

A máquina de desenvolvimento, a rede do compose, as portas e o volume da base de dados. Explicado em [06 · 1. Desenvolvimento](06-physical-view.md#1-desenvolvimento-docker-compose).

<img src="diagrams/06-physical-prod.svg" alt="Implantação em produção, planeado (T06)" width="800">

A implantação prevista (Render, Supabase, Brevo), a tracejado. Explicado em [06 · 3. Produção, planeado](06-physical-view.md#3-produção-planeado-t06).

## 5. As decisões (ADR)

Cada ADR regista o contexto, a decisão, as alternativas e as consequências. Uma ADR aceite não se reescreve: se a decisão mudar, escreve-se uma nova que a substitui. Os estados possíveis estão no [índice das ADRs](adr/README.md).

| N.º | Título | Estado | Data | Ficheiro |
| --- | --- | --- | --- | --- |
| 0001 | Uma só aplicação com três áreas | Aceite | 2026-10-30 | [0001](adr/0001-uma-app-tres-areas.md) |
| 0002 | Sessão em cookies httpOnly em vez de localStorage | Aceite | 2026-10-30 | [0002](adr/0002-sessao-em-cookies-httponly.md) |
| 0003 | Exclusion constraint contra reservas duplicadas | Aceite | 2026-10-30 | [0003](adr/0003-exclusion-constraint-reservas.md) |
| 0004 | Outbox para os emails | Aceite | 2026-10-30 | [0004](adr/0004-outbox-para-emails.md) |
| 0005 | Um marketplace em vez de um site por dono | Aceite | 2026-10-30 | [0005](adr/0005-marketplace-unico.md) |
| 0006 | Ids públicos GUID v7 | Aceite | 2026-10-30 | [0006](adr/0006-ids-guid-v7.md) |
| 0007 | Slots calculados, não guardados | Aceite | 2026-10-30 | [0007](adr/0007-slots-calculados.md) |
| 0008 | Chaves do Data Protection na base de dados | Aceite | 2026-10-30 | [0008](adr/0008-chaves-data-protection-na-base-de-dados.md) |
| 0009 | Alvo de implantação | **Proposta** (T06) | 2026-10-30 | [0009](adr/0009-alvo-de-implantacao.md) |

A data é a do registo; nas ADRs 0001 a 0008 a decisão é mais antiga e vem da Stack ou do backlog, como cada ADR indica.

## 6. Notação e como editar

### 6.1 Legenda

**Ferramenta.** Os diagramas são escritos em [PlantUML](https://plantuml.com) com a biblioteca [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) e **desenhados para SVG**. O texto (`.puml`) é a fonte e muda no mesmo pull request que o código; o SVG gerado também vai para o repositório, para os diagramas se verem no GitHub e nos documentos sem ninguém ter de instalar nada. Em cada ficheiro Markdown, o diagrama é uma imagem Markdown que aponta para o `.svg` da pasta `diagrams/`, seguida da sua legenda.

**Cores (as do C4-PlantUML, sem as alterar).**

| Elemento | Macro | Cor | Forma |
| --- | --- | --- | --- |
| Pessoa (`P-`) | `Person` / `Person_Ext` | Azul-escuro `#08427B`, texto branco | Figura de pessoa |
| O nosso sistema (C4 nível 1) | `System` | Azul `#1168BD`, texto branco | Retângulo |
| Contentor (`C-`) | `Container`, `ContainerDb` | Azul `#438DD5`, texto branco | Retângulo; cilindro para a base de dados |
| Componente (`API-`, `CC-`, `WEB-`) | `Component`, `ComponentDb` | Azul-claro `#85BBF0`, texto escuro | Retângulo |
| Sistema externo (`X-`) | `System_Ext`, `Container_Ext` | Cinzento `#999999`, texto branco | Retângulo |
| Entidade do domínio (`D-`) | `class` (diagrama de classes) | Branco, contorno azul | Caixa de classe com o nome, os campos essenciais e as invariantes numa nota |
| Objeto da base de dados (`DB-`) | `ComponentDb` | Azul-claro | Cilindro |
| Planeado (`PL-`) | A macro do tipo com a etiqueta `planeado` (`AddElementTag`) | A cor do tipo, com contorno tracejado | A forma do tipo, com «planeado» na legenda |
| Fronteira (sistema, contentor, nó) | `System_Boundary`, `Container_Boundary`, `Boundary`, `Node` | Sem preenchimento, contorno tracejado cinzento | Retângulo com o nome em cima |

**Rótulos dos elementos.** O alias no PlantUML é o id com `_` no lugar de `-` (`API_BOOK` para `API-BOOK`), porque os alias não aceitam hífenes. O nome visível começa sempre pelo id verdadeiro: `Container(C_API, "C-API · API ASP.NET Core", ".NET 10", "…")`. A tecnologia vai no argumento próprio da macro e a descrição, curta, no último.

**Setas.** Uma seta tem sempre um rótulo em **pt-PT**, com um verbo do ponto de vista de quem chama e, a seguir, o protocolo ou formato no argumento da tecnologia: `Rel(JOG, WEB, "Reserva um slot", "HTTPS")`, `Rel(API, DB, "Lê e escreve", "SQL")`, `Rel(WORKER, SMTP, "Envia o email", "SMTP")`. Setas assíncronas (outbox, worker) usam `Rel_Back`/`Rel` com o rótulo «(assíncrono)» e, nos diagramas dinâmicos, a numeração do `C4_Dynamic`. A direção é a de quem inicia.

**Tamanho.** No máximo cerca de 15 elementos por diagrama. Se passar disso, divide-se (por área, por funcionalidade ou por cenário) e liga-se um ao outro com uma nota. Cada diagrama usa `SHOW_LEGEND()` e `LAYOUT_WITH_LEGEND()` quando a legenda automática ajuda, e tem título.

### 6.2 Ids obrigatórios

Todo o elemento de um diagrama usa um id do [inventário](00-inventory.md), com o prefixo do seu tipo: `P-` pessoas, `X-` sistemas externos, `C-` contentores, `L-` camadas, `API-`/`CC-`/`WEB-` componentes, `D-` domínio, `DB-` base de dados, `DEV-` desenvolvimento, `DEP-` implantação de hoje, `SC-` cenários, `PL-` planeado. Um id nunca muda nem é reutilizado. Um elemento novo entra primeiro no inventário, no mesmo pull request; um diagrama sem id para um elemento está errado. Os ids `PL-` só aparecem em vistas do futuro, marcados como planeados.

### 6.3 Atualização

Um pull request que muda um elemento do inventário (cria, remove, muda de responsabilidade ou de caminho) atualiza o inventário e os diagramas que o mostram.

### 6.4 Editar, voltar a desenhar e verificar

Os `.puml` e os `.svg` ficam lado a lado em [diagrams/](diagrams/): `NN-nome.puml` e `NN-nome.svg`, com o número do documento a que pertencem (`02-containers-dev.puml` para `02-containers.md`; vários diagramas no mesmo documento levam um sufixo, `03-logical-api-bookings.puml`).

1. Edita o `.puml`. Usa a biblioteca incluída no próprio PlantUML (`!include <C4/C4_Container>`, `C4_Component`, `C4_Context`, `C4_Dynamic`, `C4_Deployment`), sem descarregar nada da rede, para o desenho ser sempre o mesmo.
2. Desenha todos os diagramas (só precisa do Docker) com `scripts/render-diagrams.sh` (bash) ou `scripts/render-diagrams.ps1` (PowerShell, no Windows). Os dois leem a versão do cabeçalho dos `.puml` (`' plantuml-version: X.Y.Z`) e usam a imagem `plantuml/plantuml:X.Y.Z`; recusam-se a correr se os ficheiros indicarem versões diferentes. Para mudar a versão, altera o cabeçalho de **todos** os `.puml`, volta a desenhar e faz commit dos SVG.
3. Faz commit do `.puml` e do `.svg` juntos. Um SVG que não corresponde ao `.puml` é um erro de revisão.
4. Um diagrama novo tem de entrar **uma vez** na galeria deste ficheiro. Corre `scripts/check-architecture-docs.sh` (ou `.ps1`): falha se algum `.svg` de `diagrams/` não estiver referenciado exatamente uma vez neste README, se este README referir um ficheiro que não existe, ou se uma ligação relativa de qualquer `.md` desta pasta (incluindo `adr/`) não chegar a um ficheiro. Não verifica as âncoras (`#secção`) nem se os ids dos diagramas existem no inventário: isso é revisão.

**Integração contínua.** O job `diagrams` («Docs (architecture diagrams)», `DEV-CI-DIAGRAMS`) faz, por esta ordem: (1) corre `scripts/render-diagrams.sh`; (2) falha se algum `.svg` mudou ou falta, porque o `git status` da pasta deve ficar limpo («drift check»); (3) corre `scripts/check-architecture-docs.sh`.

## Notas de arquitetura

Factos do código que as vistas, os cenários e as ADRs têm de dizer tal como são. Não são erros.

1. **A API também se alcança diretamente, em desenvolvimento.** Além de passar pelo Next.js, a API está publicada na porta 5080 do compose (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. O diagrama de contentores e o de implantação de desenvolvimento mostram as duas entradas (browser → C-WEB → C-API, e browser/ferramentas → C-API em 5080).
2. **Hoje o proxy reverso é o Next.js, não o Caddy.** O browser fala com C-WEB e é o Next.js (WEB-REWRITE) que reencaminha `/api/*` para C-API. Por isso a API só vê o endereço do contentor Next.js como IP do cliente (relevante para os limites por IP). O Caddy aparece só como planeado (PL-CADDY), e a [ADR 0009](adr/0009-alvo-de-implantacao.md) propõe não o usar: a solução prevista para a T06 está na [vista física](06-physical-view.md#44-o-ip-do-cliente), secção 4.4.
3. **Entidades persistidas fora do Domain.** A conta (`AppUser`, do Identity), o refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application. No Domain a conta existe só como regras (`Dom/Accounts`: email, nome, telemóvel, palavra-passe, perfis). O diagrama do domínio mostra estes elementos com a camada a que pertencem (`D-USER`, `D-REFRESH`, `D-STATUSCHG`, `D-OUTBOX`).
4. **Uma exceção ao relógio injetado.** Toda a hora atual vem de `TimeProvider` (CC-TIME), exceto o gerador de tokens de recuperação de palavra-passe do Identity, que lê o relógio do sistema e por isso não se testa com um relógio falso (os testes fabricam tokens antigos no formato do Identity).
5. **A exclusion constraint só cobre reservas `Confirmed`.** `bookings_no_overlap` (DB-NOOVERLAP) tem `WHERE status = 'Confirmed'`: uma reserva cancelada ou uma falta não ocupa o slot, e anular uma falta (ou qualquer volta a `Confirmed`) só tem sucesso se mais nenhuma reserva confirmada se sobrepuser entretanto; senão a base de dados recusa (23P01 → 409 `slot_taken`).

## 7. Limitações conhecidas e pontos em aberto

Tirados dos próprios documentos (secções «Notas» e «Riscos» de cada vista); nada aqui é novo. Esta documentação só descreve: não corrige código.

### 7.1 Dependem da T06 e da ADR 0009

- **A [ADR 0009](adr/0009-alvo-de-implantacao.md) é uma Proposta.** Os diagramas `*-prod` e a [secção 3 da vista física](06-physical-view.md#3-produção-planeado-t06) mostram a proposta (Render, Supabase, Brevo, sem Caddy), não uma decisão. Se a ADR mudar, esses diagramas e os ids `PL-` mudam com ela.
- **A Stack e o backlog ainda descrevem outro alvo.** `docs/stack.md` e o critério da T06 em `docs/backlog.md` falam numa máquina virtual com Caddy, `deploy.yml` e migration bundle; a ADR 0009, quando aceite, substitui-os. Está marcado como provisório em «Pontos a rever no T06» do backlog. A Stack não foi alterada, exceto os nomes dos projetos do back end.
- **O IP do cliente não está resolvido.** Hoje a API não confia em nenhum proxy e vê o endereço do contentor Next.js, por isso todos os utilizadores partilham o mesmo limite de pedidos por IP. A solução prevista (cinco passos) está na [secção 4.4 da vista física](06-physical-view.md#44-o-ip-do-cliente) e depende de confirmar, com um pedido real no Render, o cabeçalho que a borda envia.
- **Os 16 pontos de risco da [secção 5 da vista física](06-physical-view.md#5-riscos-e-pontos-em-aberto-para-a-t06)** estão por confirmar com os fornecedores (planos e custos do Render, pooler do Supabase com o EF Core, `btree_gist`, domínio próprio para o Brevo, migrações e comandos sem acesso local, limites dos planos gratuitos, entre outros). Foram lidos em 2026-10-30 e têm de ser verificados outra vez antes de decidir.
- **O worker corre dentro da API**, por isso em produção a API tem de estar sempre ligada, ou os emails e lembretes atrasam ou são ignorados.
- **Faltam antes da T06** o `dependabot.yml` e os cabeçalhos de segurança do site (`PL-DEPENDABOT`, `PL-SECHEADERS`); hoje só `/reset-password` tem cabeçalhos próprios.

### 7.2 Diferenças entre documentos e código que ficaram em aberto

As que já foram corrigidas estão registadas como tal nas «Notas» de cada vista. Estas continuam por corrigir (fora do âmbito de uma branch só de documentação):

1. **Comentário de `PitchPriceBand`**: refere `Pitch.AddPriceBand` e `Pitch.ChangePriceBand`; os métodos chamam-se `TryAddPriceBand` e `TryChangePriceBand` (código, [vista lógica](03-logical-view.md#notas)).
2. **Comentário de `TrustedProxyOptions`** (`Api/Security/TrustedProxyOptions.cs`): diz que a produção confia só no Caddy, tal como o critério da T06 no backlog ([vista física](06-physical-view.md#6-notas)).
3. **`README.md` da raiz**: diz «Production: trust only Caddy»; a secção «Continuous integration» não menciona o job `diagrams`; a «Repository layout» está desatualizada ([vista de desenvolvimento, secção 7](05-development-view.md#7-notas-e-diferenças-encontradas)).
4. **`docs/stack.md`**: a árvore do repositório mostra `Caddyfile` e `deploy.yml`, que não existem (são da T06), e promete as ADRs «em `docs/`» quando estão em `docs/architecture/adr/`.
5. **Comentários de `MigrateDatabaseAsync` e de `Program.cs`**: dizem que a produção usa um migration bundle, que ainda não existe.
6. **`getPublicClub`** chama, a partir do servidor, um endpoint com limite por IP sem o IP do cliente: no compose, todas as páginas de clube já partilham a partição do contentor Next.js ([secção 4.4](06-physical-view.md#44-o-ip-do-cliente)).
7. **Commit citado na vista de desenvolvimento**: as contagens de testes citam `9b45eed`, que já não está na `main` (ver a [secção 1](#1-para-que-serve)).

Não são erros: o `frontend/Dockerfile` copia uma pasta `public` que não existe e cria-a vazia; a `main` não tem proteção de branch (o CI confirma-se à mão antes de cada merge).

### 7.3 Limites desta documentação

- Foi conferida à mão com o código; não há teste que prove que um diagrama corresponde ao código. O que o CI garante é que os SVG correspondem aos `.puml`, que cada SVG está na galeria e que as ligações relativas chegam a um ficheiro.
- Os ids dos diagramas e do inventário também só se confirmam em revisão.
- Os diagramas mais largos ficam ilegíveis quando reduzidos à largura de uma página, sobretudo os quatro de componentes da API (`03-logical-api-bookings`, 4523 px; `03-logical-api-accounts`, 3132 px; `03-logical-api-overview`, 2900 px; `03-logical-api-notifications`, 2484 px); os de sequência de 2200 a 2500 px lêem-se com esforço. Abre o `.svg` ao tamanho real. Dividi-los mais é trabalho futuro.
- Os dados de planos e preços de fornecedores (Render, Supabase, Brevo) mudam; os que estão na vista física têm data e fonte.
