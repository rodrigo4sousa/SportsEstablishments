# Arquitetura

Ponto de entrada da documentação de arquitetura da App de Reservas de Campos: o que há, em que ordem ler, a galeria de todos os diagramas, as decisões registadas e como editar e verificar tudo.

## 1. Para que serve

Estes documentos descrevem a arquitetura da aplicação tal como está no código: quem a usa, de que partes é feita, como essas partes se organizam, como colaboram durante um pedido, onde correm e porque foram feitas assim. Servem a quem chega ao projeto (incluindo quem o avalia num portfólio) e a quem o vai mudar.

**O que descrevem.** O sistema que existe: um Next.js com três áreas (jogador, dono, admin) sobre uma API ASP.NET Core e uma base PostgreSQL, com um worker de emails dentro da API. A produção existe desde 2026-11-09 (T06): os ids `PL-` e as vistas `*-prod` descrevem-na, e a [ADR 0009](adr/0009-alvo-de-implantacao.md) foi aceite.

**Estado descrito.**

| Parte | Estado |
| --- | --- |
| Contexto, contentores, vista lógica, vista de processo, vista de desenvolvimento, desenvolvimento e CI da vista física, cenários | **Construído**: verificado contra o código. |
| Produção (diagramas `*-prod`, secção 3 da vista física, ADR 0009) | **Em produção desde 2026-11-09 (T06)**; o que o primeiro deploy não confirmou continua em aberto. |

