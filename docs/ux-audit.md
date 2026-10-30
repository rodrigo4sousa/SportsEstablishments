# Auditoria de UX e acessibilidade

Oct 30, 2026 · antes da T06

Auditoria da aplicação a correr (stack completa com os dados de demonstração), para decidir o que melhorar antes do deploy. Não altera código: cada problema tem uma correção proposta e uma estimativa, e a secção 5 propõe a ordem do trabalho.

## 1. Método e cobertura

**Ambiente.** `docker compose` num projeto à parte (`-p uxaudit`), build de produção do front end, `seed-demo` (5 donos, 60 jogadores, 10 clubes, 43 campos, cerca de 10 400 reservas) e, acrescentada pela API, uma faixa de preço no Futsal A da Arena Futsal Boavista (dias úteis, 18:00–00:00, 46,00 €), porque o seed não cria faixas. Só contas de demonstração (`example.test`) e um admin de demonstração. Tudo removido no fim.

**Ferramentas.** Playwright (Chromium) e axe-core 4.14, ambos fora do repositório, com regras `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa` e `best-practice`. Em cada ecrã: captura de página inteira, axe, título, cabeçalhos, landmarks, scroll horizontal, alvos pequenos, layout shift (CLS) e o elemento com foco depois de cada ação. As capturas ficaram fora do repositório.

**Larguras.** Desktop 1440×900 e mobile 390×844 (emulação com toque), locale `pt-PT`, fuso `Europe/Lisbon`.

**Coberto.**

- Visitante: início e pesquisa, filtros, resultados (com e sem resultados), página do clube, slots, campo com faixas de preço, convite para entrar, registo (vazio e com erros), registo de dono, login, um 429 real no login, recuperação de palavra-passe, ligação de recuperação sem token, 404 de clube e 404 global.
- Jogador: resumo da reserva, repetição semanal (pré-visualização e confirmação), confirmação simples, «As minhas reservas» (próximas e anteriores), cancelar dentro da janela, botão desativado fora da janela, corrida real pelo mesmo slot (`slot_taken`), erro 500 e falha de rede ao cancelar, carregamento lento, estado vazio (conta nova criada pela interface).
- Dono: `/manage` (com clubes e vazio, conta nova), edição do clube e gravação, campos, horário, bloqueios (com erros do formulário), faixas de preço, novo clube, calendário diário e semanal, «Nova reserva», detalhes de uma reserva passada, marcar falta, cancelar com motivo (vazio e preenchido), estatísticas gerais e por clube, 404 de clube.
- Admin: painel (totais, dois gráficos, top 10, donos), pesquisa e ordenação dos donos, página de um dono, formulário de desativação (vazio), 404 de dono, carregamento lento.
- Global: cabeçalhos por perfil, carregamento, estados vazios, páginas de erro e 404, toasts (500 no login), foco depois das ações, títulos das páginas.
- Teclado (desktop): ordem de foco no início, login só com teclado, reservar um slot só com teclado, separadores de «As minhas reservas», abrir uma reserva no calendário e fechá-la com Escape. O aspeto do foco foi conferido em imagens dos elementos focados.

**Não coberto.**

- Leitor de ecrã real (NVDA, VoiceOver); só o que o axe e o DOM mostram (nomes, papéis, regiões vivas).
- Telemóveis reais e Safari/Firefox. Em emulação mobile, o `tap()` do Playwright nos botões de cada campo («Horário», «Bloqueios», «Faixas de preço») não chegou a clicar, embora o teste de clique no centro do botão acerte nele: tratado como artefacto da emulação, a confirmar num telemóvel.
- Zoom a 200 %, largura de 320 px, modo escuro (o `next-themes` está instalado), movimento reduzido.
- Fluxos que pedem email real: definir a nova palavra-passe com um token válido. Série com datas recusadas (`series_unavailable`), anular falta, desativar e reativar um dono até ao fim, criar e editar uma faixa de preço com erros.
- Os campos `type=date` e `type=time` do Chromium sem interface em pt-PT mostram «10/30/2026» e «12:00 AM»; num browser em português mostram dd/mm/aaaa e 24 h. Não é um problema da aplicação e não entra nos achados.

## 2. Achados

Severidade: **blocker** impede a tarefa; **major** dificulta muito a tarefa ou falha WCAG AA de forma visível; **minor** atrapalha mas tem caminho; **polish** acabamento. Esforço: **S** até meio dia, **M** um a dois dias, **L** mais.

### UX-001 · Indicador de foco quase invisível

- **Ecrã e perfil:** todos. **Viewport:** ambos. **Severidade:** major. **Esforço:** S–M.
- **O que se vê:** com teclado, o foco num campo, numa lista ou num link do cabeçalho é um anel cinzento muito claro; no botão principal preto («Entrar», «Confirmar reserva») não se vê anel nenhum. Os slots e as caixas do calendário têm um anel cinzento fino, que mal se distingue da borda.
- **Porque importa:** quem usa teclado não sabe onde está. Falha WCAG 2.4.7 e 1.4.11 (contraste de componentes, 3:1). A causa é uma só: `--ring` é `oklch(0.708 0 0)` e os componentes usam `ring-ring/50` (metade da opacidade).
- **Correção:** um `--ring` escuro com opacidade total (por exemplo `oklch(0.205 0 0)`, o `--primary`) e um estilo de foco comum (`focus-visible:outline-2 focus-visible:outline-offset-2`) em `Button`, `Input`, `NativeSelect`, nos links e nos botões feitos à mão (`SlotList`, separadores, `BookingBox`, `HourCell` não, que está fora do Tab).

### UX-002 · O foco cai no `<body>` depois das ações

- **Ecrã e perfil:** jogador (confirmar reserva e série, abrir e confirmar cancelamento), dono (marcar falta, cancelar), admin (abrir «Desativar dono»), login falhado. **Viewport:** ambos. **Severidade:** major. **Esforço:** M.
- **O que se vê:** depois de «Confirmar reserva», o botão desaparece e o foco volta ao início do documento; a próxima tecla Tab leva ao logótipo. O mesmo ao abrir a confirmação de cancelamento: o botão «Cancelar reserva» é substituído e o foco perde-se, por isso «Sim, cancelar reserva» fica a dezenas de Tabs. A confirmação (`role="status"`) é anunciada, mas o utilizador de teclado tem de voltar a encontrar o sítio.
- **Porque importa:** WCAG 2.4.3 (ordem do foco). É o padrão mais repetido da auditoria.
- **Correção:** uma regra única: quando um elemento com foco é substituído, o foco vai para o que o substituiu. O título da confirmação ou do aviso leva `tabIndex={-1}` e recebe o foco. Um formulário que abre focar o primeiro campo, e ao fechar volta ao botão que o abriu. Um pequeno hook partilhado (`useFocusOnMount`) resolve quase todos os casos.

