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
| [03-components.md](03-components.md) | Nível 3 · Componentes | De desenvolvimento | Como se organiza o código dentro da API e do front end? |
| [04-domain.md](04-domain.md) | Nível 4 · Código (modelo do domínio) | Lógica | Que conceitos e invariantes tem o domínio, e onde vivem? |
| [05-runtime.md](05-runtime.md) | Diagramas dinâmicos | De processo | Como colaboram as partes num pedido, numa transação e em segundo plano? |
| [06-deployment.md](06-deployment.md) | Diagrama de implantação | Física | Onde corre cada contentor, em desenvolvimento, no CI e em produção? |
| [07-scenarios.md](07-scenarios.md) | Diagramas dinâmicos (por caso de uso) | Cenários (+1) | Os casos de uso que atravessam todas as vistas e as validam. |
| [adr/](adr/README.md) | — | — (justificação transversal) | Porque é que as escolhas importantes são as que são? |

## Nível de abstração de cada vista

| Vista | Mostra | Não mostra |
| --- | --- | --- |
| 01 Contexto | Pessoas (`P-`), o sistema como uma caixa, sistemas externos (`X-`). | Tecnologias, contentores, código. |
| 02 Contentores | Contentores (`C-`), sistemas externos, protocolos e formatos nas setas. | Classes, pastas, componentes internos. |
| 03 Componentes | Componentes de um contentor de cada vez (`API-`, `CC-` ou `WEB-`) e as suas dependências. | Classes individuais, salvo quando são o próprio componente. |
| 04 Domínio | Entidades e valores (`D-`), relações e invariantes, objetos da base de dados que os garantem (`DB-`). | Controllers, HTTP, ecrãs. |
| 05 Execução | Uma sequência por diagrama: pedido, transação, trinco, outbox, worker. | Todas as variantes de erro de uma só vez (uma variante por diagrama). |
| 06 Implantação | Nós, contentores implantados, portas, redes, variáveis de configuração que mudam o comportamento. | Lógica de negócio. |
| 07 Cenários | Um caso de uso de ponta a ponta, com as vistas que atravessa. | Detalhe que já está nas outras vistas (aponta para elas). |

## Legenda e notação

**Ferramenta.** Os diagramas são escritos em [PlantUML](https://plantuml.com) com a biblioteca [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) e **desenhados para SVG**. O texto (`.puml`) é a fonte e muda no mesmo pull request que o código; o SVG gerado também vai para o repositório, para os diagramas se verem no GitHub e nos documentos sem ninguém ter de instalar nada. Em cada ficheiro Markdown, o diagrama é uma imagem (`![Título](diagrams/02-containers.svg)`) seguida da sua legenda.

**Onde ficam.** Em `docs/architecture/diagrams/`, lado a lado: `NN-nome.puml` e `NN-nome.svg`, com o número do documento a que pertencem (`02-containers.puml` para `02-containers.md`; vários diagramas no mesmo documento levam um sufixo, `03-components-api-bookings.puml`).

**Como editar e voltar a desenhar.**

1. Edita o `.puml`. Usa a biblioteca incluída no próprio PlantUML (`!include <C4/C4_Container>`, `C4_Component`, `C4_Context`, `C4_Dynamic`, `C4_Deployment`), sem descarregar nada da rede, para o desenho ser sempre o mesmo.
2. Desenha todos os diagramas do repositório com o PlantUML em Docker (precisa só do Docker):

   ```bash
   docker run --rm -v "$(pwd)/docs/architecture/diagrams:/data" plantuml/plantuml -tsvg "/data/*.puml"
   ```

   (Com o `plantuml.jar` e Java: `java -jar plantuml.jar -tsvg docs/architecture/diagrams/*.puml`.) A versão do PlantUML usada fica registada no cabeçalho de cada `.puml`, para uma nova versão não mudar os desenhos sem se dar por isso.
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
2. **Hoje o proxy reverso é o Next.js, não o Caddy.** O browser fala com C-WEB e é o Next.js (WEB-REWRITE) que reencaminha `/api/*` para C-API. Por isso, até haver um proxy à frente (PL-CADDY, T06), a API só vê o endereço do contentor Next.js como IP do cliente (relevante para os limites por IP). O Caddy aparece só como planeado.
3. **Entidades persistidas fora do Domain.** A conta (`AppUser`, do Identity), o refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application. No Domain a conta existe só como regras (`Dom/Accounts`: email, nome, telemóvel, palavra-passe, perfis). O diagrama do domínio mostra estes elementos com a camada a que pertencem (`D-USER`, `D-REFRESH`, `D-STATUSCHG`, `D-OUTBOX`).
4. **Uma exceção ao relógio injetado.** Toda a hora atual vem de `TimeProvider` (CC-TIME), exceto o gerador de tokens de recuperação de palavra-passe do Identity, que lê o relógio do sistema e por isso não se testa com um relógio falso (os testes fabricam tokens antigos no formato do Identity).
5. **A exclusion constraint só cobre reservas `Confirmed`.** `bookings_no_overlap` (DB-NOOVERLAP) tem `WHERE status = 'Confirmed'`: uma reserva cancelada ou uma falta não ocupa o slot, e anular uma falta (ou qualquer volta a `Confirmed`) só tem sucesso se mais nenhuma reserva confirmada se sobrepuser entretanto; senão a base de dados recusa (23P01 → 409 `slot_taken`).
