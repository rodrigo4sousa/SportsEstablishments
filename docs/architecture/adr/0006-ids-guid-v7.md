# 0006 · Ids públicos GUID v7

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da [Stack](../../stack.md), «Regras de código»).
- **Elementos:** D-CLUB, D-PITCH, D-BLOCK, D-PRICEBAND, D-BOOKING, D-USER, D-REFRESH, D-OUTBOX, D-STATUSCHG, C-DB.

## Contexto

Os ids aparecem nos endereços (`/manage/clubs/{id}`), nas respostas da API e nas ligações. Um número sequencial revelaria quantas reservas ou clubes existem e seria fácil de adivinhar; um GUID aleatório (v4) não se adivinha, mas espalha as inserções pelo índice da chave primária.

Alternativas:

1. **Inteiros sequenciais (identity).** Compactos e ordenados, mas adivinháveis e atribuídos pela base de dados.
2. **GUID v4** (`Guid.NewGuid()`). Não se adivinham, mas os índices fragmentam-se.
3. **GUID v7** (`Guid.CreateVersion7()`): aleatório, com o tempo nos primeiros bits.

## Decisão

Opção 3: as entidades de negócio e de contas nascem com `Guid.CreateVersion7()` gerado no código (`Club`, `Pitch`, `Block`, `PitchPriceBand`, `Booking.ForPlayer`/`ForCustomer`, `OutboxMessage`, `RefreshToken`, `AppUser`, `OwnerStatusChange`, e o `SeriesId` de uma série semanal). Não há ids gerados pela base de dados.

## Consequências

**Boas**

- Não revelam quantas entidades existem e não se adivinham (um GUID não é, contudo, uma autorização: a posse continua a verificar-se, ver [ADR 0005](0005-marketplace-unico.md)).
- Ordenam-se aproximadamente pela criação, por isso os índices crescem no fim.
- A entidade tem o id antes de ser gravada, o que deixa ligar objetos (por exemplo uma reserva e a sua mensagem do outbox) sem ir à base de dados.

**Más ou a vigiar**

- 16 bytes por chave em vez de 4 ou 8, e em todas as chaves estrangeiras.
- Os ids contêm o instante de criação, por isso um id público revela quando a entidade foi criada. Não é segredo, mas um id não deve ser usado como se fosse um token.
- Um id que não é um GUID válido é recusado cedo (404 nas páginas, ignorado nos endereços do calendário).