### UX-003 · O aviso de cancelamento aparece fora de vista

- **Ecrã e perfil:** «As minhas reservas», jogador. **Viewport:** ambos (pior em mobile). **Severidade:** major. **Esforço:** S.
- **O que se vê:** ao cancelar uma reserva mais abaixo na lista, a confirmação fecha e o cartão muda de estado, mas a mensagem «Reserva cancelada. O horário voltou a ficar livre.» é escrita no topo da lista: medido a 3 700 px acima do viewport em desktop e 5 900 px em mobile. O mesmo para as recusas e para um erro 500 (ver UX-019). Quem não usa leitor de ecrã não vê nada a acontecer.
- **Porque importa:** a ação mais sensível do jogador fica sem resposta visível; o utilizador pode repetir ou pensar que falhou.
- **Correção:** mostrar o aviso dentro do cartão, no lugar da confirmação (sucesso a verde e recusa a vermelho), e pôr-lhe o foco (UX-002). O aviso no topo pode ficar só para erros que não pertencem a um cartão.

### UX-004 · Os detalhes de uma reserva abrem longe da grelha

- **Ecrã e perfil:** calendário, dono. **Viewport:** ambos. **Severidade:** major. **Esforço:** M.
- **O que se vê:** ao carregar numa reserva, os detalhes (com «Marcar falta» e «Cancelar reserva») aparecem por baixo da grelha: medido a 1 400–1 740 px do topo do viewport em desktop e 1 370 px em mobile. Nada se mexe, o foco fica na caixa e Escape não fecha. O dono tem de saber que deve fazer scroll.
- **Porque importa:** é o caminho de todas as ações do dono sobre uma reserva (US12, US15).
- **Correção:** em desktop, um painel lateral fixo à direita da grelha; em mobile, uma folha que sobe de baixo. Em alternativa mínima: `scrollIntoView` e foco no título «Reserva» ao abrir, Escape fecha e devolve o foco à caixa.

### UX-005 · A grelha do calendário não usa o ecrã

- **Ecrã e perfil:** calendário diário e semanal, dono. **Viewport:** ambos. **Severidade:** major. **Esforço:** M–L.
- **O que se vê:** em 1440 px o calendário fica na coluna de 768 px de todas as páginas (`max-w-3xl`), com colunas fixas de 176 px (`w-44`): três campos cabem, um clube com quatro ou cinco campos já pede scroll horizontal, enquanto mais de 600 px do ecrã ficam vazios. Em 390 px vê-se a coluna das horas e um campo e meio, sem nenhum sinal de que a grelha continua para o lado. O texto das caixas é cortado («Confirmada · Criada pelo cl…»).
- **Porque importa:** é o ecrã que o dono abre todos os dias.
- **Correção:** o calendário numa largura própria (como o admin, `max-w-6xl`, ou a largura toda), colunas flexíveis com mínimo, coluna das horas e cabeçalho dos campos fixos ao fazer scroll. Em mobile, decidir (secção 6): um campo de cada vez com seletor (como a vista semanal) ou scroll com sombra e indicação «desliza para ver mais campos».

### UX-006 · Estatísticas em mobile escondem colunas e não se alcançam por teclado

- **Ecrã e perfil:** estatísticas do clube e estatísticas gerais, dono. **Viewport:** mobile. **Severidade:** major. **Esforço:** M.
- **O que se vê:** em 390 px a tabela mostra Campo, Horas reservadas, Horas abertas e Ocupação e parece completa; Receita, Confirmadas e Faltas estão escondidas à direita, num contentor com scroll sem pista visual. Os nomes dos campos partem («Futebol / 7»). O contentor (`.overflow-x-auto`) não é focável, por isso o teclado não o desloca (axe `scrollable-region-focusable`).
- **Porque importa:** a receita, o número que o dono mais procura, fica invisível.
- **Correção:** em ecrãs pequenos, um cartão por campo com os sete números em duas colunas; ou manter a tabela com a primeira coluna fixa, sombra de scroll e `tabIndex={0}`, `role="region"` e `aria-label` no contentor (vale também para as tabelas do admin).

### UX-007 · A página 404 global está em inglês e é um beco sem saída

- **Ecrã e perfil:** qualquer endereço desconhecido, todos. **Viewport:** ambos. **Severidade:** major. **Esforço:** S.
- **O que se vê:** «404 | This page could not be found.» (a página por omissão do Next.js), título «404: This page could not be found.», sem cabeçalho, sem ligação para o início, sem `<main>` (axe `landmark-one-main`, `region`).
- **Porque importa:** quebra a regra de interface em pt-PT e deixa o visitante sem caminho. Uma ligação antiga partilhada cai aqui.
- **Correção:** `app/not-found.tsx` em pt-PT, com o cabeçalho público, «Página não encontrada», ligações para a pesquisa e para entrar, e um título próprio.

### UX-008 · Cores que falham o contraste AA

- **Ecrã e perfil:** vários (lista abaixo). **Viewport:** ambos. **Severidade:** major. **Esforço:** S.
- **O que se vê:** dois pares de cores repetem-se. Texto de erro `#e7000b` sobre o fundo rosado dos alertas `#fde6e7`: 4,0:1 (login com 429, ligação de recuperação inválida, slot ocupado, erros de motivo). Cinzento `--muted-foreground` `#737373` sobre `--muted` `#f5f5f5` ou sobre fundos tingidos: 4,33–4,34:1 (estado «Cancelada»/«Realizada» nas reservas anteriores, slots «Reservado» da pré-visualização, linha da lista de clubes do dono ao passar o rato, ajudas dos campos de motivo).
- **Porque importa:** WCAG 1.4.3 (4,5:1 para texto normal). São 48 nós em 9 ecrãs.
- **Correção:** nos tokens, uma vez: `--muted-foreground` um pouco mais escuro (cerca de `oklch(0.50 0 0)`, mais de 5:1 sobre `--muted`) e o texto dos alertas destrutivos num vermelho escuro (`text-red-800` ou um token `--destructive-foreground` próprio para fundos claros).

