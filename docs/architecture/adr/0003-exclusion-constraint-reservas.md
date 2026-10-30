# 0003 · Exclusion constraint contra reservas duplicadas

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da [Stack](../../stack.md) e da T03).
- **Elementos:** DB-NOOVERLAP, DB-GIST, DB-BOOKINGS, D-BOOKING, API-BOOK, API-BOOK-STORE, API-CLUBBOOK, CC-LOCK, CC-SLOTCHECK.

## Contexto

Dois jogadores (ou um jogador e o dono, ou duas datas de uma série) podem pedir o mesmo slot ao mesmo tempo. A T03 pede que a base de dados impeça duas reservas confirmadas sobrepostas no mesmo campo, «e não só a validação na aplicação», e que o conflito seja um 409 e nunca um 500.

Alternativas:

1. **Verificar na aplicação antes de inserir.** Dois pedidos podem verificar ao mesmo tempo e inserir os dois.
2. **Isolamento `SERIALIZABLE`** ou um trinco consultivo por campo. Funciona, mas a garantia depende de todo o código passar por ali; um caminho novo que a esqueça duplica reservas.
3. **Uma exclusion constraint do PostgreSQL**, que vale para qualquer escrita.

## Decisão

Opção 3: na migração `AddBookings`, com SQL à mão e a extensão `btree_gist`,

```sql
ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
  EXCLUDE USING gist (pitch_id WITH =, tstzrange(starts_at, ends_at) WITH &&)
  WHERE (status = 'Confirmed');
```

- A violação (SQLSTATE `23P01`) é traduzida por `IBookingStore.TryAddAsync` para 409 `slot_taken` (ou o motivo `taken` numa série). O código **não procura** sobreposições entre reservas antes de inserir; a constraint tem a última palavra.
- O estado guarda-se como texto para a condição `WHERE` funcionar, e a constraint cobre só `Confirmed`: uma reserva cancelada ou uma falta liberta o slot.
- Reservas e bloqueios estão em tabelas diferentes, e a constraint não os cobre aos dois. Por isso criar uma reserva ou um bloqueio começa por trancar a linha do campo (`SELECT … FOR UPDATE`, CC-LOCK) e só depois verifica a sobreposição com os bloqueios; o trinco serve também o cancelamento, as faltas e as faixas de preço.
- Perder a corrida é esperado: o `BookingStore` regista-o em Information (campo e slot, nunca o jogador) e não como erro.

## Consequências

**Boas**

- A garantia vale para as três vias de reservar (jogador, série, dono), para anular uma falta e para qualquer caminho futuro.
- O teste da T03 (20 pedidos simultâneos para o mesmo slot) corre contra uma base de dados real e espera exatamente um 201 e os restantes 409.

**Más ou a vigiar**

- Anular uma falta volta a `Confirmed` e pode chocar com uma reserva feita entretanto: é a constraint que recusa (409 `slot_taken`), por `TrySaveChangesAsync`.
- A regra existe em SQL à mão numa migração, e quem testa precisa de PostgreSQL real (Testcontainers; o provider em memória do EF não a tem).
- Exige a extensão `btree_gist` no PostgreSQL de produção, e que o papel que migra a consiga criar ([ADR 0009](0009-alvo-de-implantacao.md), condição 3).
