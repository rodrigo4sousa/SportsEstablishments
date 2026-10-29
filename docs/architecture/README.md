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

**Ferramenta.** Os diagramas são escritos em Mermaid dentro do Markdown, para aparecerem no GitHub e mudarem no mesmo pull request que o código. Cada diagrama tem um título e, por baixo, a legenda dos elementos que usa.

**Cores (as do C4).**

| Elemento | Cor | Forma |
| --- | --- | --- |
| Pessoa (`P-`) | Azul-escuro `#08427B`, texto branco | Retângulo arredondado com o rótulo «Pessoa» |
| O nosso sistema / contentor (`C-`) | Azul `#1168BD`, texto branco | Retângulo |
| Componente (`API-`, `CC-`, `WEB-`) | Azul-claro `#85BBF0`, texto escuro | Retângulo |
| Sistema externo (`X-`) | Cinzento `#999999`, texto branco | Retângulo |
| Base de dados e tabelas (`C-DB`, `DB-`) | A cor do nível (azul ou azul-claro) | Cilindro |
| Entidade do domínio (`D-`) | Branco, contorno azul | Caixa de classe (nome, campos essenciais, invariantes em nota) |
| Planeado (`PL-`) | A cor do tipo, com contorno tracejado e o rótulo «planeado» | A forma do tipo |
| Fronteira (sistema, contentor, nó) | Sem preenchimento, contorno tracejado cinzento | Retângulo com o nome em cima |

**Rótulos dos elementos.** `ID · Nome` na primeira linha (por exemplo `API-BOOK · Reservas do jogador`) e, num contentor ou componente, a tecnologia entre parênteses retos na segunda (`[ASP.NET Core 10]`).

**Setas.** Uma seta tem sempre um rótulo em **pt-PT**, com um verbo do ponto de vista de quem chama e, a seguir, o protocolo ou formato entre parênteses retos: «Reserva um slot [HTTPS, JSON]», «Lê e escreve [SQL]», «Envia o email [SMTP]». Setas a tracejado são assíncronas (outbox, worker). A direção é a de quem inicia; uma resposta não leva seta própria, salvo nos diagramas de sequência.

**Tamanho.** No máximo cerca de 15 elementos por diagrama. Se passar disso, divide-se (por área, por funcionalidade ou por cenário) e liga-se um ao outro com uma nota.

**Ids obrigatórios.** Todo o elemento de um diagrama usa um id do [inventário](00-inventory.md). Um elemento novo entra primeiro no inventário, no mesmo pull request; um diagrama sem id para um elemento está errado. Os ids `PL-` só aparecem em vistas do futuro, marcados como planeados.

**Atualização.** Um pull request que muda um elemento do inventário (cria, remove, muda de responsabilidade ou de caminho) atualiza o inventário e os diagramas que o mostram.
