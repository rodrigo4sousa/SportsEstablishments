# 0005 · Um marketplace em vez de um site por dono

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem do [backlog](../../backlog.md), «Regras decididas», e das US01 e US02).
- **Elementos:** P-JOG, P-DONO, P-ADMIN, P-CLI, D-CLUB, D-USER, API-CATALOG, API-CLUBS, CC-AUTHZ.

## Contexto

O produto serve clubes de vários donos. Cada dono podia ter o seu site e o seu domínio, com os seus próprios jogadores, ou todos podiam partilhar uma plataforma.

Alternativas:

1. **Um site (ou domínio) por dono**, cada um com os seus jogadores. Isola os donos, mas multiplica a configuração, os certificados e as contas, e obriga um jogador a registar-se em cada clube.
2. **Um marketplace**: uma só aplicação, uma só base de dados, contas partilhadas.

## Decisão

Opção 2, como regra de negócio decidida:

- **Uma conta de jogador serve em todos os clubes de todos os donos.** Não há sites nem domínios por dono.
- Um email = uma conta = um perfil (`Player`, `Owner` ou `Admin`). Um dono pode ter vários clubes; um clube tem um só dono, para sempre (`OwnerId` só na criação).
- O público vê os clubes pelo catálogo (`/` e `/clubs/{slug}`); o `slug` é único, gerado do nome na criação e nunca muda. A área do dono usa o GUID público (`/manage/clubs/{id}`).
- Os donos ficam isolados uns dos outros por autorização, não por separação física: todo o acesso a clubes na área do dono parte de `OwnedClubs`, e um clube de outro dono dá o mesmo 404 que um que não existe (T02).
- O admin, o dono da plataforma, vê só contagens, nunca dados pessoais de jogadores; o dono vê só o nome do jogador que reservou.
- Um dono desativado (US29) deixa de aparecer nas páginas públicas e de receber reservas novas, mas as reservas já feitas ficam.
- Quem reserva por telefone ou à porta não tem conta: o dono reserva em nome dele (nome e telemóvel, US13), e essa reserva não está na lista de nenhum jogador.

## Consequências

**Boas**

- Um jogador regista-se uma vez e pesquisa em todos os clubes; só há uma aplicação para configurar e implantar.
- A pesquisa e as estatísticas da plataforma (admin) são consultas sobre os mesmos dados.

**Más ou a vigiar**

- Todos os dados partilham uma base de dados: o isolamento entre donos é só código e testes. Cada recurso de um clube tem de passar por `OwnedClubs`/`OwnedPitches`/`OwnedBookings`, e `OwnerAreaAccessTests` descobre as rotas de `/api/manage` para o verificar.
- As respostas públicas e as do admin têm de nunca levar dados pessoais; cada endpoint tem um teste que procura o nome, o email e o id de um jogador no corpo.
- O registo revela que um email já tem conta (409 `email_taken`), compromisso aceite na US01.