**Verificado contra o commit `f4057ca`** (último commit de código da `main`, 2026-10-29: «chore: pre-deploy cleanup and public rate limit»). O merge destes documentos (`2e4e113`, PR #26) não mudou código. O pull request de endurecimento pré-deploy (T06) acrescentou depois o middleware `UseResponseSecurityHeaders` (CC-HEADERS) e os cabeçalhos de segurança do site (WEB-HEADERS), que o inventário, a vista de processo e dois diagramas já mostram. Quando o código mudar, o inventário e os diagramas afetados mudam no mesmo pull request (ver [Atualização](#63-atualização)).

**Fontes de verdade.** As regras estão no [backlog](../backlog.md); as escolhas técnicas, na [Stack](../stack.md); o mapa do código, no [AGENTS.md](../../AGENTS.md). Estes documentos não repetem regras: mostram como as partes se ligam e apontam para onde está o detalhe.

## 2. Como está organizada

### 2.1 Duas formas de ver a mesma coisa

- O **modelo C4** (contexto, contentores, componentes, código) vai do mais geral ao mais detalhado: cada nível abre uma caixa do nível anterior.
- As **vistas 4+1** de Kruchten (lógica, de processo, de desenvolvimento, física e, ao centro, os cenários) separam preocupações diferentes para leitores diferentes: quem programa, quem opera, quem avalia.

Não são concorrentes. O C4 dá os níveis de zoom; o 4+1 dá os pontos de vista. Cada ficheiro diz, no cabeçalho, a que nível e a que vista pertence.

### 2.2 Um ficheiro, uma pergunta

| Ficheiro | C4 | Vista 4+1 | Pergunta a que responde | Para quem |
| --- | --- | --- | --- | --- |
| [inventory.md](inventory.md) | — (catálogo) | — (catálogo) | Que elementos existem, com que id, e onde estão no código? | Toda a gente que lê um diagrama e quer chegar ao código |
| [c4/README.md](c4/README.md) | Nível 1 · Contexto do sistema | Cenários (contexto de uso) | Quem usa o sistema e com que sistemas externos fala? | Quem avalia, produto, recém-chegados |
| [c4/README.md](c4/README.md) | Nível 2 · Contentores | De processo (unidades que correm) | De que aplicações e armazenamentos é feito, e como comunicam? | Recém-chegados, quem opera |
| [4plus1/logical.md](4plus1/logical.md) | Nível 3 · Componentes e nível 4 · Código | Lógica | Como se organiza o código em camadas e componentes, e que conceitos, invariantes e estados tem o domínio? | Quem programa |
| [4plus1/process.md](4plus1/process.md) | Diagramas dinâmicos (em sequência) | De processo | Que processos e fios correm, o que corre ao mesmo tempo, e como colaboram as partes num pedido, numa transação e em segundo plano? | Quem programa e quem depura |
| [4plus1/development.md](4plus1/development.md) | — (sem equivalente direto no C4) | De desenvolvimento | Como está organizado o repositório, como se constrói, testa e verifica, e como se acrescenta uma funcionalidade? | Quem vai mudar o código |
| [4plus1/physical.md](4plus1/physical.md) | Diagrama de implantação | Física | Onde corre cada contentor (desenvolvimento, CI, produção), o que é público e em que se confia? | Quem opera e faz deploy |
| [4plus1/scenarios.md](4plus1/scenarios.md) | Diagramas dinâmicos (por caso de uso) | Cenários (+1) | Que casos de uso atravessam todas as vistas e as validam? | Todos: é o fio que liga as vistas |
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

Escolhe o percurso pelo que precisas de fazer. Em qualquer um, quando um diagrama mostra um elemento, procura o id no [inventário](inventory.md) para saber onde está o código.

**5 minutos: perceber o que é.**
1. [Contexto](c4/README.md): quem usa e com que sistemas fala.
2. [Contentores](c4/README.md): as peças que correm e como comunicam.

**30 minutos: perceber como funciona.** Os dois anteriores, mais:
3. [Vista lógica](4plus1/logical.md): camadas, componentes da API e do front end, modelo do domínio e máquinas de estados (secções 1 a 5; o código, secção 6, só se for preciso).
4. [Vista de processo](4plus1/process.md): o pipeline de um pedido, a reserva de um slot (2.1) e a concorrência (secção 3).
5. Um cenário à escolha em [Cenários](4plus1/scenarios.md), por exemplo o SC-07, a corrida pelo mesmo slot.

**Para mudar o código.**
1. [Vista de desenvolvimento](4plus1/development.md): estrutura, projetos e referências, convenções e «[Como se acrescenta uma funcionalidade](4plus1/development.md#como-se-acrescenta-uma-funcionalidade)».
2. [Cenários](4plus1/scenarios.md): o cenário mais próximo do que vais mexer mostra as vistas e as regras que atravessas.
3. As [ADRs](adr/README.md) das peças que tocas (por exemplo a [0003](adr/0003-exclusion-constraint-reservas.md) antes de mexer em reservas e a [0004](adr/0004-outbox-para-emails.md) antes de mexer em emails).
4. O [inventário](inventory.md) para o caminho exato de cada elemento, e as secções «Notas» de cada vista para os desvios conhecidos.

**Para fazer deploy (T06).**
1. [Vista física](4plus1/physical.md): o que é público, TLS, uma só origem, o IP do cliente (4.4), a base de dados (4.6), segredos (4.7), migrações (4.8) e os [riscos da T06](4plus1/physical.md#5-riscos-e-pontos-em-aberto-para-a-t06).
2. [ADR 0009 · Alvo de implantação](adr/0009-alvo-de-implantacao.md): a decisão (Vercel, Render, Supabase, Brevo) e o que substitui da Stack. Foi aceite depois do primeiro deploy real.

## 4. Galeria de diagramas

Os 40 diagramas, pela ordem dos ficheiros, cada um com uma linha de legenda e a ligação à secção que o explica. Os diagramas largos estão reduzidos para caberem na página; abre o `.svg` na pasta [out/](out/) para o ver ao tamanho real (e o `.puml` ao lado é a fonte).

### 01 · Contexto

![Contexto do sistema](out/01-context.svg)

Quem usa o sistema e os sistemas externos com que fala. Explicado em [Contexto · Diagrama](c4/README.md#diagrama-do-contexto).

### 02 · Contentores

![Contentores em desenvolvimento](out/02-containers-dev.svg)

Os contentores do docker compose e as duas entradas em desenvolvimento (pelo Next.js e diretamente na API). Explicado em [Contentores · Desenvolvimento](c4/README.md#desenvolvimento-docker-compose).

![Contentores em produção (T06)](out/02-containers-prod.svg)

Os contentores em produção. Explicado em [Contentores · Produção](c4/README.md#produção-t06).

### 03 · Vista lógica

![Camadas do back end](out/logical-layers.svg)

As quatro camadas e o sentido das dependências (Api → Infrastructure → Application → Domain). Explicado em [Vista lógica · 1. Camadas](4plus1/logical.md#1-camadas).

<img src="out/03-components-api-overview.svg" alt="Componentes da API, visão geral" width="900">

Os componentes da API por funcionalidade e as peças transversais. Explicado em [Vista lógica · Visão geral por funcionalidade](4plus1/logical.md#visão-geral-por-funcionalidade).

<img src="out/03-components-api-bookings.svg" alt="Componentes das reservas" width="900">

As partes das reservas: uma reserva, série semanal, as minhas reservas e o lado do dono. Explicado em [Vista lógica · Reservas](4plus1/logical.md#reservas-uma-série-semanal-e-do-dono).

<img src="out/03-components-api-accounts.svg" alt="Componentes das contas" width="900">

Registo, login, sessões e recuperação de palavra-passe. Explicado em [Vista lógica · Contas, sessões e recuperação](4plus1/logical.md#contas-sessões-e-recuperação-de-palavra-passe).

<img src="out/03-components-api-notifications.svg" alt="Componentes das notificações" width="900">

O outbox e o worker: pôr, processar, compor e enviar. Explicado em [Vista lógica · Notificações](4plus1/logical.md#notificações-outbox-e-worker).

<img src="out/03-components-web.svg" alt="Componentes do front end" width="900">

As rotas, as guardas e os caminhos do front end até à API. Explicado em [Vista lógica · Rotas, guardas e caminhos](4plus1/logical.md#rotas-guardas-e-caminhos-até-à-api).

<img src="out/03-components-web-features.svg" alt="Áreas e funcionalidades do front end" width="800">

As áreas do front end e as pastas de funcionalidades de cada uma. Explicado em [Vista lógica · Áreas e funcionalidades](4plus1/logical.md#áreas-e-funcionalidades).

<img src="out/logical-domain-booking.svg" alt="Modelo do domínio: reservas" width="800">

Clubes, campos, horários, bloqueios, faixas de preço e reservas, com as relações. Explicado em [Vista lógica · Clubes, campos, bloqueios e reservas](4plus1/logical.md#clubes-campos-bloqueios-e-reservas).

<img src="out/logical-domain-accounts.svg" alt="Modelo do domínio: contas" width="800">

Contas, refresh tokens, histórico dos donos e mensagens do outbox, com a camada de cada um. Explicado em [Vista lógica · Contas, sessões, histórico e outbox](4plus1/logical.md#contas-sessões-histórico-dos-donos-e-outbox).

<img src="out/logical-state-booking.svg" alt="Estados de uma reserva" width="900">

Os estados de uma reserva e as transições permitidas. Explicado em [Vista lógica · Reserva](4plus1/logical.md#reserva).

<img src="out/logical-state-outbox.svg" alt="Estados de uma mensagem do outbox" width="800">

Os estados de uma mensagem: por enviar, enviada, ignorada ou falhada, com novas tentativas. Explicado em [Vista lógica · Mensagem do outbox](4plus1/logical.md#mensagem-do-outbox).

<img src="out/logical-state-owner.svg" alt="Estados de um dono" width="800">

Dono ativo e desativado, e quem muda o estado. Explicado em [Vista lógica · Dono](4plus1/logical.md#dono).

<img src="out/logical-code-booking.svg" alt="Código: Booking" width="900">

Nível 4: a entidade `Booking` e os resultados dos seus métodos. Explicado em [Vista lógica · O agregado Booking](4plus1/logical.md#o-agregado-booking).

<img src="out/logical-code-slots.svg" alt="Código: slots" width="900">

Nível 4: geração de slots, definição de «livre» e preço de um slot. Explicado em [Vista lógica · Geração de slots, «livre» e preço](4plus1/logical.md#geração-de-slots-livre-e-preço).

### 04 · Vista de processo

<img src="out/process-runtime.svg" alt="Processos e fios de execução" width="900">

O que corre ao mesmo tempo: processos, fios e o worker dentro da API. Explicado em [Vista de processo · 1. Processos e fios](4plus1/process.md#1-processos-fios-e-o-que-corre-ao-mesmo-tempo).

![Pipeline de um pedido na API](out/process-pipeline.svg)

A ordem dos middlewares de um pedido na API. Explicado em [Vista de processo · Pipeline de um pedido](4plus1/process.md#pipeline-de-um-pedido-na-api).

<img src="out/process-book.svg" alt="Jogador reserva um slot" width="900">

Sequência da reserva de um slot, com o trinco do campo e a exclusion constraint. Explicado em [Vista de processo · 2.1 Jogador reserva um slot](4plus1/process.md#21-jogador-reserva-um-slot).

<img src="out/process-series.svg" alt="Série semanal" width="900">

Sequência da série semanal: uma transação por data e um resumo no fim. Explicado em [Vista de processo · 2.2 Série semanal](4plus1/process.md#22-série-semanal).

<img src="out/process-cancel-player.svg" alt="Jogador cancela" width="800">

O jogador cancela, dentro da janela de cancelamento. Explicado em [Vista de processo · 2.3 Pelo jogador](4plus1/process.md#pelo-jogador).

<img src="out/process-cancel-club.svg" alt="Dono cancela" width="900">

O dono cancela com motivo e o email segue na mesma transação. Explicado em [Vista de processo · 2.3 Pelo dono](4plus1/process.md#pelo-dono).

<img src="out/process-login.svg" alt="Login e logout" width="800">

Login com uma só resposta para qualquer falha, e logout. Explicado em [Vista de processo · 2.4 Login e logout](4plus1/process.md#login-e-logout).

<img src="out/process-refresh.svg" alt="Refresh na API" width="800">

Refresh com rotação e deteção de reutilização. Explicado em [Vista de processo · 2.4 Refresh com rotação](4plus1/process.md#refresh-com-rotação-e-deteção-de-reutilização).

<img src="out/process-refresh-client.svg" alt="Refresh no browser" width="800">

No browser: Web Lock entre separadores e refresh silencioso em `/login`. Explicado em [Vista de processo · 2.4 No browser](4plus1/process.md#no-browser-web-lock-entre-separadores-e-refresh-silencioso-em-login).

<img src="out/process-reset-request.svg" alt="Pedir nova palavra-passe" width="900">

Pedir nova palavra-passe: resposta sempre igual e o trabalho fica no outbox. Explicado em [Vista de processo · 2.5 Pedido](4plus1/process.md#pedido).

<img src="out/process-reset-confirm.svg" alt="Definir a nova palavra-passe" width="800">

Confirmar a nova palavra-passe e revogar as sessões. Explicado em [Vista de processo · 2.5 Confirmação](4plus1/process.md#confirmação).

<img src="out/process-outbox.svg" alt="Worker de emails" width="800">

O worker reclama, envia, adia ou desiste. Explicado em [Vista de processo · 2.6 Reclamar, enviar, adiar, desistir](4plus1/process.md#reclamar-enviar-adiar-desistir).

<img src="out/process-outbox-skip.svg" alt="Avisos ao dono e lembretes" width="800">

Lembretes e avisos que já não se aplicam são ignorados, não falhados. Explicado em [Vista de processo · 2.6 Ignorar o que já não se aplica](4plus1/process.md#ignorar-o-que-já-não-se-aplica).

<img src="out/process-guard.svg" alt="Página com guarda" width="900">

Uma página de uma área, com a guarda no servidor. Explicado em [Vista de processo · 2.7 Página com guarda](4plus1/process.md#27-página-de-uma-área-com-guarda-no-servidor).

<img src="out/process-owner-deactivation.svg" alt="Desativar um dono" width="800">

O admin desativa um dono numa transação, com o histórico só de acrescentar. Explicado em [Vista de processo · 2.8 Desativar um dono](4plus1/process.md#28-desativar-um-dono).

### 05 · Vista de desenvolvimento

<img src="out/development-repo.svg" alt="Estrutura do repositório" width="900">

As pastas do repositório e o que cada uma contém. Explicado em [Vista de desenvolvimento · 1. Estrutura do repositório](4plus1/development.md#1-estrutura-do-repositório).

<img src="out/development-backend.svg" alt="Projetos do back end" width="800">

Os projetos .NET e as suas referências. Explicado em [Vista de desenvolvimento · Projetos e referências](4plus1/development.md#projetos-e-referências).

<img src="out/development-di.svg" alt="Composição da injeção de dependências" width="900">

Como cada camada regista os seus serviços e o `Program.cs` só compõe. Explicado em [Vista de desenvolvimento · Injeção de dependências](4plus1/development.md#como-a-injeção-de-dependências-é-composta).

<img src="out/development-frontend.svg" alt="Pastas do front end" width="900">

As pastas do front end e a fronteira `server-only`. Explicado em [Vista de desenvolvimento · 3. Front end](4plus1/development.md#3-front-end).

![Do controller aos tipos](out/development-api-types.svg)

Do controller ao OpenAPI e aos tipos TypeScript gerados. Explicado em [Vista de desenvolvimento · Tipos gerados da API](4plus1/development.md#tipos-gerados-da-api).

<img src="out/development-ci.svg" alt="Pipeline de CI" width="900">

Os jobs de CI e o que cada um verifica. Explicado em [Vista de desenvolvimento · 4. Pipeline de build e qualidade](4plus1/development.md#4-pipeline-de-build-e-qualidade).

### 06 · Vista física

![Implantação em desenvolvimento](out/04-deployment-dev.svg)

A máquina de desenvolvimento, a rede do compose, as portas e o volume da base de dados. Explicado em [Vista física · 1. Desenvolvimento](c4/README.md#1-desenvolvimento-docker-compose).

<img src="out/04-deployment-prod.svg" alt="Implantação em produção (T06)" width="800">

A implantação em produção (Vercel, Render, Supabase, Brevo e GitHub Actions). Explicado em [Vista física · 3. Produção](c4/README.md#3-produção-t06).

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
| 0009 | Alvo de implantação | **Aceite** (T06) | 2026-11-09 | [0009](adr/0009-alvo-de-implantacao.md) |

A data é a do registo; nas ADRs 0001 a 0008 a decisão é mais antiga e vem da Stack ou do backlog, como cada ADR indica.

## 6. Notação e como editar

### 6.1 Legenda

**Ferramenta.** Os diagramas são escritos em [PlantUML](https://plantuml.com) com a biblioteca [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) e **desenhados para SVG**. O texto (`.puml`) é a fonte e muda no mesmo pull request que o código; o SVG gerado também vai para o repositório, para os diagramas se verem no GitHub e nos documentos sem ninguém ter de instalar nada. Em cada ficheiro Markdown, o diagrama é uma imagem Markdown que aponta para o `.svg` da pasta `out/`, seguida da sua legenda.

**Cores (as do C4-PlantUML, sem as alterar; a paleta atual está em [style.puml](style.puml)).**

| Elemento | Macro | Cor | Forma |
| --- | --- | --- | --- |
| Pessoa (`P-`) | `Person` / `Person_Ext` | Azul-escuro `#08427B`, texto branco | Figura de pessoa |
| O nosso sistema (C4 nível 1) | `System` | Azul `#1168BD`, texto branco | Retângulo |
| Contentor (`C-`) | `Container`, `ContainerDb` | Azul `#438DD5`, texto branco | Retângulo; cilindro para a base de dados |
| Componente (`API-`, `CC-`, `WEB-`) | `Component`, `ComponentDb` | Azul-claro `#85BBF0`, texto escuro | Retângulo |
| Sistema externo (`X-`) | `System_Ext`, `Container_Ext` | Cinzento `#999999`, texto branco | Retângulo |
| Entidade do domínio (`D-`) | `class` (diagrama de classes) | Branco, contorno azul | Caixa de classe com o nome, os campos essenciais e as invariantes numa nota |
| Objeto da base de dados (`DB-`) | `ComponentDb` | Azul-claro | Cilindro |
| Produção (`PL-`) | A macro do tipo, como qualquer elemento que existe | A cor do tipo | A forma do tipo |
| Fronteira (sistema, contentor, nó) | `System_Boundary`, `Container_Boundary`, `Boundary`, `Node` | Sem preenchimento, contorno tracejado cinzento | Retângulo com o nome em cima |

**Rótulos dos elementos.** O alias no PlantUML é o id com `_` no lugar de `-` (`API_BOOK` para `API-BOOK`), porque os alias não aceitam hífenes. O nome visível começa sempre pelo id verdadeiro: `Container(C_API, "C-API · API ASP.NET Core", ".NET 10", "…")`. A tecnologia vai no argumento próprio da macro e a descrição, curta, no último.

**Setas.** Uma seta tem sempre um rótulo em **pt-PT**, com um verbo do ponto de vista de quem chama e, a seguir, o protocolo ou formato no argumento da tecnologia: `Rel(JOG, WEB, "Reserva um slot", "HTTPS")`, `Rel(API, DB, "Lê e escreve", "SQL")`, `Rel(WORKER, SMTP, "Envia o email", "SMTP")`. Setas assíncronas (outbox, worker) usam `Rel_Back`/`Rel` com o rótulo «(assíncrono)» e, nos diagramas dinâmicos, a numeração do `C4_Dynamic`. A direção é a de quem inicia.

**Tamanho.** No máximo cerca de 15 elementos por diagrama. Se passar disso, divide-se (por área, por funcionalidade ou por cenário) e liga-se um ao outro com uma nota. Cada diagrama usa `SHOW_LEGEND()` e `LAYOUT_WITH_LEGEND()` quando a legenda automática ajuda, e tem título.

### 6.2 Ids obrigatórios

Todo o elemento de um diagrama usa um id do [inventário](inventory.md), com o prefixo do seu tipo: `P-` pessoas, `X-` sistemas externos, `C-` contentores, `L-` camadas, `API-`/`CC-`/`WEB-` componentes, `D-` domínio, `DB-` base de dados, `DEV-` desenvolvimento, `DEP-` implantação de hoje, `SC-` cenários, `PL-` produção. Um id nunca muda nem é reutilizado. Um elemento novo entra primeiro no inventário, no mesmo pull request; um diagrama sem id para um elemento está errado. Os ids `PL-` só aparecem nas vistas de implantação em produção.

### 6.3 Atualização

Um pull request que muda um elemento do inventário (cria, remove, muda de responsabilidade ou de caminho) atualiza o inventário e os diagramas que o mostram.

### 6.4 Editar, voltar a desenhar e verificar

Os `.puml` ficam em [c4/](c4/) (os níveis do C4: `02-containers-dev.puml`, `03-components-api-bookings.puml`) e em [4plus1/](4plus1/) (as vistas: `logical-state-booking.puml`, `process-book.puml`, `development-ci.puml`); os `.svg` gerados ficam todos em [out/](out/), com o mesmo nome do `.puml`. Todos incluem [style.puml](style.puml), que tem o estilo comum, e começam por um comentário com o título, a versão, a data e o que não está desenhado (e, nas vistas 4+1, para quem é e que decisão documenta).

1. Edita o `.puml`. Usa a biblioteca incluída no próprio PlantUML (`!include <C4/C4_Container>`, `C4_Component`, `C4_Context`, `C4_Dynamic`, `C4_Deployment`), sem descarregar nada da rede, para o desenho ser sempre o mesmo.
2. Desenha todos os diagramas (só precisa do Docker) com `docs/architecture/render.sh` (bash) ou `docs/architecture/render.ps1` (PowerShell, no Windows). Os dois leem a versão de `style.puml` (linha `' plantuml-version: X.Y.Z`) e usam a imagem `plantuml/plantuml:X.Y.Z`, sem montar nada do computador: o tipo de letra (DejaVu Sans) vem dessa imagem, por isso o SVG é o mesmo em qualquer máquina e no CI. Para mudar a versão, altera essa linha, volta a desenhar e faz commit dos SVG.
3. Faz commit do `.puml` e do `.svg` juntos. Um SVG que não corresponde ao `.puml` é um erro de revisão.
4. Um diagrama novo tem de entrar **uma vez** na galeria deste ficheiro. Corre `scripts/check-architecture-docs.sh` (ou `.ps1`): falha se algum `.svg` de `out/` não estiver referenciado exatamente uma vez neste README, se este README referir um ficheiro que não existe, se algum `.puml` não incluir `../style.puml`, ou se uma ligação relativa de qualquer `.md` de `docs/` ou do `README.md` da raiz não chegar a um ficheiro. Não verifica as âncoras (`#secção`) nem se os ids dos diagramas existem no inventário: isso é revisão.

**Integração contínua.** O job `diagrams` («Docs (architecture diagrams)», `DEV-CI-DIAGRAMS`) faz, por esta ordem: (1) corre `docs/architecture/render.sh`; (2) falha se algum `.svg` mudou ou falta, porque o `git status` da pasta deve ficar limpo («drift check»); (3) corre `scripts/check-architecture-docs.sh`.

## Notas de arquitetura

Factos do código que as vistas, os cenários e as ADRs têm de dizer tal como são. Não são erros.

1. **A API também se alcança diretamente, em desenvolvimento.** Além de passar pelo Next.js, a API está publicada na porta 5080 do compose (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. O diagrama de contentores e o de implantação de desenvolvimento mostram as duas entradas (browser → C-WEB → C-API, e browser/ferramentas → C-API em 5080).
2. **O proxy reverso é o Next.js, não o Caddy.** O browser fala com C-WEB e é o Next.js (WEB-REWRITE) que reencaminha `/api/*` para C-API, em desenvolvimento e em produção. O Caddy (PL-CADDY) não foi adotado. O IP do cliente está na [vista física](4plus1/physical.md#44-o-ip-do-cliente), secção 4.4.
3. **Entidades persistidas fora do Domain.** A conta (`AppUser`, do Identity), o refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application. No Domain a conta existe só como regras (`Dom/Accounts`: email, nome, telemóvel, palavra-passe, perfis). O diagrama do domínio mostra estes elementos com a camada a que pertencem (`D-USER`, `D-REFRESH`, `D-STATUSCHG`, `D-OUTBOX`).
4. **Uma exceção ao relógio injetado.** Toda a hora atual vem de `TimeProvider` (CC-TIME), exceto o gerador de tokens de recuperação de palavra-passe do Identity, que lê o relógio do sistema e por isso não se testa com um relógio falso (os testes fabricam tokens antigos no formato do Identity).
5. **A exclusion constraint só cobre reservas `Confirmed`.** `bookings_no_overlap` (DB-NOOVERLAP) tem `WHERE status = 'Confirmed'`: uma reserva cancelada ou uma falta não ocupa o slot, e anular uma falta (ou qualquer volta a `Confirmed`) só tem sucesso se mais nenhuma reserva confirmada se sobrepuser entretanto; senão a base de dados recusa (23P01 → 409 `slot_taken`).

## 7. Limitações conhecidas e pontos em aberto

Tirados dos próprios documentos (secções «Notas» e «Riscos» de cada vista); nada aqui é novo. Esta documentação só descreve: não corrige código.

### 7.1 Dependem da T06 e da ADR 0009

- **A [ADR 0009](adr/0009-alvo-de-implantacao.md) foi aceite** em 2026-11-09, depois do primeiro deploy real. Os diagramas `*-prod` e a [secção 3 da vista física](c4/README.md#3-produção-t06) mostram o que está em produção (Vercel, Render gratuito com a API pública, Supabase, Brevo, sem Caddy).
- **A Stack ainda descreve outro alvo.** `docs/stack.md` fala numa máquina virtual com Caddy; a ADR 0009 nomeia as secções que substitui. A Stack não foi alterada, exceto os nomes dos projetos do back end.
- **O IP do cliente é de melhor esforço.** Em produção a API acredita em `X-Forwarded-For` com `ForwardLimit=1`, confirmado no primeiro deploy, e os limites por IP são só de melhor esforço, porque a API é pública ([secção 4.4 da vista física](4plus1/physical.md#44-o-ip-do-cliente)). O IP nas chamadas dos Server Components não foi reportado.
- **Os pontos da [secção 5 da vista física](4plus1/physical.md#5-riscos-e-pontos-em-aberto-para-a-t06)** que o primeiro deploy real não confirmou continuam em aberto. Os riscos aceites (arranque a frio, planos gratuitos, entre outros) estão na ADR 0009.
- **O worker corre dentro da API**, por isso os emails e lembretes só saem enquanto a API estiver acordada.
- **Feitos antes da T06:** o `dependabot.yml` e os cabeçalhos de segurança do site e da API (`PL-DEPENDABOT`, `PL-SECHEADERS`). O HSTS liga-se com `ENABLE_HSTS=true` na Vercel. A política de conteúdo (CSP) tem `'unsafe-inline'` em scripts e estilos, porque o Next.js com `cacheComponents` envia scripts em linha com os dados de cada página: um limite conhecido, marcado como «Provisório» no backlog.

### 7.2 Diferenças entre documentos e código que ficaram em aberto

As que já foram corrigidas estão registadas como tal nas «Notas» de cada vista. Fecharam-se com o pull request de endurecimento pré-deploy (T06) os comentários de código (`PitchPriceBand`, `TrustedProxyOptions`, `MigrateDatabaseAsync` e `Program.cs`), o `README.md` da raiz (reescrito no pull request #29) e o commit citado nas contagens da vista de desenvolvimento. Estas continuam por corrigir (fora do âmbito de uma branch só de documentação):

1. **`docs/stack.md`**: a árvore do repositório mostra um `Caddyfile`, que não existe, e promete as ADRs «em `docs/`» quando estão em `docs/architecture/adr/`.

Não são erros: o `frontend/Dockerfile` copia uma pasta `public` que não existe e cria-a vazia; a `main` não tem proteção de branch (o CI confirma-se à mão antes de cada merge).

### 7.3 Limites desta documentação

- Foi conferida à mão com o código; não há teste que prove que um diagrama corresponde ao código. O que o CI garante é que os SVG correspondem aos `.puml`, que cada SVG está na galeria e que as ligações relativas chegam a um ficheiro.
- Os ids dos diagramas e do inventário também só se confirmam em revisão.
- Os diagramas mais largos ficam ilegíveis quando reduzidos à largura de uma página, sobretudo os quatro de componentes da API (`03-components-api-bookings`, 4523 px; `03-components-api-accounts`, 3132 px; `03-components-api-overview`, 2900 px; `03-components-api-notifications`, 2484 px); os de sequência de 2200 a 2500 px lêem-se com esforço. Abre o `.svg` ao tamanho real. Dividi-los mais é trabalho futuro.
- Os dados de planos e preços de fornecedores (Render, Supabase, Brevo) mudam; os que estão na vista física têm data e fonte.
