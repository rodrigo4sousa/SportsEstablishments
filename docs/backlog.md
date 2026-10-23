# Backlog — App de Reservas de Campos
 
Oct 22, 2026 · @Rodri
 
O MVP soma 92 pontos (19 histórias de utilizador e 6 técnicas); o que fica para depois soma 37. Uma só app React com três áreas: `/` (jogador), `/manage` (dono) e `/admin` (admin), sobre uma API ASP.NET Core.
 
## Convenções e regras de negócio
 
**Formato.** Cada história segue «Como \<perfil>, quero \<ação>, para \<benefício>», com critérios de aceitação testáveis. **M** = MVP, **D** = depois do MVP.
 
**Story points (Fibonacci: 1, 2, 3, 5, 8).** Medem esforço e incerteza relativos, não horas. A referência é a US02 (registo de dono) = 2 pontos. Depois do primeiro sprint, a tua velocidade (pontos feitos por sprint) é que converte pontos em tempo. Histórias de 8 pontos são candidatas a dividir.
 
**Perfis.** Jogador, Dono e Admin. O admin é o dono da plataforma: não tem registo público e vê só contagens, nunca dados pessoais de jogadores.
 
**Regras decididas**
 
- Um email corresponde a uma só conta e a um só perfil.
- A plataforma é um único marketplace: uma conta de jogador serve em todos os clubes de todos os donos. Não há sites nem domínios por dono.
- Um dono pode ter vários clubes; cada clube pertence a um só dono.
- Janela de cancelamento definida por clube, 24 horas por omissão. Conta a que estiver em vigor no momento do cancelamento.
- Reservas de hoje até 30 dias à frente.
- Duração do slot por campo: 30, 60 ou 90 minutos.
- Reservas e cancelamentos nunca se apagam: mudam de estado (Confirmada, Cancelada pelo jogador, Cancelada pelo clube, Falta).
- Cada reserva guarda o preço do momento em que foi feita.
- Pagamentos ficam fora do MVP.
- Datas guardadas em UTC e mostradas em hora de Lisboa.
## Épico 1 — Contas e acesso

**Requisito transversal às histórias de autenticação (US01, US03, US04).** Todas as sessões, de qualquer perfil, são renovadas pelo `POST /api/auth/refresh`, que verifica sempre que a conta continua ativa antes de emitir novos tokens. Uma conta desativada não recebe novos tokens, e o pedido de refresh é tratado como falhado (401). Há um teste de integração para este caso.
 
### US01 · Registo de jogador · M · 3 pontos
 
Como jogador, quero criar conta com email e palavra-passe, para poder reservar campos e gerir as minhas reservas.
 
- O registo pede nome, email, palavra-passe e telemóvel (opcional).
- O email tem de ser único; se já existir, aparece uma mensagem a dizê-lo.
- A palavra-passe tem pelo menos 8 caracteres e é guardada com hash, nunca em texto.
- Campos inválidos são rejeitados com mensagem por campo, no front end e na API.
- Depois do registo, o jogador fica com sessão iniciada e vai para a página inicial.
- A conta criada tem o perfil Jogador.
- Decidido: pedidos POST, PUT, PATCH e DELETE sem cabeçalho `Origin`, ou com um `Origin` de outro site, são rejeitados com 403 `origin_not_allowed`.
- Decisões de implementação (provisórias, a confirmar):
  - **Email já usado (compromisso aceite):** o critério pede uma mensagem a dizê-lo, por isso o registo revela que um email tem conta. A API responde 409 com o código `email_taken` e sempre a mesma mensagem («Já existe uma conta com este email.»), seja a conta de jogador, de dono ou de admin, sem mais nenhum dado. A comparação ignora maiúsculas e minúsculas e os espaços à volta; o índice único da base de dados decide registos simultâneos. A alternativa sem enumeração (responder sempre «verifica o teu email» e avisar por email) contraria este critério e o de sessão iniciada logo a seguir. O login (US03) e a recuperação (US05) continuam sem revelar se o email existe. O rate limiting das rotas `/api/auth/*` fica para a US03 (ver lá).
  - Nome obrigatório, até 100 caracteres; os espaços à volta são removidos.
  - Email: um endereço simples (sem nome de apresentação), com ponto no domínio e até 256 caracteres; os espaços à volta são removidos; é guardado como foi escrito.
  - Telemóvel opcional: 9 a 15 algarismos, com `+` inicial opcional; os espaços são removidos. Aceita números estrangeiros.
  - Palavra-passe: só o mínimo de 8 caracteres, sem máximo nem regras de composição; é usada tal como foi escrita (sem cortar espaços).
  - Sessão: a validade de 7 dias do refresh token é absoluta (a rotação mantém a data de expiração), por isso é preciso entrar de novo, no máximo, 7 dias depois do registo ou do login. Um refresh token já usado que volte a aparecer termina todas as sessões da conta (docs/stack.md); dois separadores a renovar ao mesmo tempo podem por isso terminar a sessão.
  - Conta desativada (requisito do Épico 1): `users.deactivated_at` preenchido. A US29 acrescenta o motivo e o registo de quem desativou.
  - Pedidos anónimos a rotas que não existem recebem 401 (política de fallback que exige sessão), e não 404.
  - A conta de admin criada pelo seed tem o nome «Administrador».
  - A página inicial mostra «Olá, \<nome>» no cabeçalho depois do registo, ou a ligação «Criar conta» sem sessão. A ligação para o login aparece com a US03.
