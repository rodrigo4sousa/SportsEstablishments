# Registos de decisões de arquitetura (ADR)

Cada ADR regista uma decisão importante: o contexto, a decisão, as alternativas consideradas e as consequências. Uma ADR aceite não se reescreve; se a decisão mudar, escreve-se uma nova que a substitui e a antiga passa a «Substituída por …». Os elementos referidos usam os ids do [inventário](../00-inventory.md).

Nome dos ficheiros: `NNNN-titulo-curto.md` (por exemplo `0001-uma-app-tres-areas.md`).

**Estados:** «Por escrever» (ainda sem ficheiro), «Proposta» (escrita, ainda não aceite: a decisão pode mudar), «Aceite», «Substituída por …».

## Índice

| N.º | Título | Estado | Numa linha |
| --- | --- | --- | --- |
| 0001 | Uma só aplicação com três áreas | Por escrever | Um Next.js com `/`, `/manage` e `/admin` sobre uma só API, em vez de uma aplicação por perfil. |
| 0002 | Sessão em cookies httpOnly em vez de localStorage | Por escrever | JWT curto e refresh token rotativo em cookies httpOnly, na mesma origem, para que nenhum script consiga ler a sessão. |
| 0003 | Exclusion constraint contra reservas duplicadas | Por escrever | A base de dados garante que duas reservas confirmadas do mesmo campo nunca se sobrepõem, mesmo com pedidos simultâneos. |
| 0004 | Outbox para os emails | Por escrever | Os emails entram numa tabela na mesma transação da alteração e um worker envia-os depois, sem atrasar nem falhar os pedidos. |
| 0005 | Um marketplace em vez de um site por dono | Por escrever | Uma conta de jogador serve em todos os clubes de todos os donos; não há sites nem domínios por dono. |
| 0006 | Ids públicos GUID v7 | Por escrever | Ids `Guid.CreateVersion7()` em todas as entidades: não revelam quantas existem e ordenam-se pela criação. |
| 0007 | Slots calculados, não guardados | Por escrever | Os slots geram-se do horário quando são precisos; só se guardam reservas e bloqueios, e uma mudança de horário nunca toca nas reservas. |
| 0008 | Chaves do Data Protection na base de dados | Por escrever | As chaves que protegem os tokens de recuperação vivem no PostgreSQL, para sobreviverem a reinícios e servirem várias instâncias. |
| [0009](0009-alvo-de-implantacao.md) | Alvo de implantação | Proposta (T06) | Next.js num serviço web público do Render (única entrada), API com o worker num serviço privado do Render, Supabase e Brevo; sem Caddy. Substitui, quando aceite, a máquina virtual com Caddy da Stack. |
