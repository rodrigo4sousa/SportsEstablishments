# 0010 · Testes end-to-end com Playwright, em CI, contra o docker compose

- **Estado:** Aceite em 2026-11-10 (T08).
- **Data:** 2026-11-10.
- **Elementos:** DEV-E2E, PL-E2E, DEV-COMPOSE, DEV-WORKFLOWS, X-GHA, X-MAILPIT.

## Contexto

Até aqui os fluxos críticos percorriam-se à mão no browser, antes de cada merge, com a stack do `docker compose` (ver a [Stack](../../stack.md), «Testes»). Os testes de integração (Testcontainers) e os de componentes (MSW) cobrem as regras e o ecrã, cada um do seu lado; nada garante que o site, o rewrite do Next.js, a API e a base de dados funcionam juntos. Querem-se **poucos** testes end-to-end, estáveis, que corram no CI como sinal de qualidade.

O que o repositório impõe:

- **Uma só origem.** A sessão vive em cookies httpOnly e a API verifica o `Origin` (Development: `:3000` e `:5080`); o browser tem de falar com o site, que reencaminha `/api/*`.
- **Dados de demonstração** (`seed-demo`) e Mailpit já existem na stack do `docker compose`. Não há verificação de email no registo, por isso nenhum fluxo precisa de ler o Mailpit.
- **O `deploy.yml` espera pelo workflow `CI`.** Um passo lento ou instável no `ci.yml` atrasava ou bloqueava os deploys.

## Decisão

1. **Onde vive o código: `e2e/`, na raiz**, com `package.json` e `package-lock.json` próprios, e não `frontend/e2e`. Os testes não importam código do front end nem da API (só falam HTTP e o DOM), por isso não há razão para o `npm ci` e o Dependabot do front end carregarem o Playwright, nem para o Playwright ficar nas dependências do build do site. Segue o que `docs/demo/` já faz (um `package.json` à parte), sem importar nada de lá. Playwright e `@playwright/test` ficam fixados numa versão exata com pelo menos duas semanas (regra das dependências do projeto); o Dependabot tem uma entrada para `/e2e`.
2. **Como a stack corre no CI: o `docker compose` do repositório** (`postgres`, `mailpit`, `api`, `frontend`), construído no próprio runner, com `seed-demo`. `e2e/scripts/stack.mjs up` apaga o projeto `sportsbooking-e2e` (nome e volume próprios, para nunca tocar numa stack de desenvolvimento), constrói, espera por um pedido que passa pelo site, pelo rewrite, pela API e pela base de dados, e semeia. É o mesmo comando no CI e em local. Porque não «artefactos construídos» (`dotnet run` + `next start` no runner): seria uma segunda forma de arrancar a aplicação, diferente das imagens que se usam em produção (o `Dockerfile` do Render e o do site) e do que o README manda correr, e exigiria instalar o SDK .NET e um PostgreSQL só para isto. O custo é construir duas imagens (a medir; o alvo é menos de 8 minutos no total).
3. **Fluxos (só Chromium), três e não mais:** (1) o jogador entra, reserva um horário livre, vê-o em «As minhas reservas» e cancela; (2) dois jogadores com o mesmo horário no ecrã: o segundo recebe o 409 `slot_taken` e a mensagem, e só existe uma reserva; (3) o dono entra, vê a reserva no calendário, e um clube que não existe responde 404 a sério. O teste 2 não é uma corrida: a corrida a sério (20 pedidos simultâneos) já é um teste de integração e a [ADR 0003](0003-exclusion-constraint-reservas.md) garante-a; o que o browser acrescenta é o que o jogador vê quando perde.
4. **Dados e repetição.** Contas de demonstração (`jogador01`…`jogador04`, `dono01`), sempre o Padel Foz Clube. Cada teste usa o primeiro dia livre a partir do dia 20, 21 e 22 (o seed só reserva até 14 dias à frente), escolhido pela API pública, e cancela as reservas que fez no fim e no princípio (só as de dias ≥ 20, que nenhuma conta de demonstração tem), por isso podem correr outra vez na mesma base de dados. Quem não é o assunto do teste entra pela API e não pelo formulário (limite de 10 inícios de sessão por minuto e por IP).
5. **Estabilidade.** Localizadores por papel e nome (um `ul[aria-labelledby]` só para o grupo de um dia), esperas só por `expect`/auto-espera (nunca `sleep`), um worker, `retries: 1` só no CI, `trace` e `video` na primeira repetição, relatório e vídeos carregados como artefacto quando falha. A configuração recusa um endereço que não seja `localhost`.
6. **Um workflow à parte, `e2e.yml`** (em `pull_request` e em `push` à `main`), com `contents: read`, e não um job do `ci.yml`: o `deploy.yml` só espera pelo `CI`, por isso o E2E não o atrasa nem o bloqueia. Consequência aceite: como a `main` não tem proteção de branch, nada obriga a que o E2E esteja verde para fazer merge; confere-se à mão, como o CI.

## Alternativas consideradas

- `frontend/e2e/`: mais perto do site, mas mistura dependências e o Dependabot do front end.
- Um job no `ci.yml`: um E2E lento ou instável bloquearia os deploys.
- Contas novas por registo em cada teste: independente do seed, mas mais lento e esbarra no limite de 5 registos por minuto e por IP; o pedido era usar contas semeadas.
- Mais fluxos (admin, séries, recuperação de palavra-passe): ficam nos testes de componentes e de integração; cada fluxo end-to-end acrescenta tempo e risco de instabilidade.

## Consequências

- Mais um workflow (cerca de metade do tempo é construir as imagens) e uma pasta com as suas próprias dependências.
- O que o browser vê passa a ser verificado com a stack inteira em cada PR; um erro de configuração entre o site, o rewrite e a API (o `Origin`, os cookies, o proxy) aparece aqui e não só em produção.
- Os fluxos dependem de textos e papéis da interface em pt-PT; mudar o texto de um botão obriga a mudar o teste.