### US02 · Registo de dono · M · 2 pontos
 
Como dono de um clube, quero criar a minha conta de dono, para registar e gerir o meu clube na plataforma.
 
- Existe uma página de registo própria para donos, separada da do jogador.
- Pede os mesmos dados e aplica as mesmas validações da US01.
- Um email já usado noutra conta (de qualquer perfil) é rejeitado.
- A conta criada tem o perfil Dono.
- Depois do registo, o dono entra em `/manage` e é convidado a criar o primeiro clube.
- Estado: feita. A página é `/register/owner` e o endpoint é `POST /api/auth/register/owner`: o perfil Dono é fixo no endpoint e nunca vem do pedido, por isso nenhum pedido cria um admin. Mesmo corpo, mesmas validações, o mesmo 409 `email_taken` para qualquer perfil e os mesmos cookies que o registo de jogador. O registo de jogador e o de dono partilham o mesmo limite de pedidos por IP.
- Provisório: o botão «Criar o primeiro clube» de `/manage` é um marcador desativado até existir a US06 (por agora todos os donos veem o estado vazio, porque ainda não há lista de clubes).
- Decidido: um utilizador com sessão que abra `/login` ou `/register` (incluindo a página de registo de donos) é reencaminhado para a página inicial do seu perfil, com uma verificação no servidor.
### US03 · Login e logout · M · 5 pontos
 
Como utilizador registado, quero entrar e sair da minha conta, para aceder à área do meu perfil.
 