### UX-009 · Nos resultados, só o nome do clube é uma ligação

- **Ecrã e perfil:** pesquisa (e o nome do clube em «As minhas reservas»), visitante e jogador. **Viewport:** ambos (pior em mobile). **Severidade:** major. **Esforço:** S.
- **O que se vê:** cada resultado é um cartão com borda que parece clicável, mas só o nome (cerca de 21 px de altura) leva ao clube; tocar no resto do cartão, ou em «Há horários livres», não faz nada.
- **Porque importa:** é o passo entre pesquisar e reservar. Em mobile o alvo fica abaixo de 24 px de altura (WCAG 2.5.8).
- **Correção:** o cartão inteiro como ligação (padrão de ligação esticada: `a::after` com `inset-0` dentro de um cartão `relative`), com estado de hover e foco no cartão.

### UX-010 · Na página do clube, a reserva está no fim

- **Ecrã e perfil:** página do clube, visitante e jogador. **Viewport:** ambos (pior em mobile). **Severidade:** major. **Esforço:** M.
- **O que se vê:** a ordem é título, «← Pesquisar clubes», Contactos, Campos, e só depois Horários. Em mobile os horários começam a cerca de 750 px e o resumo com «Confirmar reserva» fica por baixo da grelha de slots: depois de escolher um slot, nada indica que é preciso descer. A ligação de voltar aparece por baixo do título.
- **Porque importa:** é o ecrã onde o jogador decide reservar; o passo principal é o último a aparecer.
- **Correção:** Horários logo a seguir ao título; contactos numa coluna lateral em desktop e no fim em mobile; a ligação de voltar antes do título. Em mobile, uma barra fixa em baixo com o slot escolhido, o preço e o botão principal, que aparece quando há um slot escolhido.

### UX-011 · Títulos das páginas genéricos

- **Ecrã e perfil:** vários. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** a página de qualquer clube chama-se «Clube · Reservas de Campos»; `/`, `/manage` e `/admin` usam só «Reservas de Campos»; o calendário e as estatísticas não dizem de que clube são; a página de um dono é «Dono · Administração», com outro sufixo.
- **Porque importa:** separadores, histórico e leitores de ecrã não distinguem as páginas (WCAG 2.4.2).
- **Correção:** `generateMetadata` com o nome do clube ou do dono onde já é lido no servidor (`loadPublicClub`), títulos próprios nas restantes («Pesquisar clubes», «Os teus clubes», «Administração») e um sufixo único.

### UX-012 · Páginas do dono e do admin sem o nome do que mostram

- **Ecrã e perfil:** calendário, edição do clube, página do dono (admin). **Viewport:** ambos. **Severidade:** major. **Esforço:** S.
- **O que se vê:** o `h1` é «Calendário», «Editar clube» ou «Dono». No calendário o nome do clube não aparece em lado nenhum; um dono com dois clubes não sabe qual está a ver. Na página de um dono, o nome não está no título.
- **Porque importa:** orientação básica, e erros caros (cancelar uma reserva no clube errado).
- **Correção:** o nome no `h1` («Arena Futsal Boavista · Calendário», o nome do dono), com o tipo de página por cima ou por baixo em texto pequeno.

### UX-013 · As áreas do dono e do admin não têm navegação

- **Ecrã e perfil:** todas as páginas de `/manage` e `/admin`. **Viewport:** ambos. **Severidade:** major. **Esforço:** M.
- **O que se vê:** o cabeçalho tem o título da área, «Olá, …» e «Sair». Para ir ao calendário, o dono abre a página de edição do clube e carrega em «Calendário de reservas» (dois passos, e a lista de clubes não o oferece). Entre clubes ou entre calendário e estatísticas, o caminho é sempre voltar atrás.
- **Porque importa:** o calendário é a tarefa diária; a edição do clube é rara.
- **Correção (a decidir, secção 6):** na lista de clubes, ações diretas por clube («Calendário», «Estatísticas», «Definições»); um menu de navegação da área no cabeçalho (`<nav>`), com o clube atual quando há um.

### UX-014 · Sem «saltar para o conteúdo» e landmarks incompletos

- **Ecrã e perfil:** todos. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** não há ligação para saltar o cabeçalho; os cabeçalhos não usam `<nav>`. As páginas de conta (entrar, registo, recuperação) não têm cabeçalho nem marca, só «Voltar ao início».
- **Porque importa:** WCAG 2.4.1; consistência entre páginas.
- **Correção:** uma ligação «Saltar para o conteúdo» no layout raiz, visível com foco; `<nav aria-label>` à volta das ligações dos cabeçalhos; o cabeçalho público também nas páginas de conta.

### UX-015 · Os separadores de «As minhas reservas» não seguem o padrão

- **Ecrã e perfil:** «As minhas reservas», jogador. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** «Próximas» e «Anteriores» têm `role="tab"`, mas as setas não mudam de separador e os dois ficam na ordem do Tab.
- **Porque importa:** um leitor de ecrã anuncia separadores e o utilizador espera as setas.
- **Correção:** ou setas com `tabIndex` móvel, ou, mais simples e coerente com o estado no endereço, ligações normais com `aria-current="page"`, sem os papéis de separador.

### UX-016 · Caixas do calendário com nome acessível diferente do texto visível

- **Ecrã e perfil:** calendário, dono. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** cada reserva tem um `aria-label` («Bruno Oliveira, 14:00 – 15:00, Confirmada») que não contém o texto visível tal como está, e o texto visível é cortado com reticências. axe `label-content-name-mismatch`: 264 nós em 7 ecrãs.
- **Porque importa:** WCAG 2.5.3: quem usa controlo por voz diz o que vê e o comando não acerta.
- **Correção:** tirar o `aria-label` e deixar o texto visível ser o nome (com o resto em `sr-only`), ou começar o `aria-label` pelo texto visível; mostrar o texto completo num `title` ou nos detalhes em vez de o cortar.

### UX-017 · Saltos na hierarquia dos títulos

