# Arquitetura

## Para que serve

Estes documentos descrevem a arquitetura da App de Reservas de Campos tal como está no código: quem a usa, de que partes é feita, como essas partes se organizam, como colaboram durante um pedido, onde correm e porque foram feitas assim. Servem a quem chega ao projeto (incluindo quem o avalia num portfólio) e a quem o vai mudar.

Usam duas formas de ver a mesma coisa, que se completam:

- o **modelo C4** (contexto, contentores, componentes, código), que vai do mais geral ao mais detalhado;
- as **vistas 4+1** de Kruchten (lógica, de processo, de desenvolvimento, física e cenários), que separam preocupações diferentes.

A fonte de verdade das regras é o [backlog](../backlog.md); a das escolhas técnicas é a [Stack](../stack.md); o mapa do código é o [AGENTS.md](../../AGENTS.md). Estes documentos não repetem regras: mostram como as partes se ligam e apontam para onde está o detalhe.

## Como ler

1. Começa pelo [inventário](00-inventory.md): é o catálogo de todos os elementos, com o id, o que faz e onde está no código.
2. Lê os documentos pela ordem dos números: cada um desce um nível ou muda de ponto de vista.
3. Quando um diagrama mostra um elemento, procura o id no inventário para saber onde está o código.
4. As decisões que explicam o «porquê» estão em [adr/](adr/README.md); os diagramas apontam para elas quando uma escolha não é óbvia.

## Correspondência entre C4 e 4+1

| Ficheiro | C4 | Vista 4+1 | Pergunta a que responde |
| --- | --- | --- | --- |
| [00-inventory.md](00-inventory.md) | — (catálogo) | — (catálogo) | Que elementos existem e onde estão no código? |
| [01-context.md](01-context.md) | Nível 1 · Contexto do sistema | Cenários (contexto de uso) | Quem usa o sistema e com que sistemas externos fala? |
| [02-containers.md](02-containers.md) | Nível 2 · Contentores | De processo (unidades que correm) | De que aplicações e armazenamentos é feito, e como comunicam? |
| [03-logical-view.md](03-logical-view.md) | Nível 3 · Componentes e nível 4 · Código | Lógica | Como se organiza o código em camadas e componentes, que conceitos, invariantes e estados tem o domínio? |
| [04-process-view.md](04-process-view.md) | Diagramas dinâmicos (em sequência) | De processo | Que processos e fios correm, o que corre ao mesmo tempo, e como colaboram as partes num pedido, numa transação e em segundo plano? |
| [05-development-view.md](05-development-view.md) | — (organização do código; sem equivalente direto no C4) | De desenvolvimento | Como está organizado o repositório, que projetos e pacotes tem, como se constrói, testa e verifica, e como se acrescenta uma funcionalidade? |
| [06-physical-view.md](06-physical-view.md) | Diagrama de implantação | Física | Onde corre cada contentor, em desenvolvimento, no CI e em produção (planeada), o que é público e em que se confia? |
| [07-scenarios.md](07-scenarios.md) | Diagramas dinâmicos (por caso de uso) | Cenários (+1) | Os casos de uso que atravessam todas as vistas e as validam. |
| [adr/](adr/README.md) | — | — (justificação transversal) | Porque é que as escolhas importantes são as que são? |

## Nível de abstração de cada vista

| Vista | Mostra | Não mostra |
| --- | --- | --- |
| 01 Contexto | Pessoas (`P-`), o sistema como uma caixa, sistemas externos (`X-`). | Tecnologias, contentores, código. |
| 02 Contentores | Contentores (`C-`), sistemas externos, protocolos e formatos nas setas. | Classes, pastas, componentes internos. |
| 03 Vista lógica | Camadas (`L-`); componentes de um contentor de cada vez (`API-`, `CC-` ou `WEB-`) e as suas dependências; entidades e valores (`D-`) com relações, invariantes e estados; o código de duas peças centrais. | Classes individuais nos diagramas de componentes, salvo quando são o próprio componente; controllers, HTTP e ecrãs no modelo do domínio. |
| 04 Vista de processo | Processos e fios; o pipeline da API; uma sequência por diagrama (pedido, transação, trinco, outbox, worker), com cerca de 8 participantes no máximo. | Detalhe interno das classes; recusas que não mudam a sequência (ficam no texto). |
| 05 Vista de desenvolvimento | Pastas do repositório, projetos e referências, pacotes principais, composição da injeção de dependências, pipeline de CI, estratégia de testes, convenções (`DEV-`). | Classes, endpoints e regras de negócio. |
| 06 Vista física | Nós (`DEP-`, e `PL-` para produção), contentores implantados, portas, volumes, redes, o que é público, TLS, IP do cliente, papéis da base de dados, variáveis de configuração que mudam o comportamento. | Lógica de negócio. |
| 07 Cenários | Um caso de uso de ponta a ponta, com as vistas que atravessa. | Detalhe que já está nas outras vistas (aponta para elas). |

## Legenda e notação