- Login com email e palavra-passe; credenciais erradas mostram uma mensagem genérica, sem revelar se o email existe.
- Depois do login, o utilizador vai para a área do seu perfil: `/` (jogador), `/manage` (dono) ou `/admin` (admin).
- O admin criado pelo seed (US04) entra por este login e vai para `/admin` (critério que passou da US04 para aqui, com teste).
- Notas da US01 para esta história: o `POST /api/auth/refresh`, a rotação e a recusa de contas desativadas já existem.
- Rate limiting e bloqueio temporário de conta nas rotas `/api/auth/*`. Isto também trava a enumeração de emails pelo registo (US01).
- Só o Caddy é de confiança para o `X-Forwarded-For` (`KnownProxies` / `ForwardedHeaders`), para o IP usado no rate limiting não poder ser forjado.
- A sessão usa um access token JWT de curta duração (15 minutos) e um refresh token. Um pedido com access token expirado ou inválido devolve 401; o front end chama então `POST /api/auth/refresh` uma vez e repete o pedido. Só se o refresh falhar é que o utilizador é enviado para o login.
- O refresh verifica que a conta continua ativa; uma conta desativada não recebe novos tokens (ver requisito transversal do Épico 1 e US29).
- Quem tenta abrir a área de outro perfil é redirecionado no front end, e a API devolve 403.
- O logout termina a sessão no browser: recebe o cookie `refresh_token` (que vai para todo o `/api/auth`), revoga esse token na base de dados e apaga os dois cookies com os mesmos caminhos com que foram criados.
- Uma conta de dono desativada (US29) não consegue entrar.
- Estado: back end e front end feitos (página `/login`, redirecionamento por perfil, guardas das áreas, botão «Sair», refresh entre separadores).
- Decidido (front end): o servidor nunca renova a sessão (o cookie `refresh_token` só vai para `/api/auth`). Os guardas vivem nos layouts de `/manage` e `/admin`; sem sessão → `/login?next=<caminho>`; perfil errado → a área desse utilizador. A página de login tenta primeiro um refresh silencioso no browser e, com sucesso, segue para `next`. `next` só é aceite se for um caminho relativo que começa por uma única `/` e pertence à área do utilizador; senão vai para a página inicial do perfil (jogador `/`, dono `/manage`, admin `/admin`). Só um separador de cada vez renova a sessão (Web Locks API).
- Decisões de implementação (provisórias, a confirmar):
  - **Falha genérica:** email desconhecido, palavra-passe errada, conta bloqueada e conta desativada dão todos o mesmo 401 `invalid_credentials` («Email ou palavra-passe incorretos.»), depois do mesmo trabalho de hash, para o tempo de resposta também não revelar se o email existe. Um email mal formado também dá este 401; só os campos vazios dão 400 `validation_failed`, por campo.
  - O email é comparado sem maiúsculas/minúsculas e sem os espaços à volta; a palavra-passe é usada tal como foi escrita.
  - **Bloqueio:** 5 palavras-passe erradas seguidas bloqueiam a conta durante 15 minutos (bloqueio do Identity). Tentativas durante o bloqueio não o prolongam. Um login certo recomeça a contagem. Tentativas erradas em paralelo na mesma conta podem não ser todas contadas (controlo de concorrência do Identity); o rate limiting cobre esse caso.
  - **Rate limiting:** janela fixa de 1 minuto, por IP do cliente e por rota: login 10, registo 5, refresh 30 pedidos (o refresh tem mais margem porque vários utilizadores podem partilhar um IP). Um cliente IPv6 conta por rede /64. Valores configuráveis em `RateLimiting:Auth`. Acima do limite: 429 `too_many_requests` com `Retry-After`.
  - **Proxies de confiança:** por omissão nenhum; o IP é o da ligação. Em produção só o Caddy, com um endereço fixo (`ForwardedHeaders:KnownProxies`), e não a rede inteira. O contentor do Next.js nunca é de confiança: o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser. Em desenvolvimento todos os pedidos partilham um limite (o do contentor do Next.js), o que chega para um programador.
  - **Páginas públicas (front end):** `/` e as páginas de pesquisa e de clube ficam públicas para todos os perfis; os guardas só existem em `/manage` e `/admin`. Um utilizador com sessão que abra `/login` só é reencaminhado se o refresh silencioso funcionar; o redireccionamento no servidor de `/login` e `/register` para a página inicial do perfil fica para a US02 (ver lá).
  - **Caminho de regresso:** o guarda de um layout não conhece o caminho completo do pedido, por isso `next` é a página inicial da área (`/manage` ou `/admin`), não a subpágina onde o utilizador estava. Decidido: resolve-se quando existir a primeira subpágina (US06, páginas de clube em `/manage`), com um `proxy.ts` pequeno que só encaminha o caminho do pedido num cabeçalho, para o layout o ler. O `proxy.ts` continua a ser uma camada de conveniência: não renova tokens nem decide acessos.
  - **`next` e a área do utilizador:** para o jogador, a sua área são as páginas públicas (tudo exceto `/manage`, `/admin`, `/login` e `/register`); um dono só aceita caminhos de `/manage` e um admin só de `/admin`.
  - **Logout:** responde sempre 204 e apaga os dois cookies, com ou sem sessão válida. Um refresh token já usado que apareça no logout termina todas as sessões da conta, como no refresh.
  - Os refreshes vêm sempre do browser (o cookie `refresh_token` só vai para `/api/auth`, por isso o servidor Next.js nunca renova a sessão); o limite do refresh conta, por isso, o IP real do utilizador.
### US04 · Conta de admin criada por seed · M · 2 pontos
 
Como dono da plataforma, quero que a conta de admin seja criada na instalação, para não haver forma pública de alguém se tornar admin.
 
- A conta de admin é criada no arranque a partir de variáveis de ambiente (email e palavra-passe), se ainda não existir.
- Não existe nenhum endpoint nem página que crie contas de admin ou promova outras contas a admin.
- Sem as variáveis definidas, a aplicação arranca sem admin e regista um aviso no log.
- Só uma das variáveis definida, email inválido ou palavra-passe com menos de 8 caracteres: a API não arranca e mostra um erro claro, que nunca inclui o email nem a palavra-passe.
- Se já existe um admin, não é criado outro, mesmo com outro email, e o admin existente nunca é alterado (mudar a variável da palavra-passe depois não tem efeito).
- Se o email já pertence a uma conta de jogador ou de dono, essa conta nunca é promovida a admin; fica um aviso no log, sem o email.
- O seed corre em todos os ambientes, sempre depois das migrações. Em produção as migrações são aplicadas no deploy (T06), antes de a API arrancar.
- Mudar a palavra-passe do admin é um procedimento manual, descrito no README: apagar a conta de admin e arrancar a API com as novas variáveis.
- O critério «o admin entra pelo login normal e vai para `/admin`» passou para a US03.
- Decisões de implementação:
  - Variáveis `AdminSeed__Email` e `AdminSeed__Password` (no docker compose, `ADMIN_SEED_EMAIL` e `ADMIN_SEED_PASSWORD` no `.env`). A palavra-passe segue as regras da US01.
  - Os espaços à volta do email são removidos antes de o validar; o resto da validação é estrita (a palavra-passe é usada tal como está).
  - As palavras-passe de todas as contas são guardadas com PBKDF2-HMAC-SHA512 e 210 000 iterações (formato v3 do Identity); hashes antigos com menos iterações são atualizados no login seguinte.