- **Ecrã e perfil:** pesquisa, «As minhas reservas». **Viewport:** ambos. **Severidade:** polish. **Esforço:** S.
- **O que se vê:** o `h1` da página passa diretamente para `h3` nos cartões (axe `heading-order`, 14 nós em 8 ecrãs).
- **Correção:** um `h2` (pode ser `sr-only`, «Resultados», «Reservas») antes da lista, ou os cartões em `h2`.

### UX-018 · O aviso de limite de pedidos não diz quanto esperar

- **Ecrã e perfil:** login (e os outros formulários com limite), visitante. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** depois de 10 tentativas, «Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.»; o botão continua ativo e cada clique repete o 429.
- **Porque importa:** a API envia `Retry-After`; o utilizador fica a adivinhar.
- **Correção:** guardar `Retry-After` no `ApiError` e mostrar «Tenta de novo dentro de N segundos», com o botão desativado até lá. Não precisa de mudança na API (a mesma origem lê o cabeçalho).

### UX-019 · Erros inesperados ao cancelar não chegam ao toast (código diferente do documentado)

- **Ecrã e perfil:** cancelar em «As minhas reservas», jogador (o mesmo padrão existe noutros componentes). **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** um 500 ou uma falha de rede ao cancelar mostra «Ocorreu um erro inesperado. Tenta novamente mais tarde.» no aviso do topo da lista (fora de vista, UX-003), não num toast. Causa: `client.ts` transforma uma falha de rede em `ApiError(0, "network_error")`, e `MyBookings` só usa o toast quando o erro **não** é um `ApiError`, por isso esse ramo nunca corre. O mesmo `if (!(error instanceof ApiError))` aparece em 16 sítios de 15 componentes; nos formulários de conta (login, registo, recuperação) o toast funciona porque também o usam para códigos desconhecidos (conferido com um 500 no login); no calendário e nos formulários dos campos não foi testado com um 500. O AGENTS.md diz que os erros inesperados de mutações vão para um toast.
- **Porque importa:** o comportamento difere do documentado, e o erro fica onde não se vê.
- **Correção:** tratar `status === 0` e `status >= 500` como inesperados (toast) num helper partilhado, ou corrigir a documentação se o aviso local for a escolha. Erro de código: listado aqui, não corrigido.

### UX-020 · Carregamentos só com texto

- **Ecrã e perfil:** «As minhas reservas», painel do admin, rotas (`RouteLoading`). **Viewport:** ambos. **Severidade:** polish. **Esforço:** M.
- **O que se vê:** «A carregar…», «A carregar as reservas…», «A carregar o gráfico…» numa página vazia; o conteúdo aparece de uma vez. O layout shift medido é baixo (CLS ≤ 0,022 em todos os ecrãs), por isso é só perceção.
- **Correção:** esqueletos com a forma dos cartões, das tabelas e dos gráficos nos sítios mais lentos (lista de reservas, gráficos, slots).

### UX-021 · O cabeçalho do jogador em mobile parte em duas linhas

- **Ecrã e perfil:** páginas públicas com sessão, jogador. **Viewport:** mobile. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** «Reservas de / Campos», «As minhas / reservas» e «Olá, Luís / Costa» quebram, e o cabeçalho passa a 73 px.
- **Correção:** em ecrãs pequenos, esconder a saudação e usar um menu (ou um ícone de conta) com «As minhas reservas» e «Sair».

### UX-022 · «As minhas reservas» é longa e repetitiva

- **Ecrã e perfil:** «As minhas reservas», jogador. **Viewport:** ambos (pior em mobile). **Severidade:** minor. **Esforço:** M.
- **O que se vê:** 20 cartões altos por página (mais de 6 000 px em mobile), cada um com a data duas vezes («sexta-feira, 30 de outubro (30/10/2026)»), Data e Hora em linhas separadas e um botão «Cancelar reserva».
- **Correção:** linhas compactas agrupadas por dia («Hoje», «Amanhã», «sábado, 31 de outubro»), hora e clube na mesma linha, sem a data numérica repetida; o prazo de cancelamento numa linha pequena.

### UX-023 · Edição do clube mistura definições e campos, com ações do mesmo peso

- **Ecrã e perfil:** edição do clube, dono. **Viewport:** ambos. **Severidade:** minor. **Esforço:** M.
- **O que se vê:** uma página única com o formulário do clube e, por baixo, os campos; cada campo tem cinco botões iguais («Horário», «Bloqueios», «Faixas de preço», «Editar», «Desativar»), e o destrutivo está ao lado de «Editar». Em mobile os botões partem em duas linhas.
- **Correção:** separar «Dados do clube» e «Campos» (secções com âncoras ou separadores); «Desativar» num menu de mais ações ou com estilo destrutivo; ações principais com ícone e texto curto em mobile.

### UX-024 · Região dos toasts com nome em inglês

- **Ecrã e perfil:** todos. **Viewport:** ambos. **Severidade:** polish. **Esforço:** S.
- **O que se vê:** o leitor de ecrã anuncia a região como «Notifications alt+T» (o nome por omissão do Sonner).
- **Correção:** `containerAriaLabel="Notificações"` no `Toaster`.

### UX-025 · Slots indisponíveis pouco legíveis e sem legenda

- **Ecrã e perfil:** página do clube, visitante e jogador. **Viewport:** ambos. **Severidade:** polish. **Esforço:** S.
- **O que se vê:** os slots ocupados são cinzento muito claro sobre cinzento; não há legenda a dizer o que significa (a regra de nunca dizer o motivo mantém-se).
- **Correção:** uma legenda curta («Livre», «Indisponível»), texto um pouco mais escuro e riscado nos indisponíveis.

### UX-026 · A escolha do campo mostra o preço base quando há faixas

- **Ecrã e perfil:** página do clube, visitante e jogador. **Viewport:** ambos. **Severidade:** polish. **Esforço:** S.
- **O que se vê:** «Futsal A · Futsal · 38,00 €» na lista de campos, enquanto metade dos slots custa 46,00 €.
- **Correção:** «desde 38,00 €» quando `hasPriceBands`, ou tirar o preço da opção (cada slot já mostra o seu).

### UX-027 · Gráficos do admin em tons de cinzento, uma série quase invisível

