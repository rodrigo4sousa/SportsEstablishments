# GIF de demonstração

Gera o [`docs/images/demo.gif`](../images/demo.gif) do README principal: a aplicação a funcionar, gravada **localmente**, contra a stack do `docker compose` com os dados de `seed-demo` (nunca contra produção). Não faz parte do CI e não é executado por nenhum workflow. Quem mudar a interface pode voltar a gravar com os passos abaixo.

## O que mostra (cerca de 45 s)

1. Pesquisa de clubes por desporto (Padel), localidade (Porto) e data (amanhã), e abre o Padel Foz Clube.
2. Escolhe o primeiro horário livre de amanhã (lido da página, não fixo no script), segue para «Entrar para reservar», entra como `jogador01@example.test`, volta ao mesmo horário e confirma.
3. «As minhas reservas» com a reserva nova.
4. Sai, entra como `dono01@example.test` (dono do Padel Foz Clube), abre o calendário no dia dessa reserva e abre os detalhes dela.
5. Sai, entra como admin e mostra o painel com os gráficos.

Só se usam contas de demonstração: a palavra-passe `demo-password-1` das contas `jogador*` e `dono*` é pública (ver o [README](../../README.md#run-locally)). A do admin é um valor descartável, escolhido para esta gravação local.

## Pré-requisitos

- Docker com Compose v2, Node.js e `ffmpeg` com os filtros `palettegen` e `paletteuse` (`ffmpeg -filters`). O `ffmpeg` não se instala por aqui.
- Portas 3000 e 5080 livres. A base de dados e o Mailpit não são publicados no anfitrião (`compose.override.yml`), por isso não chocam com uma stack de desenvolvimento que use a 5432.

## Gravar

A gravação cria uma reserva, por isso **a base de dados tem de ser nova antes de cada gravação** (o horário «primeiro livre de amanhã» muda e a conta de demonstração já pode ter reservas). Na raiz do repositório, num bash:

```bash
# 1. Stack própria (projeto e volume à parte: não toca na base de desenvolvimento)
export ADMIN_SEED_EMAIL=admin@example.test
export ADMIN_SEED_PASSWORD=<valor descartável, 8 ou mais caracteres>
DC="docker compose -p sportsbooking-demo -f docker-compose.yml -f docs/demo/compose.override.yml"
$DC down -v
$DC up -d --build --wait
$DC run --rm api seed-demo

# 2. Palavra-passe do admin, como no README («Changing the admin password»)
read -rs ADMIN_NEW_PASSWORD && export ADMIN_NEW_PASSWORD
$DC run --rm -e ADMIN_NEW_PASSWORD api set-admin-password

# 3. Gravar (primeira vez: instalar as dependências e o Chromium)
cd docs/demo
npm ci && npx playwright install chromium
ADMIN_PASSWORD="$ADMIN_NEW_PASSWORD" npm run record   # escreve out/demo.webm
npm run encode                                         # escreve ../images/demo.gif

# 4. Limpar: remove só este projeto e o seu volume
cd ../..
$DC down -v
unset ADMIN_NEW_PASSWORD ADMIN_SEED_PASSWORD
```

- **Saídas.** O vídeo e os ficheiros temporários ficam em `docs/demo/out/` (ignorada pelo Git); o resultado é `docs/images/demo.gif`.
- **Tamanho.** O objetivo é no máximo 8 MB. Se passar, `npm run encode -- 800 10` (largura e imagens por segundo) volta a codificar sem refazer a gravação.
- **O que o `record-demo.mjs` faz.** Primeiro aquece todas as rotas num contexto descartável (sem gravar), depois grava num contexto novo: 1280×720, `pt-PT`, `Europe/Lisbon`, sem extensões, com cursor e animação de clique desenhados pela própria página. Recusa gravar contra um endereço que não seja `localhost`. O `DEMO_URL` muda a porta, não o anfitrião.
- **Janela visível.** O Chromium corre com janela (não em modo sem interface), porque o campo de data em modo sem interface mostra o pt-PT como mês/dia e com janela mostra dia/mês. Não mexas no rato durante a gravação.
- **Depois de gravar.** Vê imagens do GIF em vários instantes (`ffmpeg -i ../images/demo.gif -vf fps=1/3 out/f%02d.png`): texto legível, sem estados a meio de carregar, sem *spinner* no fim e só dados de demonstração.

## Dados

As datas são relativas ao dia da gravação (o seed cobre os 60 dias anteriores e os 14 seguintes). O dia mostrado no GIF é o de amanhã nesse dia.