### US05 · Recuperar palavra-passe · D · 3 pontos
 
Como utilizador, quero redefinir a palavra-passe por email, para recuperar o acesso se a esquecer.
 
- O pedido de recuperação mostra sempre a mesma mensagem, exista ou não o email.
- O email traz uma ligação de uso único que expira ao fim de 1 hora.
- Uma ligação usada ou expirada mostra erro e permite pedir outra.
- A nova palavra-passe segue as regras da US01.
## Épico 2 — Configuração do clube
 
### US06 · Criar e editar clube · M · 3 pontos
 
Como dono, quero criar e editar os meus clubes, para que os jogadores os encontrem com a informação certa.
 
- Um clube tem nome, morada, localidade, telefone, email de contacto e janela de cancelamento em horas (24 por omissão).
- Um dono pode ter vários clubes; cada clube pertence a um só dono.
- O dono só vê e edita os seus clubes; um pedido a um clube de outro dono devolve 404.
- Campos obrigatórios em falta são rejeitados com mensagem por campo.
- Alterar a janela de cancelamento aplica-se aos cancelamentos feitos a partir desse momento.
- Decidido: a janela de cancelamento lê-se do clube no momento do cancelamento e nunca é copiada para as reservas. A US19 (e a US12) seguem esta regra.
- Decidido (URLs): ao criar o clube gera-se um `slug` único a partir do nome (por exemplo `clube-desportivo-do-porto`); o `slug` não muda se o nome mudar, para os endereços públicos continuarem a funcionar. Provisório: numa colisão acrescenta-se um sufixo numérico. A área do dono usa o GUID público do clube: `/manage/clubs/{id}`.
- O clube guarda o seu fuso horário (`Europe/Lisbon`). Quando a tabela de clubes existir, repete-se o teste de armazenamento real da T04 com ela: instantes em UTC e fuso do clube guardado.
- Estado: back end feito (`/api/manage/clubs`: criar, listar, ver e editar, com o perfil Dono e a T02); falta o front end.
- Decisões de implementação (provisórias, a confirmar):
  - Os cinco campos de texto são obrigatórios: nome (até 100 caracteres), morada (até 200), localidade (até 100), telefone e email de contacto (as mesmas regras da US01: 9 a 15 algarismos com `+` opcional; um endereço simples com ponto no domínio). Os espaços à volta são removidos, e os do telefone também.
  - Janela de cancelamento: um número inteiro de horas, de 1 a 168 (uma semana). Ao criar é opcional (24 por omissão); ao editar (`PUT`, que substitui tudo) é obrigatória. A base de dados também a limita (check constraint).
  - `slug`: letras minúsculas ASCII e algarismos, sem acentos, e tudo o resto passa a um hífen; por exemplo «Clube de Ténis da Foz» dá `clube-de-tenis-da-foz` e «Padel & Co.» dá `padel-co` (o `&` não é traduzido). Um nome sem nada aproveitável dá `clube`. A base é cortada em 80 caracteres, num fim de palavra. Numa colisão acrescenta-se `-2`, `-3`, … (o número a seguir ao maior em uso). O índice único decide criações simultâneas: a que perde tenta de novo com o sufixo seguinte.
  - Lista do dono: 20 por página por omissão, no máximo 100, ordenada pelo nome sem distinguir maiúsculas e depois pelo id.
  - Um clube nunca muda de dono, e apagar a conta de um dono com clubes falha em vez de apagar os clubes.
  - O fuso horário não se edita nem aparece no formulário; a resposta mostra-o (`timeZone`).
### US07 · Gerir campos · M · 5 pontos
 
Como dono, quero adicionar, editar e desativar campos, para controlar o que está disponível para reserva.
 