- **Ecrã e perfil:** painel do admin. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** no gráfico de registos, a série «Donos» usa `--chart-1` (`#d4d4d4`) sobre branco, e a legenda «Donos» quase desaparece; jogadores e donos distinguem-se só por cinzentos.
- **Porque importa:** WCAG 1.4.11 (3:1 para elementos gráficos). As tabelas `sr-only` com os mesmos números existem e estão bem.
- **Correção:** tokens `--chart-*` com contraste suficiente e cor, e rótulos de valor nas barras pequenas.

### UX-028 · Larguras diferentes entre áreas e cabeçalho desalinhado no admin

- **Ecrã e perfil:** admin (e, por comparação, as outras áreas). **Viewport:** desktop. **Severidade:** polish. **Esforço:** S.
- **O que se vê:** o cabeçalho usa `max-w-3xl` (768 px) e o conteúdo do admin `max-w-6xl` (1152 px): em 1440 px a marca começa cerca de 190 px mais à direita do que o título da página. Jogador e dono usam 768 px em tudo, mesmo onde há tabelas largas.
- **Correção:** uma largura por área (ou por página) partilhada pelo cabeçalho e pelo `PageShell`.

### UX-029 · Tabela de donos: ligações sem aspeto de ligação e cabeçalhos pequenos

- **Ecrã e perfil:** painel do admin. **Viewport:** ambos. **Severidade:** minor. **Esforço:** S.
- **O que se vê:** os nomes dos donos levam à página do dono mas não têm sublinhado nem cor; os cabeçalhos ordenáveis («Nome ↑», «Email») têm 36–52 × 20 px em mobile.
- **Correção:** estilo de ligação nos nomes (sublinhado ao passar e com foco) e cabeçalhos com área de toque maior (`py-2`, a célula inteira como botão).

### UX-030 · Identidade visual neutra e uma página inicial sem apresentação

- **Ecrã e perfil:** todos, sobretudo a página inicial. **Viewport:** ambos. **Severidade:** minor. **Esforço:** M–L.
- **O que se vê:** o tema é o neutro por omissão do shadcn (preto, branco e cinzentos), sem cor de marca, ícones ou imagem; a página inicial abre em «Encontra onde jogar» e numa lista, sem dizer o que é a plataforma, para quem é, ou que um dono também se pode registar. O verde de «Há horários livres» e das reservas confirmadas é a única cor.
- **Porque importa:** para um portefólio e para um primeiro visitante, a aplicação parece um protótipo. Não é um problema de acessibilidade.
- **Correção:** decidir uma direção visual (secção 6): cor de marca nos tokens (`--primary`, `--ring`, `--chart-*`), uma introdução curta na página inicial com a pesquisa por baixo, e um rodapé com «És dono de um clube? Cria a tua conta».

### Quadro resumo

| Severidade | Quantidade | Achados |
| --- | --- | --- |
| blocker | 0 | — |
| major | 12 | UX-001, UX-002, UX-003, UX-004, UX-005, UX-006, UX-007, UX-008, UX-009, UX-010, UX-012, UX-013 |
| minor | 12 | UX-011, UX-014, UX-015, UX-016, UX-018, UX-019, UX-021, UX-022, UX-023, UX-027, UX-029, UX-030 |
| polish | 6 | UX-017, UX-020, UX-024, UX-025, UX-026, UX-028 |
| **Total** | **30** | |

## 3. Padrões transversais

Corrigem-se uma vez, nos componentes e tokens partilhados, e resolvem vários achados.

1. **Gestão do foco** (UX-002, UX-003, UX-004): nenhum componente move o foco depois de substituir o elemento focado. Um hook partilhado e uma regra no AGENTS.md («quem substitui o elemento com foco decide para onde vai o foco») cobrem o jogador, o dono e o admin.
2. **Tokens de cor** (UX-001, UX-008, UX-027, UX-030): `--ring`, `--muted-foreground`, o texto destrutivo sobre fundos claros e `--chart-*` vêm do tema neutro por omissão e falham contraste. Mudar os tokens em `globals.css` corrige foco, contraste e gráficos de uma vez, e é também onde entra a cor de marca.
3. **Avisos longe da ação** (UX-003, UX-004, UX-019): avisos e detalhes são escritos num sítio fixo da página (topo da lista, fundo do calendário). A regra: a resposta aparece junto do que o utilizador acabou de tocar, e recebe o foco.
4. **Uma largura para tudo** (UX-005, UX-006, UX-028): `AreaHeader`, `PageShell` e o layout do jogador fixam 768 px. Uma largura por página (estreita para formulários, larga para calendário e tabelas) resolve o espaço perdido em desktop e prepara as tabelas para mobile.
5. **Tabelas e grelhas largas em mobile** (UX-005, UX-006, UX-029): contentores com scroll sem pista nem foco. Um componente `ScrollRegion` (sombra nas pontas, `tabIndex={0}`, `role="region"`, `aria-label`) serve o calendário, as estatísticas e as tabelas do admin.
6. **Títulos e identidade da página** (UX-011, UX-012, UX-017): títulos genéricos no separador e no `h1`. Um padrão para `generateMetadata` e para o `PageShell` (nome da entidade + tipo de página).
7. **Alvos e ligações** (UX-009, UX-021, UX-029): ligações só no texto, com 18–21 px de altura, em cartões e tabelas que parecem clicáveis por inteiro.

## 4. Resultados automáticos (axe)

102 capturas (54 ecrãs diferentes, cada um nas duas larguras quando aplicável); 59 sem nenhuma violação. Nenhuma página tem scroll horizontal da página inteira em 390 px, e o maior CLS medido é 0,022.

| Regra (axe) | Impacto | Nós | Ecrãs | Onde | Achado |
| --- | --- | --- | --- | --- | --- |
| `label-content-name-mismatch` | serious | 264 | 7 | Calendário (dia, semana, nova reserva, detalhes, falta, cancelar) | UX-016 |
| `color-contrast` | serious | 48 | 9 | Alertas de erro (429, recuperação, slot ocupado, motivo), «Reservado» na pré-visualização, estados das reservas anteriores, lista de clubes ao passar o rato, ajudas dos motivos | UX-008 |
| `heading-order` | moderate | 14 | 8 | Pesquisa, «As minhas reservas» | UX-017 |
| `region` | moderate | 4 | 1 | 404 global | UX-007 |
| `landmark-one-main` | moderate | 2 | 1 | 404 global | UX-007 |
| `scrollable-region-focusable` | serious | 1 | 1 | Estatísticas do clube (mobile) | UX-006 |