**Ferramenta.** Os diagramas são escritos em [PlantUML](https://plantuml.com) com a biblioteca [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) e **desenhados para SVG**. O texto (`.puml`) é a fonte e muda no mesmo pull request que o código; o SVG gerado também vai para o repositório, para os diagramas se verem no GitHub e nos documentos sem ninguém ter de instalar nada. Em cada ficheiro Markdown, o diagrama é uma imagem (`![Título](diagrams/02-containers.svg)`) seguida da sua legenda.

**Onde ficam.** Em `docs/architecture/diagrams/`, lado a lado: `NN-nome.puml` e `NN-nome.svg`, com o número do documento a que pertencem (`02-containers.puml` para `02-containers.md`; vários diagramas no mesmo documento levam um sufixo, `03-logical-api-bookings.puml`).

**Como editar e voltar a desenhar.**

1. Edita o `.puml`. Usa a biblioteca incluída no próprio PlantUML (`!include <C4/C4_Container>`, `C4_Component`, `C4_Context`, `C4_Dynamic`, `C4_Deployment`), sem descarregar nada da rede, para o desenho ser sempre o mesmo.
2. Desenha todos os diagramas do repositório (precisa só do Docker) com `scripts/render-diagrams.sh` (ou `scripts/render-diagrams.ps1` no Windows). Os dois leem a versão do cabeçalho dos `.puml` (`' plantuml-version: X.Y.Z`) e usam a imagem `plantuml/plantuml:X.Y.Z`; recusam-se a correr se os ficheiros indicarem versões diferentes. O CI (job `diagrams`) corre o mesmo script e falha se algum `.svg` mudar, por isso a versão é a mesma em todo o lado: para a mudar, altera o cabeçalho de **todos** os `.puml`, volta a desenhar e faz commit dos SVG.
3. Confirma o resultado no SVG, e faz commit do `.puml` e do `.svg` juntos. Um SVG que não corresponde ao `.puml` é um erro de revisão.

**Cores (as do C4-PlantUML, sem as alterar).**

| Elemento | Macro | Cor | Forma |
| --- | --- | --- | --- |
| Pessoa (`P-`) | `Person` / `Person_Ext` | Azul-escuro `#08427B`, texto branco | Figura de pessoa |
| O nosso sistema (`C4 nível 1`) | `System` | Azul `#1168BD`, texto branco | Retângulo |
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

**Ids obrigatórios.** Todo o elemento de um diagrama usa um id do [inventário](00-inventory.md). Um elemento novo entra primeiro no inventário, no mesmo pull request; um diagrama sem id para um elemento está errado. Os ids `PL-` só aparecem em vistas do futuro, marcados como planeados.

**Atualização.** Um pull request que muda um elemento do inventário (cria, remove, muda de responsabilidade ou de caminho) atualiza o inventário e os diagramas que o mostram.

## Notas de arquitetura

Factos do código que os diagramas e as ADRs seguintes (P2 a P6) têm de dizer tal como são. Não são erros.

1. **A API também se alcança diretamente, em desenvolvimento.** Além de passar pelo Next.js, a API está publicada na porta 5080 do compose (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. O diagrama de contentores e o de implantação de desenvolvimento mostram as duas entradas (browser → C-WEB → C-API, e browser/ferramentas → C-API em 5080).
2. **Hoje o proxy reverso é o Next.js, não o Caddy.** O browser fala com C-WEB e é o Next.js (WEB-REWRITE) que reencaminha `/api/*` para C-API. Por isso a API só vê o endereço do contentor Next.js como IP do cliente (relevante para os limites por IP). O Caddy aparece só como planeado (PL-CADDY), e a [ADR 0009](adr/0009-alvo-de-implantacao.md) propõe não o usar: a solução prevista para a T06 está na [vista física](06-physical-view.md#44-o-ip-do-cliente), secção 4.4.
3. **Entidades persistidas fora do Domain.** A conta (`AppUser`, do Identity), o refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application. No Domain a conta existe só como regras (`Dom/Accounts`: email, nome, telemóvel, palavra-passe, perfis). O diagrama do domínio mostra estes elementos com a camada a que pertencem (`D-USER`, `D-REFRESH`, `D-STATUSCHG`, `D-OUTBOX`).
4. **Uma exceção ao relógio injetado.** Toda a hora atual vem de `TimeProvider` (CC-TIME), exceto o gerador de tokens de recuperação de palavra-passe do Identity, que lê o relógio do sistema e por isso não se testa com um relógio falso (os testes fabricam tokens antigos no formato do Identity).
5. **A exclusion constraint só cobre reservas `Confirmed`.** `bookings_no_overlap` (DB-NOOVERLAP) tem `WHERE status = 'Confirmed'`: uma reserva cancelada ou uma falta não ocupa o slot, e anular uma falta (ou qualquer volta a `Confirmed`) só tem sucesso se mais nenhuma reserva confirmada se sobrepuser entretanto; senão a base de dados recusa (23P01 → 409 `slot_taken`).