- Um campo tem nome, desporto (de uma lista fixa: futsal, futebol 7, padel, ténis), preço por slot em euros e estado (ativo ou inativo).
- Mudar o preço só afeta reservas novas; as existentes mantêm o preço do momento da reserva.
- Um campo inativo não aparece na pesquisa nem aceita reservas.
- Não é possível desativar um campo com reservas futuras confirmadas; a mensagem indica quantas são.
- Os campos não se apagam, só se desativam, para manter o histórico.
### US08 · Horário e duração dos slots · M · 5 pontos
 
Como dono, quero definir o horário de funcionamento e a duração dos slots de cada campo, para que os slots livres sejam gerados automaticamente.
 
- Para cada dia da semana, o dono define hora de abertura e de fecho, ou marca o dia como fechado.
- A duração do slot é de 30, 60 ou 90 minutos, por campo.
- Os slots são gerados a partir da abertura, em blocos da duração definida; um bloco que passe da hora de fecho não é gerado.
- Mudar o horário não altera reservas já feitas; uma reserva fora do novo horário continua válida.
- As horas são introduzidas e mostradas em hora de Lisboa (T04).
- O fecho mais tarde é às 00:00 (meia-noite do próprio dia). **Limitação conhecida do MVP:** horários que passam da meia-noite (por exemplo, 22:00–02:00) são rejeitados.
- Quando a tabela de horários existir, repete-se o teste de armazenamento real da T04 com ela: horários guardados como hora local (`time`) com o fuso do clube, e os slots gerados em UTC.
### US09 · Bloquear slots · M · 3 pontos
 
Como dono, quero bloquear períodos de um campo, para que ninguém o reserve durante manutenção ou eventos privados.
 
- Um bloqueio tem campo, início, fim e motivo (opcional).
- Os slots dentro de um bloqueio não aparecem como livres para os jogadores.
- Um bloqueio que se sobreponha a reservas confirmadas é rejeitado, e a mensagem lista essas reservas para o dono decidir (cancelar pela US12).
- O dono pode remover um bloqueio futuro, e os slots voltam a ficar livres.
- Os bloqueios aparecem no calendário (US11).
### US10 · Preços de ponta e vazio · D · 5 pontos
 
Como dono, quero definir preços diferentes por faixa horária, para cobrar mais nas horas de maior procura.
 
- O dono define faixas por campo (dias da semana e intervalo de horas), cada uma com o seu preço.
- Um slot fora de qualquer faixa usa o preço base do campo.
- Faixas sobrepostas no mesmo campo são rejeitadas.
- O preço mostrado ao jogador e guardado na reserva é o da faixa do slot.
## Épico 3 — Operação do clube
 
### US11 · Calendário de reservas · M · 8 pontos
 
Como dono, quero ver as reservas de um clube numa vista diária e semanal, para saber o que se passa em cada campo.
 
- Vista diária: uma coluna por campo, linhas por hora, com reservas e bloqueios.
- Vista semanal: um campo de cada vez, escolhido numa lista.
- Cada reserva mostra o nome do jogador (ou do cliente, na US13), a hora e o estado.
- Navegação para o dia ou semana anterior e seguinte, e um botão «Hoje».
- As reservas canceladas não aparecem por omissão.
- Abrir uma reserva mostra os detalhes e a ação de cancelar.
- Candidata a dividir: vista diária (5) e vista semanal (3).
### US12 · Cancelar reserva como dono · M · 2 pontos
 
Como dono, quero cancelar uma reserva, para resolver problemas como um campo avariado.
 
- O dono pode cancelar qualquer reserva futura dos seus clubes, sem o limite da janela de cancelamento.
- É obrigatório indicar um motivo.
- A reserva passa a «Cancelada pelo clube» (não é apagada) e o slot volta a ficar livre.
- O jogador recebe um email com o motivo (US22).
### US13 · Reserva em nome de um cliente · M · 3 pontos
 
Como dono, quero criar uma reserva para um cliente que ligou ou apareceu, para que todas as reservas fiquem no sistema.
 
- O dono escolhe campo e slot livre e indica nome e telemóvel do cliente; o cliente não precisa de conta.
- Aplica-se a mesma regra contra reservas duplicadas da US18 e da T03.
- A reserva aparece no calendário marcada como «criada pelo clube».
- Não é enviado email, porque o cliente não tem conta.
### US14 · Ocupação e receita · D · 5 pontos
 
Como dono, quero ver a ocupação e a receita dos meus clubes por período, para perceber como está o negócio.
 