O axe não deteta o foco pouco visível (UX-001), o foco perdido (UX-002) nem os avisos fora de vista (UX-003, UX-004): vieram das medições e das capturas.

## 5. Ordem de trabalho proposta

Quatro lotes, cada um numa branch pequena, com verificação manual no browser no fim (a prática do repositório) e, no último, a repetição desta auditoria.

### Lote 1 · Fundações

- **Achados:** UX-001, UX-002 (o hook e a regra), UX-007, UX-008, UX-011, UX-014, UX-017, UX-019, UX-020, UX-024, UX-028.
- **Esforço:** M (2 a 3 dias). Os tokens, o 404, o skip link, os títulos e o Toaster são S cada; o hook de foco e os esqueletos são o grosso.
- **Verificar:** axe sem `color-contrast`, `heading-order`, `region` e `landmark-one-main`; foco visível em botões, campos, listas, links e slots (imagens dos elementos focados, como nesta auditoria); a 404 em pt-PT com cabeçalho; títulos distintos em cada separador; um 500 e uma falha de rede numa mutação mostram o toast; testes de componente para o hook de foco.

### Lote 2 · Fluxo do jogador

- **Achados:** UX-003, UX-009, UX-010, UX-015, UX-018, UX-021, UX-022, UX-025, UX-026 e a aplicação de UX-002 aos componentes do jogador.
- **Esforço:** M–L (3 a 4 dias). A reorganização da página do clube e a barra fixa em mobile (UX-010) e a lista compacta (UX-022) são o maior trabalho.
- **Verificar:** reservar e cancelar só com teclado, sem o foco cair no `<body>`; o aviso de cancelamento visível sem scroll, nas duas larguras; o cartão inteiro dos resultados abre o clube; em 390 px, o botão principal visível logo depois de escolher um slot; 429 com tempo de espera; os testes de componente de `ClubBooking`, `SlotList` e `MyBookings` atualizados.

### Lote 3 · Área do dono

- **Achados:** UX-004, UX-005, UX-006, UX-012, UX-013, UX-016, UX-023 e a aplicação de UX-002 aos componentes do dono.
- **Esforço:** L (4 a 6 dias), sobretudo o calendário (largura, colunas, painel de detalhes, modelo em mobile) e a navegação.
- **Verificar:** um clube com cinco campos cabe em 1440 px sem scroll; em 390 px, o modelo escolhido para mobile funciona com toque e teclado; os detalhes abrem à vista e Escape fecha e devolve o foco; axe sem `label-content-name-mismatch` e `scrollable-region-focusable`; o nome do clube no calendário e nas estatísticas; do `/manage` ao calendário de um clube num passo; o layout do calendário em dias de mudança de hora (25 e 23 h) continua certo (`layout.test.ts`).

### Lote 4 · Admin e passagem final de acessibilidade

- **Achados:** UX-027, UX-029, UX-030 (se a direção visual for decidida) e a repetição de toda a auditoria.
- **Esforço:** M (2 a 3 dias) sem UX-030; com a direção visual, mais 2 a 4 dias.
- **Verificar:** gráficos com contraste de 3:1 entre séries e fundo; ligações dos donos reconhecíveis; a auditoria repetida com os mesmos scripts (axe em todos os ecrãs, teclado, mobile) sem violações sérias; se possível, uma passagem com NVDA ou VoiceOver nos fluxos principais e num telemóvel real (o ponto do `tap()` da secção 1).

## 6. Decisões pedidas

1. **Direção visual (UX-030).** Ficar com o neutro, com só os tokens corrigidos (lote 1), ou definir uma cor de marca, um tom e uma página inicial com apresentação? Muda os tokens, o lote 4 e o aspeto de todas as capturas do README.
2. **Navegação das áreas do dono e do admin (UX-013).** Ações por clube na lista de `/manage`, um menu no cabeçalho, ou as duas? Abrir no calendário de hoje quando o dono tem um só clube?
3. **Calendário em mobile e largura (UX-005).** Um campo de cada vez com seletor (como a vista semanal) ou a grelha com scroll e pista? E em desktop: largura própria só para o calendário ou para toda a área do dono?
4. **Página do clube (UX-010).** Aceitar a nova ordem (horários primeiro, contactos ao lado ou no fim) e a barra fixa em mobile.
5. **Toast ou aviso no sítio (UX-019, UX-003).** O AGENTS.md diz que os erros inesperados de mutações vão para um toast; o código mostra-os num aviso. Proposta: resposta esperada junto da ação, inesperada no toast; se preferir o aviso, corrige-se a documentação.
6. **Mudanças na API.** Nenhuma correção proposta precisa de mudar a API: o `Retry-After` (UX-018) lê-se na mesma origem e os nomes dos clubes e dos donos (UX-011, UX-012) já vêm nas respostas que as páginas leem.

## 7. Contagem e o que já está bem

**Contagem final:** 30 achados: 0 blocker, 12 major (UX-001 a UX-010, UX-012, UX-013), 12 minor (UX-011, UX-014, UX-015, UX-016, UX-018, UX-019, UX-021, UX-022, UX-023, UX-027, UX-029, UX-030), 6 polish (UX-017, UX-020, UX-024, UX-025, UX-026, UX-028).

**O que já está bem** (não mexer sem razão):

- Texto em pt-PT consistente, direto e simpático em todos os ecrãs (exceto a 404 global e a região dos toasts); mensagens de erro claras e específicas por `code` («Este horário acabou de ser reservado. Escolhe outro.»).
- Formatação de dinheiro, datas e horas certa e coerente (`46,00 €`, «sexta-feira, 30 de outubro», 24 h em Lisboa), com o preço de cada slot e o prazo de cancelamento por extenso («Podes cancelar até 30/10/2026 08:00», «Já não é possível cancelar: o prazo terminou a …»).
- A corrida pelo mesmo slot é tratada como deve: mensagem à vista, escolha limpa, slots recarregados.
- Formulários com rótulos, erros por campo, `noValidate` e regras iguais às da API; o axe não encontrou nenhum campo sem rótulo.
- Confirmações destrutivas no sítio, com texto que explica a consequência («O horário volta a ficar livre para outros jogadores.»), e estados vazios com uma ação clara («Criar o primeiro clube»).
- Regiões vivas (`role="status"`/`alert`) nas confirmações e nos avisos, `aria-sort` nas tabelas ordenáveis, tabelas `sr-only` com os números dos gráficos, `aria-expanded` nos painéis dos campos.
- Nenhuma página com scroll horizontal em 390 px; layout shift baixo em todo o lado (CLS ≤ 0,022).
- O estado no endereço funciona: voltar, recarregar e entrar a meio de uma reserva devolvem a mesma escolha.

