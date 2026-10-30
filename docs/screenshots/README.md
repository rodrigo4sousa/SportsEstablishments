# Capturas de ecrã

Oito imagens da aplicação a funcionar com os dados de demonstração (`seed-demo`, ver o [README](../../README.md#demo-data)). Servem de apresentação do projeto; não são geradas pelo CI e podem ficar desatualizadas se a interface mudar.

| Ficheiro | O que mostra | Conta |
| --- | --- | --- |
| [01-club-search-results.png](01-club-search-results.png) | Pesquisa de clubes (`/`) com os filtros visíveis (desporto «Padel», todas as localidades, quarta-feira 11 de novembro) e 7 clubes com horários livres. | Visitante |
| [02-club-page-slots-and-prices.png](02-club-page-slots-and-prices.png) | Página do clube Arena Futsal Boavista: campos, preço base e a lista de slots do Futsal A, que tem uma faixa de preço (38,00 € até às 18:00, 46,00 € depois). | Visitante |
| [03-booking-summary-weekly-repeat.png](03-booking-summary-weekly-repeat.png) | Resumo da reserva do slot das 21:00 com «Repetir semanalmente» em 3 semanas e a pré-visualização das datas, com preço e «Livre». | `jogador01@example.test` |
| [04-my-bookings-upcoming-series.png](04-my-bookings-upcoming-series.png) | «As minhas reservas», separador «Próximas», página inteira; no fim, as três reservas da série semanal («Reserva semanal: 1 de 3», 2 de 3, 3 de 3). | `jogador01@example.test` |
| [05-owner-calendar-day.png](05-owner-calendar-day.png) | Calendário do dono, vista diária de 11 de novembro, Lisboa Padel Club: reservas, um bloqueio no Padel 1 (tracejado) e uma reserva criada pelo clube para um cliente («Criada pelo clube», Padel 2). | `dono04@example.test` |
| [06-owner-statistics-club.png](06-owner-statistics-club.png) | Estatísticas do Padel Foz Clube de 09/09/2026 a 10/10/2026: horas reservadas e abertas, ocupação, receita, reservas confirmadas e faltas por campo, e o total. | `dono01@example.test` |
| [07-admin-dashboard.png](07-admin-dashboard.png) | Painel do admin, página inteira, no mesmo período: totais, reservas ao longo do tempo (por dia), novos registos, clubes com maior ocupação e a tabela de donos. | Admin de demonstração |
| [08-admin-top-occupation-registrations.png](08-admin-top-occupation-registrations.png) | Recorte do mesmo painel: gráfico de novos registos por semana (donos e jogadores) e o top 10 de ocupação. | Admin de demonstração |

## Dados

- **Base:** `seed-demo` correu em 2026-10-30. Cria 5 donos, 60 jogadores, 10 clubes, 43 campos, 5 bloqueios e cerca de 10 400 reservas, que cobrem os 60 dias anteriores e os 14 seguintes, isto é, de 2026-08-31 a 2026-11-13. Todos os endereços são `example.test` e a palavra-passe das contas de demonstração é a do README.
- **Períodos usados:** as estatísticas (06, 07 e 08) usam 09/09/2026 a 10/10/2026, um intervalo todo dentro dos dados. As datas de 01 a 03 e 05 são de 11/11/2026 (12 dias depois), que o seed já preenche.
- **Acrescentado à mão depois do seed**, pela API e pela interface com contas de demonstração, para mostrar funcionalidades que o seed não cria: uma faixa de preço no Futsal A da Arena Futsal Boavista (segunda a sexta, 18:00–00:00, 46,00 €); a série semanal de `jogador01@example.test` (quartas-feiras 11, 18 e 25 de novembro às 21:00); um bloqueio no Padel 1 do Lisboa Padel Club (11 de novembro, 09:00–12:00, «Manutenção da iluminação»); e uma reserva para o cliente de demonstração «Rui Teixeira» no Padel 2 (15:00–16:30).
- **Datas com dia igual ao mês** (11/11, 09/09, 10/10): o campo de data do Chromium sem interface em pt-PT mostra-se em ordem mês/dia; com estas datas a leitura é a mesma nas duas ordens.

## Como foram feitas

Stack completa com `docker compose` num projeto à parte, `seed-demo`, e Chromium com Playwright (fora do repositório, não incluído): janela de 1440 px, fuso `Europe/Lisbon`, língua `pt-PT`, tema claro, build de produção (sem sobreposições de desenvolvimento). Cada PNG foi reduzido a uma paleta de 128 cores e verificado à vista: só aparecem dados de demonstração, sem segredos, tokens nem moldura do browser.