- Filtros por clube, campo e intervalo de datas.
- Ocupação = horas reservadas a dividir pelas horas abertas no período, por campo.
- Receita = soma dos preços das reservas não canceladas.
- Os números batem certo com o calendário para o mesmo período (verificado por teste).
### US15 · Marcar falta · D · 2 pontos
 
Como dono, quero marcar uma reserva passada como falta, para saber que jogadores não aparecem.
 
- Só se podem marcar reservas confirmadas cuja hora de fim já passou.
- O estado passa a «Falta» e a marcação pode ser revertida.
- O dono vê quantas faltas um jogador tem nos seus clubes.
## Épico 4 — Pesquisa e reserva
 
### US16 · Pesquisar clubes · M · 5 pontos
 
Como jogador, quero pesquisar clubes por desporto, localidade e data, para encontrar onde jogar.
 
- Filtros: desporto, localidade e data (hoje por omissão).
- Cada resultado mostra nome, localidade, desportos e se há slots livres na data escolhida.
- Clubes sem campos ativos, ou de donos desativados, não aparecem.
- Uma pesquisa sem resultados mostra uma mensagem clara.
- Resultados paginados, 20 por página.
- Abrir um resultado mostra a página do clube, com contactos e campos. O endereço público da página do clube é `/clubs/{slug}` (o `slug` é gerado na US06).
- A pesquisa funciona sem sessão iniciada.
### US17 · Ver slots livres · M · 5 pontos
 
Como jogador, quero ver os slots livres de um campo numa data, para escolher a hora.
 
- Mostra os slots do dia com hora de início, hora de fim e preço.
- Um slot não aparece como livre se tiver reserva confirmada, estiver bloqueado, estiver fora do horário ou já tiver começado.
- Só se podem escolher datas de hoje até 30 dias à frente.
- As horas aparecem em hora de Lisboa e estão certas nos dias de mudança de hora (T04).
- No dia em que a hora atrasa (último domingo de outubro), as horas entre a 01:00 e as 02:00 aparecem duas vezes e levam a indicação «(verão)» ou «(inverno)»; nenhum slot é escondido.
### US18 · Reservar slot · M · 3 pontos
 
Como jogador, quero reservar um slot livre, para garantir o campo.
 
- Só jogadores com sessão iniciada podem reservar; sem sessão, é pedido o login e o jogador volta ao mesmo slot.
- Antes de confirmar, aparece um resumo com clube, campo, data, hora e preço.
- A reserva fica «Confirmada» e guarda o preço desse momento.
- Se o slot foi ocupado entretanto, a API devolve 409 e o jogador vê «Este horário acabou de ser reservado. Escolhe outro.», com a lista de slots atualizada.
- Não é possível reservar slots passados nem para lá dos 30 dias.
### US19 · Ver e cancelar as minhas reservas · M · 3 pontos
 
Como jogador, quero ver as minhas reservas e cancelar as futuras, para gerir os meus jogos.
 
- A lista separa «Próximas» e «Anteriores», ordenadas por data.
- Cada reserva mostra clube, campo, data, hora, preço e estado.
- O jogador pode cancelar uma reserva futura até à janela de cancelamento do clube; depois disso, o botão aparece desativado com a explicação.
- A janela é a que o clube tem no momento do cancelamento (lida de `Club.CancellationWindowHours`), nunca uma cópia guardada na reserva (decidido na US06).
- A API recusa cancelamentos fora da janela, mesmo que o pedido não venha do front end.
- A reserva cancelada passa a «Cancelada pelo jogador» e o slot volta a ficar livre.
### US20 · Reserva semanal recorrente · D · 8 pontos
 
Como jogador, quero reservar o mesmo slot todas as semanas durante algum tempo, para não ter de reservar à mão.
 
- O jogador escolhe o slot e o número de semanas, até 4.
- Antes de confirmar, o sistema mostra quais das datas estão livres.
- As datas livres são reservadas numa só operação; as ocupadas são indicadas e ficam de fora.
- Cada ocorrência é uma reserva independente, que se pode cancelar sozinha.
- Candidata a dividir.
### US21 · Lembrete antes da reserva · D · 3 pontos
 
Como jogador, quero receber um lembrete antes da reserva, para não me esquecer.
 
- É enviado um email 3 horas antes do início.
- Reservas canceladas, ou feitas a menos de 3 horas do início, não geram lembrete.
- Cada reserva gera no máximo um lembrete, mesmo que o serviço reinicie.
## Épico 5 — Notificações
 
### US22 · Emails ao jogador · M · 3 pontos
 
Como jogador, quero receber um email quando reservo e quando uma reserva minha é cancelada, para ter confirmação.
 