## 8. Decisões

Tomadas a 2026-10-30, depois da secção 6, e que valem para os lotes seguintes.

1. **Direção visual (UX-030).** Mantém-se o tema neutro e corrige-se só o que falha em contraste (nos tokens); sem cor de marca por agora. Acrescenta-se uma introdução curta na página inicial, no lote 4 (UX-030). Uma cor de marca pode entrar mais tarde sem refazer nada, e não é o que prejudica a experiência: pesa mais o foco invisível e o feedback fora do ecrã.
2. **Erros inesperados (UX-019).** Vão para um toast, como diz o AGENTS.md: corrige-se o código, não a documentação. É um erro real, feito no lote 1 (`isExpectedApiError`, `unexpectedErrorMessage`), com nota no backlog.
3. **Navegação do dono e do admin (UX-013), para o lote 3.** As duas coisas: atalhos por clube («Calendário», «Estatísticas») em `/manage`, para chegar em um clique, e um menu no cabeçalho para a navegação entre áreas.
4. **Calendário (UX-005), para o lote 3.** Em mobile, um campo de cada vez com seletor (a grelha completa só confunde em 390 px). Em desktop, página mais larga só para o calendário, não para toda a área do dono, para não mexer em formulários que ficam bem estreitos (`PAGE_WIDTH` já tem a largura `wide` para isso).
5. **Página do clube (UX-010), para o lote 2.** Os slots passam para cima dos contactos. A barra fixa de reserva em mobile só entra se for barata (esforço S ou M); senão fica de fora.

## 9. Estado dos lotes

### Lote 1 · Fundações (feito)

Medido na app a correr, com os mesmos scripts e a mesma conta de demonstração que na auditoria (desktop e mobile, teclado e axe).

| Achado | Estado | Notas |
| --- | --- | --- |
| UX-001 foco invisível | **Feito** | Uma regra em `globals.css` (anel de 2 px a 2 px de distância, `--ring` escuro) e sem `outline-none` nos controlos; testado nos tokens (3:1) e visto nos botões escuros. O botão deixou de animar o anel (`transition-all`). |
| UX-002 foco perdido | **Parcial** | Regra e ajudas feitas (`lib/focus.ts`: `useFocusOn`, `useRestoreFocus`; `ResultNotice`). Aplicadas a: confirmação de reserva e de série, recusa da reserva, perguntar e manter o cancelamento do jogador e o seu resultado, detalhes de uma reserva (abrir, formulário de cancelar, falta, resultado), formulário de estado do dono (admin), recusa do login, página de erro. **Fica para os lotes 2 e 3:** fechar os detalhes (devolver o foco à caixa da reserva), gravar o clube, os formulários dos campos (horário, bloqueios, faixas, campo), «Nova reserva». |
| UX-003 aviso fora de vista | **Parcial** | O resultado recebe o foco e entra no ecrã onde quer que a reserva estivesse. Pô-lo dentro do cartão fica para o lote 2. |
| UX-004 detalhes longe da grelha | **Parcial** | Os detalhes recebem o foco ao abrir e passam a estar à vista (com teclado, de 1 396 px para 771 px do topo do ecrã). O painel lateral e o Escape ficam para o lote 3. |
| UX-007 404 em inglês | **Feito** | `app/not-found.tsx`, `app/error.tsx` e `app/global-error.tsx` em pt-PT, com o cabeçalho do site e o caminho de volta. |
| UX-008 contraste | **Feito** | `--muted-foreground` e `--destructive` nos tokens; axe: 48 nós para 0. Um teste lê `globals.css` e confere os pares nos dois temas. |
| UX-011 títulos | **Feito** | Sufixo único (`title.template`), títulos próprios em todas as páginas e o nome do clube na página do clube. O nome do clube no calendário e nas estatísticas vem com UX-012 (lote 3). |
| UX-014 saltar para o conteúdo | **Feito** | Ligação «Saltar para o conteúdo», um `<main id>` por página, `<nav aria-label="Conta">`, cabeçalho do site nas páginas de conta. A navegação das áreas fica com UX-013 (lote 3). |
| UX-017 hierarquia dos títulos | **Feito** | `h2` nos resultados da pesquisa e na lista de reservas; axe `heading-order`: 14 nós para 0. |
| UX-019 erros inesperados | **Feito** | 500, falha de rede e erros que não são da API vão para o toast em todos os formulários e ações (a falha de rede continua dentro do formulário nos quatro ecrãs que a descrevem: login, recuperação, nova palavra-passe, reservar). Testado em «As minhas reservas», no calendário e no login, e visto na app. |
| UX-020 carregamentos | **Parcial** | `Skeleton`, `LoadingRegion` e `EmptyState`, usados em «As minhas reservas», nas rotas, na pesquisa, nos horários e nos totais, gráficos e tabelas do admin. **Fica para o lote 3:** o calendário, as estatísticas e os painéis dos campos (continuam só com texto). |
| UX-024 região dos toasts | **Feito** | «Notificações». |
| UX-028 larguras | **Feito no admin** | `PAGE_WIDTH`: o cabeçalho e as páginas do admin usam a mesma largura; o dono e o jogador já usavam a mesma. O calendário mais largo é do lote 3. |

**Resultados do axe, antes e depois** (mesmos ecrãs, as duas larguras):

| Regra | Antes (nós) | Depois (nós) |
| --- | --- | --- |
| `color-contrast` | 48 | 0 |
| `heading-order` | 14 | 0 |
| `landmark-one-main` | 2 | 0 |
| `region` | 4 | 0 |
| `label-content-name-mismatch` | 264 | 264 (UX-016, lote 3) |
| `scrollable-region-focusable` | 1 | 1 (UX-006, lote 3) |

Capturas sem nenhuma violação: 59 de 102 antes, 78 de 93 depois (corrida final; as duas séries não têm exatamente as mesmas capturas, por isso o que conta são as contagens por regra). Nenhuma violação de CSP nem erro na consola em nenhum dos fluxos.

