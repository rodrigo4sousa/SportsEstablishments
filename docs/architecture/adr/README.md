# Registos de decisões de arquitetura (ADR)

Cada ADR regista uma decisão importante: o contexto, a decisão, as alternativas consideradas e as consequências. Uma ADR aceite não se reescreve; se a decisão mudar, escreve-se uma nova que a substitui e a antiga passa a «Substituída por …». Os elementos referidos usam os ids do [inventário](../00-inventory.md).

Nome dos ficheiros: `NNNN-titulo-curto.md` (por exemplo `0001-uma-app-tres-areas.md`).

## Por escrever

| N.º | Título | Numa linha |
| --- | --- | --- |
| 0001 | Uma só aplicação com três áreas | Um Next.js com `/`, `/manage` e `/admin` sobre uma só API, em vez de uma aplicação por perfil. |
| 0002 | Sessão em cookies httpOnly em vez de localStorage | JWT curto e refresh token rotativo em cookies httpOnly, na mesma origem, para que nenhum script consiga ler a sessão. |
| 0003 | Exclusion constraint contra reservas duplicadas | A base de dados garante que duas reservas confirmadas do mesmo campo nunca se sobrepõem, mesmo com pedidos simultâneos. |
| 0004 | Outbox para os emails | Os emails entram numa tabela na mesma transação da alteração e um worker envia-os depois, sem atrasar nem falhar os pedidos. |
| 0005 | Um marketplace em vez de um site por dono | Uma conta de jogador serve em todos os clubes de todos os donos; não há sites nem domínios por dono. |
| 0006 | Ids públicos GUID v7 | Ids `Guid.CreateVersion7()` em todas as entidades: não revelam quantas existem e ordenam-se pela criação. |
| 0007 | Slots calculados, não guardados | Os slots geram-se do horário quando são precisos; só se guardam reservas e bloqueios, e uma mudança de horário nunca toca nas reservas. |
| 0008 | Chaves do Data Protection na base de dados | As chaves que protegem os tokens de recuperação vivem no PostgreSQL, para sobreviverem a reinícios e servirem várias instâncias. |
| 0009 | Alvo de implantação | Onde e como a aplicação corre em produção (a escrever na fase P5, com a T06). |