- O email de confirmação traz clube, morada, campo, data, hora e preço.
- O email de cancelamento indica quem cancelou e, se foi o clube, o motivo.
- O envio é feito em segundo plano; uma falha no envio não falha a reserva nem o cancelamento e fica registada no log.
- Em desenvolvimento, os emails vão para um serviço de teste (por exemplo, Mailpit) e nunca para endereços reais.
### US23 · Email ao dono sobre nova reserva · D · 1 ponto
 
Como dono, quero receber um email quando entra uma reserva nova num dos meus clubes, para acompanhar sem abrir a app.
 
- O email traz clube, campo, data, hora e nome do jogador.
- O dono pode desligar estes emails nas definições do clube.
- As reservas criadas pelo próprio dono (US13) não geram email.
## Épico 6 — Administração
 
Todas as páginas e endpoints deste épico só respondem ao perfil Admin (403 para os outros) e mostram contagens, nunca nomes ou emails de jogadores.
 
### US24 · Indicadores globais · M · 3 pontos
 
Como admin, quero ver os totais da plataforma, para saber o tamanho dela.
 
- Mostra o número de donos, clubes, campos ativos, jogadores e reservas confirmadas.
- Contas e clubes desativados aparecem separados dos ativos.
- Os valores vêm de queries agregadas na base de dados, sem carregar listas para memória.
### US25 · Reservas por período · M · 3 pontos
 
Como admin, quero ver as reservas ao longo do tempo num gráfico, para perceber se a plataforma está a crescer.
 
- Gráfico de reservas por dia, semana ou mês, à escolha.
- Intervalo de datas configurável, por omissão os últimos 30 dias.
- Períodos sem reservas aparecem com zero, em vez de desaparecerem do gráfico.
- As reservas canceladas ficam de fora por omissão, com opção de as incluir.
### US26 · Lista de donos · M · 3 pontos
 
Como admin, quero ver a lista de donos com os números de cada um, para saber quem usa a plataforma e quanto.
 
- Cada linha mostra nome do dono, email, data de registo, número de clubes, de campos, de reservas e de jogadores distintos que reservaram nos seus clubes.
- Ordenável por qualquer coluna, com pesquisa por nome ou email do dono.
- Paginada.
### US27 · Clubes com maior ocupação · D · 3 pontos
 
Como admin, quero ver os clubes com mais ocupação num período, para saber quais funcionam melhor.
 
- Top 10 por taxa de ocupação no período escolhido, com a mesma fórmula da US14.
- Mostra nome do clube, dono, localidade e taxa.
### US28 · Novos registos ao longo do tempo · D · 2 pontos
 
Como admin, quero ver os novos donos e jogadores ao longo do tempo, para acompanhar a adesão.
 
- Gráfico de registos por semana ou mês, separado por perfil.
- Usa o mesmo filtro de intervalo da US25.
### US29 · Desativar e reativar dono · D · 5 pontos
 
Como admin, quero desativar um dono, em caso de abuso ou a pedido dele, para que deixe de usar a plataforma.
 
- É obrigatório indicar o motivo.
- O dono desativado não consegue entrar, e os seus clubes deixam de aparecer na pesquisa e de aceitar reservas.
- A desativação revoga os refresh tokens do dono, e o `POST /api/auth/refresh` recusa contas desativadas, por isso não é emitido nenhum token novo.
- **Compromisso aceite (JWT sem estado, validade curta):** um access token já emitido continua válido até expirar, no máximo 15 minutos depois da desativação, porque a API valida o JWT sem consultar a base de dados. Durante esse período o dono ainda consegue fazer pedidos; depois disso, a sessão termina.
- As reservas futuras não são canceladas automaticamente; o admin vê quantas há antes de confirmar.
- O admin pode reativar a conta.
- Cada desativação e reativação fica registada (quem, quando e motivo), e esse registo não pode ser editado.
## Histórias técnicas
 
Não são visíveis para o utilizador, mas sem elas as estimativas das outras histórias ficam erradas. Todas são MVP.
 
### T01 · Estrutura, Docker e CI · 5 pontos
 
Como programador, quero o projeto a correr com um comando e testado em cada push, para desenvolver sem configuração manual.
 
- Repositório com a API ASP.NET Core, a app React em TypeScript e os projetos de testes.
- `docker compose up` arranca a API, a app, o PostgreSQL e o serviço de email de teste.
- As migrações EF Core são aplicadas automaticamente em desenvolvimento.
- GitHub Actions faz build e corre os testes (back end e front end) em cada push e pull request; uma falha bloqueia o merge.
- Documentação OpenAPI (Swagger) disponível em desenvolvimento.
- O README explica como correr o projeto localmente.
### T02 · Autorização por perfil e por dono · 3 pontos
 