**Encontrado durante o lote:** o `transition-all` do botão fazia o anel de foco aparecer a desvanecer (corrigido); gravar o clube deixa o foco no `<body>` (fica com UX-002, lote 3). Do lote 2 ficam também UX-009, UX-010, UX-015, UX-018, UX-021, UX-022, UX-025, UX-026.

### Lote 2 · Fluxo do jogador (feito)

Medido na app a correr, como no lote 1 (desktop e mobile, teclado e axe, as contas de demonstração), nos ecrãs do visitante e do jogador: pesquisa, clube, horários, reservar, repetição semanal, «As minhas reservas», cancelar, entrar, criar conta e recuperar a palavra-passe.

| Achado | Estado | Notas |
| --- | --- | --- |
| UX-003 aviso fora de vista | **Feito** | Uma recusa do cancelamento aparece na linha da reserva a que diz respeito, com foco; o sucesso aparece por cima da lista, na mesma região, com foco (a reserva já saiu da lista). |
| UX-009 resultados | **Feito** | A ligação é o nome do clube e o seu `::after` cobre o cartão; o anel de foco desenha-se à volta do cartão; cartão com `hover`. Os botões do paginador têm 44 px em mobile. |
| UX-010 página do clube | **Feito** | A ordem é a ligação de voltar, o nome e a morada, os horários, os campos e os contactos. Uma linha diz como reservar. **Barra de reserva fixa em mobile: feita**, esforço **M** (meio dia a um dia, só `position: sticky`, sem dependências, sem tocar na API): os botões são um irmão do cartão do resumo, não um filho, e colam-se ao fundo do ecrã enquanto a secção está à vista, com o que reservam por cima; de `sm` para cima são a linha de botões por baixo do resumo. Medido em 390 px: depois de escolher um horário a barra está nos últimos 97 px do ecrã. A coluna lateral de contactos em desktop **não** foi feita (a coluna tem 768 px). |
| UX-015 separadores | **Feito** | Duas ligações numa `<nav>` com `aria-current`; um clique normal troca pelo endereço, um clique com Ctrl ou Cmd abre num separador novo. |
| UX-018 tempo de espera | **Feito** | `Retry-After` no `ApiError`; os quatro formulários com limite contam os segundos (um `timer`, não anunciado a cada segundo), desativam o botão e dizem quando se pode voltar a tentar. Visto na app: «60 segundos», botão desativado, foco na mensagem. |
| UX-021 cabeçalho em mobile | **Feito** | A marca numa linha e um botão «Conta» que abre o mesmo conteúdo (saudação, «As minhas reservas» só para jogadores, «Sair»); teclado: Enter abre, Tab percorre, Escape fecha e devolve o foco, sair com Tab fecha. O cabeçalho passou de duas linhas (73 px) a uma (69 px, o botão tem 44 px). O visitante mantém «Entrar» e «Criar conta» (cabem). |
| UX-022 lista de reservas | **Feito** | Uma lista por dia, com o dia como título («Hoje · …», «Amanhã · …», o ano só se não for este) e uma linha compacta por reserva. Com 20 reservas, a página passou de cerca de 6 900 px a cerca de 4 600 px em mobile, e de cerca de 4 800 px a 3 500 px em desktop. Em mobile cada linha continua a ocupar cerca de 215 px porque o estado, o botão e o prazo ficam em linhas próprias; uma versão mais densa fica por decidir. |
| UX-025 horários indisponíveis | **Feito** | Riscados, sem a transparência, e uma legenda («Livre», «Indisponível (riscado)»), que não diz porquê. |
| UX-026 preço na escolha do campo | **Feito** | Um campo com faixas já não mostra um preço na escolha (cada horário mostra o seu). |
| UX-002 (ecrãs do jogador) | **Feito** | Reservar, a recusa de uma reserva, a série, perguntar e manter o cancelamento, o resultado, as páginas de conta e a recusa do login (batch 1) deixam o foco no sítio certo; nenhum fluxo do jogador larga o foco no `<body>`. |
| UX-011 / UX-020 (ecrãs do jogador) | **Feito** | Todos os títulos do jogador têm nome próprio (o clube leva o seu nome). O esqueleto dos horários tem a forma da lista (as duas linhas por cima e cerca de um dia de horários, na mesma grelha), porque com os horários primeiro a página abaixo saltava (deslocamento 0,14). |

Também feito: uma introdução na página inicial (duas frases, e a ligação para criar uma conta de dono), a confirmação a dizer o que acontece a seguir (o email e onde cancelar), botões de ação principais com 44 px em mobile (`Button size="cta"`), e a revisão do texto: «por horário» em vez de «por slot» onde a página já diz «horário», e «tentar novamente» em todas as mensagens de repetição.

**Axe nos ecrãs do visitante e do jogador** (as duas larguras): 31 de 52 capturas sem violações na auditoria, 51 de 51 no lote 1 e 51 de 51 agora. As regras da auditoria, em nós: `color-contrast` 17 para 0, `heading-order` 14 para 0, `landmark-one-main` 2 para 0, `region` 4 para 0. O axe não vê quase nada do que mudou neste lote (a ordem da página, o tamanho dos alvos, o menu, a lista por dia): isso foi conferido com as medições e as capturas acima e com os testes. Nenhum erro na consola nem violação de CSP; o deslocamento de layout máximo é 0,037 (só mudanças pedidas pelo utilizador).

**Teclado, de ponta a ponta no desktop:** Tab até ao primeiro horário passou de 13 para 11 paragens; depois de reservar o foco está no título da confirmação, com o anel visível; os dois botões de reservar são alcançados na ordem do ecrã.

**Fica para outros lotes**
- Lote 3 (dono): `slot` e «por slot» nos ecrãs do dono (UX-023 e o texto de `PitchSection`), e tudo o que o relatório lhe atribui.
- Lote 4: UX-027, UX-029, UX-030 (cor de marca e restante identidade).
- Mais densidade em mobile na lista de reservas (cerca de 215 px por reserva); a coluna lateral de contactos em desktop; o foco ao fechar os detalhes do calendário (UX-002, dono).
- Os separadores de `/manage` e `/admin` continuam sem menu (UX-013, lote 3).
