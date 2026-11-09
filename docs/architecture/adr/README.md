# Registos de decisões de arquitetura (ADR)

Cada ADR regista uma decisão importante: o contexto, a decisão, as alternativas consideradas e as consequências. Uma ADR aceite não se reescreve; se a decisão mudar, escreve-se uma nova que a substitui e a antiga passa a «Substituída por …». Os elementos referidos usam os ids do [inventário](../00-inventory.md).

Nome dos ficheiros: `NNNN-titulo-curto.md` (por exemplo `0001-uma-app-tres-areas.md`).

**Estados:** «Por escrever» (ainda sem ficheiro; hoje nenhuma), «Proposta» (escrita, ainda não aceite: a decisão pode mudar), «Aceite», «Substituída por …».

## Índice

| N.º | Título | Estado | Numa linha |
| --- | --- | --- | --- |
| [0001](0001-uma-app-tres-areas.md) | Uma só aplicação com três áreas | Aceite | Um Next.js com `/`, `/manage` e `/admin` sobre uma só API, em vez de uma aplicação por perfil. |
| [0002](0002-sessao-em-cookies-httponly.md) | Sessão em cookies httpOnly em vez de localStorage | Aceite | JWT curto e refresh token rotativo em cookies httpOnly, na mesma origem, para que nenhum script consiga ler a sessão. |
| [0003](0003-exclusion-constraint-reservas.md) | Exclusion constraint contra reservas duplicadas | Aceite | A base de dados garante que duas reservas confirmadas do mesmo campo nunca se sobrepõem, mesmo com pedidos simultâneos. |
| [0004](0004-outbox-para-emails.md) | Outbox para os emails | Aceite | Os emails entram numa tabela na mesma transação da alteração e um worker envia-os depois, sem atrasar nem falhar os pedidos. |
| [0005](0005-marketplace-unico.md) | Um marketplace em vez de um site por dono | Aceite | Uma conta de jogador serve em todos os clubes de todos os donos; não há sites nem domínios por dono. |
| [0006](0006-ids-guid-v7.md) | Ids públicos GUID v7 | Aceite | Ids `Guid.CreateVersion7()` nas entidades de negócio e de contas: não revelam quantas existem e ordenam-se pela criação. |
| [0007](0007-slots-calculados.md) | Slots calculados, não guardados | Aceite | Os slots geram-se do horário quando são precisos; só se guardam reservas e bloqueios, e uma mudança de horário nunca toca nas reservas. |
| [0008](0008-chaves-data-protection-na-base-de-dados.md) | Chaves do Data Protection na base de dados | Aceite | As chaves que protegem os tokens de recuperação vivem no PostgreSQL, para sobreviverem a reinícios e servirem várias instâncias. |
| [0009](0009-alvo-de-implantacao.md) | Alvo de implantação | Aceite (T06) | Site na Vercel, API com o worker num serviço público do Render gratuito, Supabase, Brevo e GitHub Actions; o Next.js é o proxy da mesma origem, sem Caddy. Substitui a máquina virtual com Caddy da Stack. Aceite depois do primeiro deploy real. |