Como dono da plataforma, quero que cada utilizador só aceda ao que lhe pertence, para que nenhum dono veja ou altere dados de outro.
 
- Os endpoints estão protegidos por perfil (Jogador, Dono, Admin) com políticas na API.
- Os recursos de um clube (clube, campos, bloqueios, reservas) verificam que o clube pertence ao dono autenticado.
- Pedidos a recursos de outro dono devolvem 404, para não revelar que existem.
- Testes de integração cobrem: o dono A não lê nem altera dados do dono B; um jogador não acede a `/manage` nem a `/admin`; um dono não acede a `/admin`.
- Estado: feita para os clubes (US06). O mecanismo é o `OwnedClubs` (Application): todas as consultas da área do dono partem dos clubes do dono com sessão, e um clube de outro dono dá o mesmo 404 `club_not_found` que um clube que não existe. Um teste descobre sozinho todos os endpoints `/api/manage` (401 sem sessão, 403 para jogador e admin, 404 para o clube de outro dono nas rotas com `{clubId}`), por isso cobre também os endpoints que vierem a seguir (campos, bloqueios, reservas). Como usar: ver AGENTS.md.
### T03 · Sem reservas duplicadas · 5 pontos
 
Como jogador, quero ter a certeza de que um slot reservado é só meu, para não chegar ao campo e encontrá-lo ocupado.
 
- A base de dados impede duas reservas confirmadas sobrepostas no mesmo campo (por exemplo, com uma exclusion constraint no PostgreSQL), e não só a validação na aplicação.
- A regra vale também para reservas criadas pelo dono (US13).
- A sobreposição entre reservas e bloqueios é verificada dentro de uma transação.
- Teste de integração: vários pedidos simultâneos para o mesmo slot resultam em exatamente um 201 e os restantes 409.
- A API devolve 409 com uma mensagem clara, nunca um 500.
### T04 · Datas e fusos horários · 3 pontos
 
Como jogador, quero ver sempre as horas certas, para não aparecer à hora errada.
 
- Todas as datas são guardadas em UTC; os horários de funcionamento são guardados como hora local, com o fuso do clube (Europe/Lisbon).
- A geração de slots está certa nos dias de mudança de hora (março e outubro), com testes para esses dias.
- O front end mostra sempre a hora de Lisboa, seja qual for o fuso do browser.
### T05 · Dados de demonstração · 2 pontos
 
Como programador, quero dados de exemplo realistas, para demonstrar a app e ver o painel de admin com números.
 
- Um comando cria donos, clubes, campos, horários, jogadores e reservas dos últimos 60 dias e dos próximos 14.
- Só corre com uma flag explícita, nunca por engano em produção.
- Os dados geram números com sentido nos gráficos e nas listas do admin.
### T06 · Deploy contínuo · 5 pontos
 
Como programador, quero que cada merge na main fique publicado automaticamente, para ter sempre uma versão online para mostrar.
 
- A aplicação está acessível num URL público com HTTPS.
- Cada merge na main que passe no CI faz deploy automático.
- Os segredos (ligação à base de dados, chave JWT, SMTP, conta de admin) vêm de variáveis de ambiente, nunca do repositório.
- As migrações são aplicadas no deploy de forma controlada.
- Existe um endpoint `/health` para verificar se a API está a responder.
- O rate limiting só funciona em produção se `ForwardedHeaders:KnownProxies` (`ForwardedHeaders__KnownProxies__0`) for o endereço do Caddy, com um endereço fixo na rede partilhada com a API; senão todos os utilizadores partilham o IP do Caddy e o mesmo limite.
## Resumo de pontos
 
| Épico | MVP (histórias) | MVP (pontos) | Depois (histórias) | Depois (pontos) |
| --- | --- | --- | --- | --- |
| 1 — Contas e acesso | 4 | 12 | 1 | 3 |
| 2 — Configuração do clube | 4 | 16 | 1 | 5 |
| 3 — Operação do clube | 3 | 13 | 2 | 7 |
| 4 — Pesquisa e reserva | 4 | 16 | 2 | 11 |
| 5 — Notificações | 1 | 3 | 1 | 1 |
| 6 — Administração | 3 | 9 | 3 | 10 |
| Histórias técnicas | 6 | 23 | 0 | 0 |
| **Total** | **25** | **92** | **10** | **37** |
 
As duas histórias de 8 pontos (US11 e US20) são as primeiras a dividir se não couberem num sprint.