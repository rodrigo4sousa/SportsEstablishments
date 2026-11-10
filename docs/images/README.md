# Capturas de ecrã

Oito imagens (e um GIF, mais abaixo) da aplicação a funcionar com os dados de demonstração (`seed-demo`, ver o [README](../../README.md#run-locally)). Servem de apresentação do projeto; não são geradas pelo CI e podem ficar desatualizadas se a interface mudar.

| Ficheiro | O que mostra | Conta |
| --- | --- | --- |
| [screenshot-club-search-results.png](screenshot-club-search-results.png) | Pesquisa de clubes (`/`) com os filtros visíveis (desporto «Padel», todas as localidades, quarta-feira 11 de novembro) e 7 clubes com horários livres. | Visitante |
| [screenshot-club-page-slots-and-prices.png](screenshot-club-page-slots-and-prices.png) | Página do clube Arena Futsal Boavista: campos, preço base e a lista de horários do Futsal A, que tem uma faixa de preço (38,00 € até às 18:00, 46,00 € depois). | Visitante |
| [screenshot-booking-summary-weekly-repeat.png](screenshot-booking-summary-weekly-repeat.png) | Resumo da reserva do horário das 21:00 de 11 de novembro com «Repetir semanalmente» em 3 semanas e a pré-visualização das datas (11, 18 e 25), com preço e «Livre». | `jogador01@example.test` |
| [screenshot-my-bookings-upcoming-series.png](screenshot-my-bookings-upcoming-series.png) | «As minhas reservas», separador «Próximas», página inteira; no fim, as três reservas da série semanal («Reserva semanal: 1 de 3», 2 de 3, 3 de 3). | `jogador01@example.test` |
| [screenshot-owner-calendar-day.png](screenshot-owner-calendar-day.png) | Calendário do dono, vista diária de 11 de novembro, Lisboa Padel Club, em página larga e com as colunas dos campos a ocupar a largura: reservas, um bloqueio no Padel 1 (tracejado) e uma reserva criada pelo clube para um cliente («Criada pelo clube», Padel 2). | `dono04@example.test` |
| [screenshot-owner-statistics-club.png](screenshot-owner-statistics-club.png) | Estatísticas do Padel Foz Clube de 09/09/2026 a 10/10/2026: horas reservadas e abertas, ocupação, receita, reservas confirmadas e faltas por campo, e o total. | `dono01@example.test` |
| [screenshot-admin-dashboard.png](screenshot-admin-dashboard.png) | Painel do admin, página inteira, no mesmo período: totais, reservas ao longo do tempo (por dia), novos registos (os números por cima das barras e a tabela por abrir), clubes com maior ocupação e a tabela de donos (nomes como ligações). | Admin de demonstração |
| [screenshot-admin-top-occupation-registrations.png](screenshot-admin-top-occupation-registrations.png) | Recorte do mesmo painel: gráfico de novos registos por semana (donos em cinzento escuro, jogadores em cinzento médio, com os números) e o top 10 de ocupação. | Admin de demonstração |

## GIF de demonstração

| Ficheiro | O que mostra | Contas |
| --- | --- | --- |
| [demo.gif](demo.gif) | Cerca de 45 s, 960×540 a 12 imagens por segundo, em ciclo: pesquisa de clubes, reserva de um horário livre (com a passagem pelo início de sessão), «As minhas reservas», o calendário do dono nesse dia e o painel do admin. | `jogador01@example.test`, `dono01@example.test` e o admin de demonstração |

Gravado localmente, com os dados de `seed-demo`, por um script que se pode voltar a correr: ver [docs/demo](../demo/README.md). Tal como as capturas, não é gerado pelo CI.

## Dados

- **Base:** `seed-demo` correu em 2026-10-30 (as capturas foram refeitas nesse dia, depois do lote 4 de UX, sobre uma base nova). Cria 5 donos, 60 jogadores, 10 clubes, 43 campos, 5 bloqueios e cerca de 10 400 reservas, que cobrem os 60 dias anteriores e os 14 seguintes, isto é, de 2026-08-31 a 2026-11-13. Todos os endereços são `example.test` e a palavra-passe das contas de demonstração é a do README.
- **Períodos usados:** as estatísticas (`screenshot-owner-statistics-club`, `screenshot-admin-dashboard` e `screenshot-admin-top-occupation-registrations`) usam 09/09/2026 a 10/10/2026, um intervalo todo dentro dos dados. As datas de `screenshot-club-search-results`, `screenshot-club-page-slots-and-prices`, `screenshot-booking-summary-weekly-repeat` e `screenshot-owner-calendar-day` são de 11/11/2026 (12 dias depois), que o seed já preenche.
- **Acrescentado à mão depois do seed**, pela API e pela interface com contas de demonstração, para mostrar funcionalidades que o seed não cria: uma faixa de preço no Futsal A da Arena Futsal Boavista (segunda a sexta, 18:00–00:00, 46,00 €); a série semanal de `jogador01@example.test` (quartas-feiras 11, 18 e 25 de novembro às 21:00), criada pela própria interface com «Confirmar 3 reservas» logo a seguir à captura `screenshot-booking-summary-weekly-repeat` (por isso `screenshot-my-bookings-upcoming-series` já a mostra e `screenshot-booking-summary-weekly-repeat` ainda mostra as três datas livres); um bloqueio no Padel 1 do Lisboa Padel Club (11 de novembro, 09:00–12:00, «Manutenção da iluminação»); e uma reserva para o cliente de demonstração «Rui Teixeira» no Padel 2 (15:00–16:30).
- **Datas com dia igual ao mês** (11/11, 09/09, 10/10): o campo de data do Chromium sem interface em pt-PT mostra-se em ordem mês/dia; com estas datas a leitura é a mesma nas duas ordens.

## Como foram feitas

Stack completa com `docker compose` num projeto à parte, `seed-demo`, e Chromium com Playwright (fora do repositório, não incluído): janela de 1440 px, fuso `Europe/Lisbon`, língua `pt-PT`, tema claro (a aplicação não tem tema escuro), build de produção (sem sobreposições de desenvolvimento). Cada PNG foi reduzido a uma paleta de 128 cores e verificado à vista: só aparecem dados de demonstração, sem segredos, tokens nem moldura do browser.

O script de captura não está no repositório. Quem voltar a captar escreve em `docs/images/`, com o nome `screenshot-<nome>.png` da tabela acima (a pasta chamava-se `docs/screenshots` e os ficheiros levavam um número à frente).
