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
- Decidido (confirmado):
  - **Email já usado (compromisso aceite):** o critério pede uma mensagem a dizê-lo, por isso o registo revela que um email tem conta. A API responde 409 com o código `email_taken` e sempre a mesma mensagem («Já existe uma conta com este email.»), seja a conta de jogador, de dono ou de admin, sem mais nenhum dado. A comparação ignora maiúsculas e minúsculas e os espaços à volta; o índice único da base de dados decide registos simultâneos. A alternativa sem enumeração (responder sempre «verifica o teu email» e avisar por email) contraria este critério e o de sessão iniciada logo a seguir. O login (US03) e a recuperação (US05) continuam sem revelar se o email existe. O rate limiting das rotas `/api/auth/*` está feito na US03.
  - Nome obrigatório, até 100 caracteres; os espaços à volta são removidos.
  - Email: um endereço simples (sem nome de apresentação), com ponto no domínio e até 256 caracteres; os espaços à volta são removidos; é guardado como foi escrito.
  - Telemóvel opcional: 9 a 15 algarismos, com `+` inicial opcional; os espaços são removidos. Aceita números estrangeiros.
  - Palavra-passe: só o mínimo de 8 caracteres, sem máximo nem regras de composição; é usada tal como foi escrita (sem cortar espaços).
  - Sessão: a validade de 7 dias do refresh token é absoluta (a rotação mantém a data de expiração), por isso é preciso entrar de novo, no máximo, 7 dias depois do registo ou do login. Um refresh token já usado que volte a aparecer termina todas as sessões da conta (docs/stack.md); os separadores renovam um de cada vez (Web Locks, US03), para não terminarem a sessão uns aos outros.
  - Pedidos anónimos a rotas que não existem recebem 401 (política de fallback que exige sessão), e não 404.
  - A conta de admin criada pelo seed tem o nome «Administrador».
  - A chave de assinatura do JWT de desenvolvimento (`Jwt:SigningKey` em `appsettings.Development.json`) está no repositório e só serve para desenvolvimento; fora dele vem sempre de `Jwt__SigningKey`.
  - As bibliotecas do front end usam versões publicadas há pelo menos duas semanas. Exceção decidida: o `zod` 4.6.7 (publicado a 2026-10-12) foi adotado de imediato porque corrige o aviso de segurança GHSA-mrhx-c7xc-m3w5 (gravidade alta), que fazia falhar o `npm audit` do CI; as versões anteriores têm o aviso. Todas as dependências do `package.json` têm versão exata (sem `^`); mudar uma versão é sempre um commit próprio.
- Decidido (confirmado):
  - Conta desativada (requisito do Épico 1): `users.deactivated_at` preenchido. A US29 acrescenta o motivo e o registo de quem desativou.
  - A página inicial mostra «Olá, \<nome>» no cabeçalho depois do registo, ou as ligações «Entrar» e «Criar conta» sem sessão (o «Entrar» veio com a US03).
### US02 · Registo de dono · M · 2 pontos
 
Como dono de um clube, quero criar a minha conta de dono, para registar e gerir o meu clube na plataforma.
 
- Existe uma página de registo própria para donos, separada da do jogador.
- Pede os mesmos dados e aplica as mesmas validações da US01.
- Um email já usado noutra conta (de qualquer perfil) é rejeitado.
- A conta criada tem o perfil Dono.
- Depois do registo, o dono entra em `/manage` e é convidado a criar o primeiro clube.
- Estado: feita. A página é `/register/owner` e o endpoint é `POST /api/auth/register/owner`: o perfil Dono é fixo no endpoint e nunca vem do pedido, por isso nenhum pedido cria um admin. Mesmo corpo, mesmas validações, o mesmo 409 `email_taken` para qualquer perfil e os mesmos cookies que o registo de jogador. O registo de jogador e o de dono partilham o mesmo limite de pedidos por IP.
- Decidido: um utilizador com sessão que abra `/login` ou `/register` (incluindo a página de registo de donos) é reencaminhado para a página inicial do seu perfil, com uma verificação no servidor.
- Depois do registo, `/manage` mostra o estado vazio com o botão «Criar o primeiro clube», que abre o formulário de criação (US06).
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
- Decidido (confirmado):
  - **Falha genérica:** email desconhecido, palavra-passe errada, conta bloqueada e conta desativada dão todos o mesmo 401 `invalid_credentials` («Email ou palavra-passe incorretos.»), depois do mesmo trabalho de hash, para o tempo de resposta também não revelar se o email existe. Um email mal formado também dá este 401; só os campos vazios dão 400 `validation_failed`, por campo.
  - **Bloqueio:** 5 palavras-passe erradas seguidas bloqueiam a conta durante 15 minutos (bloqueio do Identity). Tentativas durante o bloqueio não o prolongam. Um login certo recomeça a contagem. Tentativas erradas em paralelo na mesma conta podem não ser todas contadas (controlo de concorrência do Identity); o rate limiting cobre esse caso.
  - **Rate limiting:** janela fixa de 1 minuto, por IP do cliente e por rota: login 10, registo 5, refresh 30 pedidos (o refresh tem mais margem porque vários utilizadores podem partilhar um IP). Um cliente IPv6 conta por rede /64. Valores configuráveis em `RateLimiting:Auth`. Acima do limite: 429 `too_many_requests` com `Retry-After`.
  - **Proxies de confiança:** por omissão nenhum; o IP é o da ligação. Em produção só o Caddy, com um endereço fixo (`ForwardedHeaders:KnownProxies`), e não a rede inteira. O contentor do Next.js nunca é de confiança: o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser. Em desenvolvimento todos os pedidos partilham um limite (o do contentor do Next.js), o que chega para um programador.
  - **Páginas públicas (front end):** `/` e as páginas de pesquisa e de clube ficam públicas para todos os perfis; os guardas só existem em `/manage` e `/admin`. Um utilizador com sessão que abra `/login`, `/register` ou `/register/owner` é reencaminhado no servidor para a página inicial do seu perfil (US02).
  - **Caminho de regresso:** o guarda lê o caminho completo do pedido (cabeçalho `x-pathname`, posto pelo `proxy.ts` mínimo, que o reescreve sempre e não renova tokens nem decide acessos) e envia para `/login?next=<caminho completo>`; o `next` continua a ser validado por `safeNext`.
  - **`next` e a área do utilizador:** para o jogador, a sua área são as páginas públicas (tudo exceto `/manage`, `/admin`, `/login` e `/register`); um dono só aceita caminhos de `/manage` e um admin só de `/admin`.
  - **Logout:** responde sempre 204 e apaga os dois cookies, com ou sem sessão válida. Um refresh token já usado que apareça no logout termina todas as sessões da conta, como no refresh.
  - **Refreshes:** vêm sempre do browser (o cookie `refresh_token` só vai para `/api/auth`, por isso o servidor Next.js nunca renova a sessão); o limite do refresh conta, por isso, o IP real do utilizador.
- Decidido (confirmado):
  - O email é comparado sem maiúsculas/minúsculas e sem os espaços à volta; a palavra-passe é usada tal como foi escrita.
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
- Decidido (URLs): ao criar o clube gera-se um `slug` único a partir do nome (por exemplo `clube-desportivo-do-porto`); o `slug` não muda se o nome mudar, para os endereços públicos continuarem a funcionar. Numa colisão acrescenta-se um sufixo numérico (`-2`, `-3`, …). A área do dono usa o GUID público do clube: `/manage/clubs/{id}`.
- O clube guarda o seu fuso horário (`Europe/Lisbon`). Quando a tabela de clubes existir, repete-se o teste de armazenamento real da T04 com ela: instantes em UTC e fuso do clube guardado.
- Estado: feita. Back end (`/api/manage/clubs`: criar, listar, ver e editar, com o perfil Dono e a T02) e front end: `/manage` lista os clubes do dono (por nome, 20 por página) e, sem clubes, mostra o estado vazio com «Criar o primeiro clube»; `/manage/clubs/new` cria e `/manage/clubs/{id}` edita (formulário com uma mensagem por campo, erros da API nos campos certos). Um clube que não existe e o clube de outro dono mostram a mesma página «Clube não encontrado»; um id que não é um GUID nem chega à API.
- Decidido (confirmado):
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
- Estado: feita. Back end (`/api/manage/clubs/{clubId}/pitches`: criar, listar, ver, editar, desativar e reativar, com o perfil Dono e a T02) e front end: a página `/manage/clubs/{id}` mostra, por baixo dos dados do clube, a lista de campos (ativos e inativos, por nome, 20 por página) com «Adicionar campo», «Editar», «Desativar» e «Reativar». O desporto é uma lista com as etiquetas «Futsal», «Futebol 7», «Padel» e «Ténis»; o preço escreve-se e mostra-se à portuguesa («12,50 €»; aceita também «12.50») e vai para a API como número. Os campos inativos aparecem marcados com «Inativo». Um nome repetido aparece como erro no campo do nome. Um campo pedido através de um clube que não é o seu dá 404 `pitch_not_found`; o clube de outro dono dá 404 `club_not_found`, como na US06. Feito com a T03/US18: desativar um campo com reservas confirmadas que ainda não acabaram (futuras ou em curso) é recusado com 422 `pitch_has_future_bookings`, e a mensagem e o campo `count` dizem quantas são; a contagem é feita sob o trinco da linha do campo, para nenhuma reserva entrar entre a contagem e a desativação.
- Decidido (confirmado):
  - Códigos estáveis do desporto na API, guardados como texto: `futsal`, `football7`, `padel`, `tennis` (os nomes em português são só da interface). Um campo novo começa ativo.
  - Nome: obrigatório, até 100 caracteres (espaços à volta removidos), único em cada clube sem distinguir maiúsculas de minúsculas (também contra campos inativos); o mesmo nome pode existir noutro clube. Um nome repetido é um erro de validação no campo `name` (400), não um 409.
  - Preço por slot: maior que 0 € e até 500 €, com no máximo duas casas decimais (`numeric(10,2)`, com check constraint).
  - O `PUT` substitui nome, desporto e preço; o estado só muda com `POST …/deactivate` e `POST …/reactivate`, que são idempotentes (repetir devolve 200).
  - Lista do dono: campos ativos e inativos, 20 por página por omissão (no máximo 100), ordenada pelo nome sem distinguir maiúsculas e depois pelo id.
### US08 · Horário e duração dos slots · M · 5 pontos
 
Como dono, quero definir o horário de funcionamento e a duração dos slots de cada campo, para que os slots livres sejam gerados automaticamente.
 
- Para cada dia da semana, o dono define hora de abertura e de fecho, ou marca o dia como fechado.
- A duração do slot é de 30, 60 ou 90 minutos, por campo.
- Os slots são gerados a partir da abertura, em blocos da duração definida; um bloco que passe da hora de fecho não é gerado.
- Mudar o horário não altera reservas já feitas; uma reserva fora do novo horário continua válida.
- As horas são introduzidas e mostradas em hora de Lisboa (T04).
- O fecho mais tarde é às 00:00 (meia-noite do próprio dia). **Limitação conhecida do MVP:** horários que passam da meia-noite (por exemplo, 22:00–02:00) são rejeitados.
- Quando a tabela de horários existir, repete-se o teste de armazenamento real da T04 com ela: horários guardados como hora local (`time`) com o fuso do clube, e os slots gerados em UTC.
- Estado: feita. Front end: em cada campo de `/manage/clubs/{id}` o botão «Horário» abre, por baixo do campo, o editor da semana (sete linhas, segunda-feira primeiro, cada uma com «Fechado» ou hora de abertura e de fecho, mais a duração do slot de 30, 60 ou 90 minutos; guarda a semana toda e os erros da API aparecem na linha e no campo certos) e a pré-visualização dos slots (data de hoje até 30 dias à frente, em hora de Lisboa, com «(verão)» e «(inverno)» na hora repetida de outubro). Back end: `GET` e `PUT /api/manage/clubs/{clubId}/pitches/{pitchId}/schedule` (duração e os sete dias; o `PUT` substitui a semana inteira) e `GET …/slots?date=AAAA-MM-DD` (pré-visualização, só de leitura, dos slots gerados nessa data local, com início e fim em UTC, pelo `SlotGenerator` da T04). Tudo com o perfil Dono e a T02: o clube de outro dono dá 404 `club_not_found` e um campo pedido através de outro clube dá 404 `pitch_not_found`, antes de qualquer validação. Os horários ficam na tabela `pitch_opening_hours` (uma linha por dia aberto, `time` sem fuso); o teste de armazenamento da T04 foi repetido com ela. Mudar o horário ou a duração não mexe nas reservas (a US17 já diz que um slot não está livre se algo se sobrepuser).
- Decidido (confirmado):
  - As horas vão de meia em meia hora (09:00, 09:30, …), sem segundos; a API e a base de dados rejeitam o resto (a base de dados verifica só a ordem: fecho depois da abertura ou 00:00).
  - Um campo novo fica fechado todos os dias e com slots de 60 minutos até o dono definir o horário. Os campos que já existiam antes desta migração ficam também com 60 minutos.
  - Formato de cada dia: `{ "closed": true }` ou `{ "closed": false, "opens": "09:00", "closes": "22:00" }` (hora local, `HH:mm`). Os sete dias são obrigatórios no `PUT`, e um dia fechado com horas é rejeitado (em vez de as ignorar). Os erros vêm por campo com o caminho JSON (`monday.closes`).
  - A pré-visualização mostra os slots que o horário gera e marca como bloqueados (`blocked`) os que se sobrepõem a algum bloqueio (US09) e como reservados (`booked`) os que se sobrepõem a uma reserva confirmada (US18), mesmo que essa reserva tenha sido feita numa grelha antiga; ainda não tira os slots que já começaram. Funciona também para um campo inativo. Uma data fora de hoje até 30 dias (em hora de Lisboa) ou mal escrita dá 400 no campo `date`.
  - Interface: a pré-visualização mostra «Reservado» nos slots com reserva confirmada e «Bloqueado» nos que se sobrepõem a um bloqueio (os dois, quando é o caso); os slots livres não levam marca. A nota por baixo diz que os bloqueios e as reservas aparecem marcados.
### US09 · Bloquear slots · M · 3 pontos
 
Como dono, quero bloquear períodos de um campo, para que ninguém o reserve durante manutenção ou eventos privados.
 
- Um bloqueio tem campo, início, fim e motivo (opcional).
- Os slots dentro de um bloqueio não aparecem como livres para os jogadores.
- Um bloqueio que se sobreponha a reservas confirmadas é rejeitado, e a mensagem lista essas reservas para o dono decidir (cancelar pela US12).
- O dono pode remover um bloqueio futuro, e os slots voltam a ficar livres.
- Os bloqueios aparecem no calendário (US11).
- Estado: feita. Front end: o botão «Bloqueios» em cada campo de `/manage/clubs/{id}` abre a lista dos bloqueios em curso e futuros (hora de Lisboa, motivo se houver, «Em curso» nos que já começaram), o formulário para criar um (data e hora de início e de fim, que podem ser de dias diferentes, e motivo opcional) e a remoção, com confirmação, dos que ainda não começaram; os erros da API (sobreposição, início no passado, bloqueio já começado) aparecem no sítio certo, e a pré-visualização dos slots mostra «Bloqueado» nos slots afetados. Back end: `POST`, `GET` e `DELETE /api/manage/clubs/{clubId}/pitches/{pitchId}/blocks[/{blockId}]`, com o perfil Dono e a T02 (clube de outro dono: 404 `club_not_found`; campo de outro clube: 404 `pitch_not_found`; bloqueio de outro campo: 404 `block_not_found`). Criar corre numa transação que começa por trancar a linha do campo (`SELECT … FOR UPDATE`, `IPitchLock`) e só depois verifica a sobreposição com outros bloqueios e, desde a T03/US18, com reservas confirmadas: um bloqueio sobre reservas dá 409 `block_overlaps_bookings`, com as horas dessas reservas na mensagem e a lista (`bookings`: id, início e fim em UTC) na resposta. A pré-visualização de slots do dono (US08) marca como `blocked` os slots que se sobrepõem a algum bloqueio.
- Decidido (confirmado):
  - O início e o fim chegam como data e hora locais de Lisboa (`2026-10-25T09:00`), de meia em meia hora, e convertem-se com a `ClubTime` (as regras de mudança de hora da T04); a resposta traz instantes UTC. Os bloqueios podem passar da meia-noite (não são horários de funcionamento).
  - O início não pode estar no passado (começar agora é aceite); o fim tem de ser depois do início e no máximo 90 dias à frente.
  - Motivo opcional, até 200 caracteres.
  - Dois bloqueios do mesmo campo não se podem sobrepor (409 `block_overlap`); bloqueios que se tocam (um acaba quando o outro começa) são aceites. A verificação é feita sob o trinco da linha do campo, sem constraint na base de dados.
  - A lista traz os bloqueios em curso e futuros (os que ainda não acabaram) por ordem de início, 20 por página.
  - Só se remove um bloqueio que ainda não começou; um que já começou ou acabou dá 422 `block_already_started` e fica no histórico. Remover é apagar a linha.
  - Um bloqueio pode ser criado num campo inativo.
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
- Os bloqueios aparecem no calendário (US09): `GET …/pitches/{pitchId}/blocks` dá os bloqueios em curso e futuros de cada campo.
- Candidata a dividir: vista diária (5) e vista semanal (3).
- Estado: back end feito (`GET /api/manage/clubs/{clubId}/calendar` e `GET …/bookings/{bookingId}`). A interface (vista diária e semanal, navegação, abrir uma reserva) vem noutro ramo.
- Decisões de implementação (provisórias, a confirmar):
  - `GET /api/manage/clubs/{clubId}/calendar?from=AAAA-MM-DD&to=AAAA-MM-DD&includeCancelled=false` (perfil Dono, pela T02: o clube de outro dono, ou inexistente, é 404 `club_not_found`, antes de se olhar para a query). `from` e `to` são datas locais do clube, ambas incluídas; `from` não pode ser depois de `to` e o intervalo tem no máximo 7 dias (400 em `from`/`to`/`includeCancelled`, mensagens em português). O dono pode ver o passado e o futuro, sem a janela de 30 dias dos jogadores; só se exige que as datas estejam entre 2000 e 2100 (limite defensivo).
  - A resposta tem `from`, `to`, `pitches`, `bookings` e `blocks`. Os campos do clube: os ativos e os inativos que tenham reservas visíveis no intervalo (o mesmo critério que decide que reservas se veem), ordenados pelo nome; cada um com `id`, `name`, `sport`, `status`, `slotDurationMinutes` e `days` (uma entrada por data do intervalo, com `opens` e `closes` locais «HH:mm», ou `null` num dia fechado; fecho «00:00» é meia-noite). Os instantes das reservas e dos bloqueios vão em UTC; a interface mostra-os em hora de Lisboa.
  - Uma reserva pertence ao intervalo se se sobrepuser a ele (meio-aberto), e um dia é o dia local: a 25 de outubro de 2026 (25 horas) o intervalo é [2026-10-24T23:00Z, 2026-10-26T00:00Z), e as duas 01:30 contam. Uma reserva que atravessa a meia-noite local aparece nos dois dias.
  - Reservas: `id`, `pitchId`, `startsAt`, `endsAt`, `status`, `price` e `playerName`, por ordem de início. As canceladas (pelo jogador ou pelo clube) só vêm com `includeCancelled=true`; uma falta (`no_show`) não é um cancelamento e aparece sempre. Bloqueios: `id`, `pitchId`, `startsAt`, `endsAt`, `reason`, dos campos listados.
  - Só o dono do clube vê o nome do jogador (é o único dado pessoal; não há email, telefone nem id da conta, e há um teste que procura esses valores na resposta). Jogadores e admins recebem 403, sem sessão 401.
  - `GET /api/manage/clubs/{clubId}/bookings/{bookingId}`: os detalhes (campo, desporto, início, fim, preço, estado, `playerName`, `createdAt`, `cancelledAt`, `cancellationReason` e `canCancel`, verdadeiro para uma reserva confirmada que ainda não começou). Uma reserva de outro clube, mesmo do mesmo dono, e um id desconhecido são o mesmo 404 `booking_not_found`.
  - A US13 vai ter de acrescentar o nome do cliente (sem conta) ao `playerName`: hoje a reserva tem sempre um jogador e o nome vem da conta.
### US12 · Cancelar reserva como dono · M · 2 pontos
 
Como dono, quero cancelar uma reserva, para resolver problemas como um campo avariado.
 
- O dono pode cancelar qualquer reserva futura dos seus clubes, sem o limite da janela de cancelamento.
- É obrigatório indicar um motivo.
- A reserva passa a «Cancelada pelo clube» (não é apagada) e o slot volta a ficar livre.
- O jogador recebe um email com o motivo (US22).
- Estado: back end feito (`POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`). A ação na interface (no detalhe da reserva do calendário) vem noutro ramo.
- Decisões de implementação (provisórias, a confirmar):
  - Corpo `{ "reason": "…" }`: o motivo é obrigatório, sem espaços nas pontas, com no máximo 200 caracteres (400 em `reason`).
  - Ordem das respostas: clube de outro dono 404 `club_not_found` → reserva que não é deste clube 404 `booking_not_found` (as duas antes de validar o corpo) → 400 → 422 `booking_not_confirmed` (já cancelada, pelo jogador ou pelo clube, ou falta) → 422 `booking_already_started` (já começou; começar agora conta como começada).
  - Não há janela de cancelamento para o clube: pode cancelar até ao instante em que a reserva começa.
  - Corre sob o trinco do campo (o mesmo das reservas, dos bloqueios e do cancelamento do jogador) e lê a reserva outra vez lá dentro; por isso, se o dono e o jogador cancelam ao mesmo tempo, só um tem sucesso e o outro recebe 422 `booking_not_confirmed`, nunca um 500. A regra está na entidade (`Booking.CancelByClub`).
  - A reserva passa a `cancelled_by_club` e guarda o motivo (`cancellation_reason`) e o instante (`cancelled_at`, que passou também a ser guardado quando é o jogador a cancelar); nunca é apagada e o slot volta a ficar livre. A resposta é a reserva, como nos detalhes.
### US13 · Reserva em nome de um cliente · M · 3 pontos
 
Como dono, quero criar uma reserva para um cliente que ligou ou apareceu, para que todas as reservas fiquem no sistema.
 
- O dono escolhe campo e slot livre e indica nome e telemóvel do cliente; o cliente não precisa de conta.
- Aplica-se a mesma regra contra reservas duplicadas da US18 e da T03.
- A reserva aparece no calendário marcada como «criada pelo clube».
- Não é enviado email, porque o cliente não tem conta.
- Decisão para quando se fizer (provisória): o calendário e os detalhes (US11) mostram o nome do jogador com um acesso às contas só para reservas de um clube do dono (`IAppDbContext.AccountNames`, ligado às reservas); uma reserva de cliente sem conta (`player_id` vazio) tem de trazer aí o nome do cliente, e o `ck_bookings_has_player` muda para «um jogador, ou o nome e o telemóvel de um cliente».
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
- Clubes sem campos ativos, ou de donos desativados, não aparecem. Um campo inativo não conta para a pesquisa: nem o seu desporto aparece nos resultados nem é encontrado pelo filtro de desporto (US07).
- Uma pesquisa sem resultados mostra uma mensagem clara.
- Resultados paginados, 20 por página.
- Abrir um resultado mostra a página do clube, com contactos e campos. O endereço público da página do clube é `/clubs/{slug}` (o `slug` é gerado na US06).
- A pesquisa funciona sem sessão iniciada.
- Estado: feita. Back end: `GET /api/clubs`, `GET /api/localities` e `GET /api/clubs/{slug}`, todos públicos e só de leitura, sem dados de jogadores nem do dono. Interface: a página inicial `/` é a pesquisa e `/clubs/{slug}` é a página do clube.
- Decidido (confirmado):
  - Um desporto desconhecido é 400 em `sport`. Os códigos são exatamente os estáveis (`futsal`, `football7`, `padel`, `tennis`): «Padel» com maiúscula também é 400.
  - `page` vai de 1 a 10000 (limite defensivo); outro valor, ou não numérico, é 400 em `page`, com a mensagem em português.
  - A localidade filtra pelo valor inteiro: igualdade sem distinguir maiúsculas, acentos nem espaços a mais (`clubs.locality_key`, com índice, calculada por `ClubRules.LocalityKey`); não é uma pesquisa por parte do nome («Port» não encontra «Porto»).
  - `GET /api/localities` devolve uma lista simples de textos (para um select): as localidades dos clubes visíveis, uma por grafia (ignorando maiúsculas e acentos, mostrada numa das grafias em uso), ordenadas.
  - Cada resultado da pesquisa lista os desportos de todos os campos ativos do clube, mesmo quando se filtra por um desporto.
  - Na pesquisa a data é opcional (hoje em Lisboa por omissão); nos slots é obrigatória (ver a US17).
- Decisões de implementação (provisórias, a confirmar):
  - Visível = o dono não está desativado (US29) e o clube tem pelo menos um campo ativo (US07). Tudo o que é público parte de `VisibleClubs`; o que não é visível não existe: um clube desconhecido, sem campos ativos ou de um dono desativado dão o mesmo 404 `club_not_found`.
  - `GET /api/clubs?sport=&locality=&date=&page=`: 20 por página (fixo), ordenado pelo nome sem distinguir maiúsculas e depois pelo id. `date` é `AAAA-MM-DD`, hoje em Lisboa por omissão, de hoje até hoje + 30; fora disso ou mal escrita é 400 em `date`. Filtros vazios contam como ausentes.
  - Cada resultado traz só `slug`, `name`, `locality`, `sports` (os desportos dos campos ativos) e `hasFreeSlots` (algum campo ativo tem um slot livre na data, pela mesma regra da US17). Não traz morada nem contactos. A disponibilidade só se calcula para os clubes da página.
  - `GET /api/clubs/{slug}`: nome, morada, localidade, telefone, email de contacto e os campos ativos (`id`, `name`, `sport`, `pricePerSlot` atual), ordenados pelo nome. O `slug` tem de coincidir exatamente. O id do clube não aparece (é da área do dono).
- Decisões da interface (provisórias, a confirmar):
  - A pesquisa (`/`) tem um select de desporto («Todos os desportos» e os quatro desportos), um select de localidade (as de `GET /api/localities`) e uma data (hoje em Lisboa por omissão, de hoje até hoje + 30). Pesquisa logo ao mudar um campo, sem botão; funciona sem sessão. A pesquisa fica no endereço (`?sport=&locality=&date=&page=`), por isso pode ser partilhada e o botão «voltar» funciona. Mudar um filtro volta à primeira página; um valor inválido no endereço é ignorado e uma data fora do período mostra o erro sem perguntar à API.
  - Cada resultado mostra o nome (ligação para `/clubs/{slug}`, com `?date=` se se pesquisou uma data), a localidade, os desportos e «Há horários livres» ou «Sem horários livres». Páginas de 20 com «Anterior», «Seguinte» e «Página X de Y» (só se houver mais de uma). Sem resultados: «Não encontrámos clubes com estes filtros. Experimenta outra localidade, outro desporto ou outra data.» Uma página para lá do fim oferece «Voltar à primeira página».
  - A página do clube é um Server Component que lê o clube na API interna, sem cookies e sem cache: contactos (telefone e email como ligações) e os campos ativos com o preço por slot em formato pt-PT («18,50 € por slot»). Um clube desconhecido, escondido, ou um slug que não pode existir mostra a página «Clube não encontrado», sem dizer porquê.
### US17 · Ver slots livres · M · 5 pontos
 
Como jogador, quero ver os slots livres de um campo numa data, para escolher a hora.
 
- Mostra os slots do dia com hora de início, hora de fim e preço.
- Um slot não aparece como livre se tiver reserva confirmada, estiver bloqueado, estiver fora do horário ou já tiver começado.
- Só se podem escolher datas de hoje até 30 dias à frente.
- Um campo inativo não mostra slots nem aceita reservas (US07).
- Decidido: um slot não está livre se se sobrepuser a qualquer reserva confirmada ou bloqueio, e não só se tiver o mesmo início. Mudar a duração dos slots de um campo (US08) não altera as reservas existentes, por isso uma reserva antiga pode ficar desalinhada com a nova grelha e ocupar parte de um slot novo; esse slot não aparece como livre.
- As horas aparecem em hora de Lisboa e estão certas nos dias de mudança de hora (T04).
- No dia em que a hora atrasa (último domingo de outubro), as horas entre a 01:00 e as 02:00 aparecem duas vezes e levam a indicação «(verão)» ou «(inverno)»; nenhum slot é escondido.
- Estado: feita. Back end: `GET /api/clubs/{slug}/pitches/{pitchId}/slots?date=`, público e só de leitura. Interface: a secção «Horários» da página do clube.
- Decidido (confirmado):
  - Na API dos slots a data (`AAAA-MM-DD`) é obrigatória e vai de hoje até hoje + 30 em Lisboa: em falta, mal escrita ou fora do período é 400 em `date`. Na pesquisa (US16) é opcional.
  - O que está escondido não existe, e isso verifica-se antes da data: um clube desconhecido ou escondido (sem campos ativos, dono desativado), e um campo desconhecido, inativo, de outro clube ou com um id que não é um GUID, são 404 (`club_not_found` ou `pitch_not_found`) mesmo que a data seja inválida.
  - Quem tem sessão de dono ou de admin vê os slots mas não os pode escolher (não são botões, não há botão de reservar); ver a US18.
- Decisões de implementação (provisórias, a confirmar):
  - A resposta é `date` e `slots`; cada slot traz `startsAt` e `endsAt` (instantes UTC com fuso), `price` (o preço atual do campo) e um só booleano `available`. Nunca diz porque não está livre, nem quem reservou, nem o id de uma reserva ou de um bloqueio. Um dia fechado devolve a lista vazia.
  - `available` = não se sobrepõe, nem em parte, a uma reserva confirmada ou a um bloqueio, está dentro do horário e ainda não começou (começar agora conta como começado, como na US18). É uma só conta (`PitchDayCalendar` e `DaySlot.IsFreeAt`), partilhada com a pré-visualização do dono e com o «há slots livres» da pesquisa, e um teste confirma que concordam.
  - Em 25 de outubro a API devolve os slots como instantes distintos (a hora das 01:00 às 02:00 aparece duas vezes, uma em cada fuso); a etiqueta «(verão)/(inverno)» é da interface.
- Decisões da interface (provisórias, a confirmar):
  - A secção «Horários» da página do clube tem um select de campo (o primeiro por omissão; mostra nome, desporto e preço) e uma data (hoje em Lisboa por omissão, de hoje até hoje + 30). Os slots do dia aparecem em hora de Lisboa, qualquer que seja o fuso do browser, com o preço, e a hora que se repete em outubro leva «(verão)» ou «(inverno)». O campo e a data ficam no endereço (`?pitch=&date=`).
  - Um slot que não está livre aparece desativado (`disabled`), sem dizer porquê: nem «reservado», nem «bloqueado», nem «fechado». Um dia sem slots diz «Sem horários neste dia.» Uma data fora do período mostra o erro sem perguntar à API.
### US18 · Reservar slot · M · 3 pontos
 
Como jogador, quero reservar um slot livre, para garantir o campo.
 
- Só jogadores com sessão iniciada podem reservar; sem sessão, é pedido o login e o jogador volta ao mesmo slot.
- Antes de confirmar, aparece um resumo com clube, campo, data, hora e preço.
- A reserva fica «Confirmada» e guarda o preço desse momento: o preço por slot do campo (US07) copiado para a reserva. Mudar depois o preço do campo nunca altera as reservas existentes.
- Um campo inativo não aceita reservas (US07).
- Decidido e feito (T03/US18): desativar um campo com reservas futuras confirmadas é recusado e a mensagem diz quantas são (US07): `POST …/pitches/{pitchId}/deactivate` devolve 422 `pitch_has_future_bookings` com `count` (regra de negócio, como na tabela de códigos da Stack; esta nota dizia antes 409).
- Decidido e feito (T03/US18): um bloqueio que se sobreponha a reservas confirmadas é rejeitado (409 `block_overlaps_bookings`) e a mensagem lista essas reservas (US09); a verificação está em `BlockService.CreateAsync`, dentro do trinco da linha do campo, e a criação de uma reserva tranca a mesma linha antes de verificar os bloqueios.
- Estado: feita. Back end com a T03 (`POST /api/bookings`, perfil Jogador); interface na página do clube, com a US16 e a US17. Os emails (US22) ainda não existem; ver as minhas reservas e cancelar é a US19.
- Decidido (confirmado):
  - O pedido traz o campo e o início do slot como instante com fuso (`2026-10-25T00:00:00Z`, como as listas de slots o dão), para a hora repetida de outubro nunca ser ambígua.
  - Códigos: 404 `pitch_not_found` (campo desconhecido); 422 `club_unavailable` (dono desativado), `pitch_inactive`, `outside_booking_window` (data local fora de hoje até 30 dias em Lisboa, incluindo dias passados), `invalid_slot` (um início que não seja um slot gerado: dia fechado, fora do horário, fora da grelha, segundos a mais), `slot_started`; 409 `slot_blocked` (um bloqueio sobrepõe-se, mesmo em parte) e `slot_taken` («Este horário acabou de ser reservado. Escolhe outro.»). Os 422 são verificados por esta ordem; os 409 só depois de trancar a linha do campo.
  - Um slot que começa exatamente agora conta como começado (`slot_started`).
  - A desativação de um campo conta as reservas confirmadas que ainda não acabaram: as que estão a decorrer também impedem.
  - Os estados na API são `confirmed`, `cancelled_by_player`, `cancelled_by_club` e `no_show`; na base de dados ficam com o nome do enum (`Confirmed`, …), como pede a constraint.
  - A resposta 201 traz o cabeçalho `Location: /api/bookings/{id}` (relativo, como os outros), que `GET /api/bookings/{id}` resolve (US19).
- Decisões de implementação (provisórias, a confirmar):
  - O fim vem sempre do horário do campo e o preço é o preço por slot atual do campo.
  - A transação tranca a linha do campo, volta a ler o campo (estado, preço e horário como estão nesse momento), verifica os bloqueios e insere; a sobreposição com outra reserva fica só a cargo da exclusion constraint (23P01 → 409 `slot_taken`), que é a última defesa e a regra também para a US13.
  - A resposta 201 traz a reserva (`id`, `pitchId`, `clubId`, início e fim em UTC, `price`, `status` = `confirmed`).
  - Preparado para a US13: `player_id` pode ser nulo na tabela, mas uma check constraint (`ck_bookings_has_player`) obriga a tê-lo até a US13 a trocar por «um jogador, ou o nome e o telemóvel do cliente»; a regra de sobreposição não muda.
- Decidido (confirmado): a interface da reserva
  - O que o jogador escolhe (campo, data e slot) fica no endereço (`?pitch=&date=&slot=<instante com fuso>`), não numa store Zustand: é isso que permite voltar ao mesmo slot depois do login e partilhar a ligação. Escolher um slot livre abre, por baixo da lista, o «Resumo da reserva» (clube, campo, data, hora e preço); «Cancelar» fecha-o, e mudar de campo ou de data também.
  - Sem sessão, o resumo oferece «Entrar para reservar», que vai para `/login?next=<este endereço com campo, data e slot>`; depois do login o jogador volta ao mesmo clube, campo, data e slot, com o resumo aberto. O `safeNext` não precisou de mudar: já aceita uma página pública com query para um jogador, e manda um dono ou um admin para a sua área, porque não podem reservar. Se o slot entretanto deixou de estar livre (ou já não existe), aparece «Este horário já não está disponível. Escolhe outro.» e não há resumo.
  - Só um jogador vê «Confirmar reserva». Donos e admins veem os slots mas não os podem escolher, nem há botão de reservar, e uma nota diz «Só os jogadores podem fazer reservas. Entra com uma conta de jogador para reservar.»
  - Em 409 (`slot_taken`, `slot_blocked`) ou 422 (`slot_started`, `invalid_slot`, `outside_booking_window`, `pitch_inactive`, `club_unavailable`) mostra-se a mensagem desse código («Este horário acabou de ser reservado. Escolhe outro.» para `slot_taken`), fecha-se o resumo e recarrega-se a lista de slots. Um 401 (sessão terminada) volta ao login, com regresso ao slot; sem ligação ao servidor mantém-se o resumo para tentar de novo.
  - Para um visitante, `club_not_found` diz «Este clube já não está disponível.» (a mensagem geral fala a um dono).
- Decisões da interface (provisórias, a confirmar):
  - Confirmar envia só `pitchId` e `startsAt` (o instante com fuso, tal como a lista o deu); o botão fica desativado enquanto se reserva. Em 201 aparece «Reserva confirmada» com os mesmos dados, a lista de slots e a pesquisa recarregam-se e o slot passa a desativado.
  - Quem escolhe «Criar conta» no ecrã de login também volta ao mesmo slot: a ligação leva o `next` (`/register?next=…`), a página de registo de jogador lê-o (e a ligação «Entrar» desse ecrã também o mantém) e, depois de criada a conta, o `safeNext` decide se o aceita: um endereço público segue-se, qualquer outro (outro site, área do dono ou do admin, a própria página de login) é ignorado e o jogador vai para a página inicial. O registo de dono não usa `next`.
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
- Estado: feita. Back end e interface (a página `/bookings`, «As minhas reservas»). O email de cancelamento é da US22 (o email de cancelamento indica quem cancelou); a US19 não envia emails.
- Decisões da interface (provisórias, a confirmar):
  - A página `/bookings` é só para jogadores: sem sessão vai para `/login?next=<a página, com o separador e a página>` e volta a ela depois de entrar; um dono ou um admin vai para a sua área (`/manage`, `/admin`). A API continua a ser quem decide (401/403).
  - O separador («Próximas» ou «Anteriores») e a página ficam no endereço (`?tab=past&page=2`; «Próximas» e a página 1 são o valor por omissão), por isso um recarregamento ou o botão de voltar mantêm o sítio. Mudar de separador volta à primeira página. Uma página que não é um número de 1 a 99999 conta como a primeira.
  - Cada reserva mostra o clube (ligação para `/clubs/{slug}`), o campo e o desporto, a data (com o dia da semana e o ano) e a hora em hora de Lisboa (`lib/datetime.ts`, com «(verão)»/«(inverno)» na hora repetida de outubro), o preço pago e o estado em português: «Confirmada», «Cancelada pelo jogador», «Cancelada pelo clube», «Falta».
  - Só as «Próximas» se podem cancelar (uma reserva confirmada que já acabou, em «Anteriores», não tem botão). O botão «Cancelar reserva» pede uma confirmação na própria linha («Sim, cancelar reserva» / «Manter reserva»; só uma de cada vez). Depois do prazo o botão fica desativado e diz «Já não é possível cancelar: o prazo terminou a dd/mm/aaaa hh:mm.»; antes do prazo diz «Podes cancelar até dd/mm/aaaa hh:mm.». O botão só segue `canCancel`: quem decide é a API.
  - Depois de cancelar aparece «Reserva cancelada. O horário voltou a ficar livre.» e recarregam-se as listas e os horários livres das páginas públicas (`["my-bookings"]` e `["catalog"]`). Se o pedido for recusado (422 `outside_cancellation_window`, 422 `booking_not_confirmed`, 404 `booking_not_found`) mostra-se a mensagem do código e recarrega-se só a lista; uma sessão que acabou leva ao login. Uma reserva nova (US18) também recarrega «As minhas reservas».
  - O cabeçalho do jogador tem a ligação «As minhas reservas» (só para o perfil Jogador) e a confirmação de uma reserva tem «Ver as minhas reservas».
  - A mensagem do 422 não leva o prazo (o front end só vê o código); o prazo vê-se na lista, que se recarrega logo a seguir.
- Decisões de implementação (provisórias, a confirmar):
  - `GET /api/me/bookings?scope=upcoming|past&page=` (perfil Jogador, só as reservas de quem pede, 20 por página). `scope` é obrigatório: em falta ou desconhecido é 400 em `scope`; `page` de 1 a 10000, e não numérica também é 400 em `page` (mensagem em português).
  - «Próximas»: reservas confirmadas que ainda não acabaram (uma que está a decorrer conta), da mais antiga para a mais recente. «Anteriores»: as que já acabaram e as canceladas (pelo jogador ou pelo clube) ou marcadas como falta, da mais recente para a mais antiga. Em ambas, a ordem é pelo início e depois pelo id.
  - Cada reserva traz `id`, `clubName`, `clubSlug`, `pitchName`, `sport`, `startsAt`, `endsAt`, `price` (o que foi pago, não o preço atual do campo), `status`, `canCancel` e `cancellationDeadline` (instante UTC, só nas confirmadas; `null` nas outras). `canCancel` é verdadeiro se a reserva está confirmada e agora é estritamente antes do início menos a janela atual do clube. Uma reserva de um clube que entretanto ficou escondido continua na lista do jogador.
  - `GET /api/bookings/{id}` lê uma reserva própria; a de outro jogador e um id desconhecido são o mesmo 404 `booking_not_found`.
  - `POST /api/bookings/{id}/cancel` (perfil Jogador) responde 200 com a reserva já cancelada (`cancelled_by_player`). A reserva nunca é apagada e o slot volta a ficar livre, porque a exclusion constraint só olha para as confirmadas. Ordem das recusas: 404 `booking_not_found` → 422 `booking_not_confirmed` (já cancelada, cancelada pelo clube ou falta; vem antes da janela, por isso a resposta não depende do relógio) → 422 `outside_cancellation_window`, com `cancellationDeadline` (instante UTC) e a janela em horas na mensagem.
  - A fronteira é «estritamente antes»: no instante exato do prazo já não se pode cancelar.
  - A janela lê-se do clube no momento de cancelar, sob o trinco da linha do campo, nunca da reserva (US06). O mesmo trinco serializa dois cancelamentos simultâneos da mesma reserva: um tem sucesso e o outro recebe o 422 `booking_not_confirmed`, nunca um 500.
  - Donos e admins recebem 403 nos três endpoints, e sem sessão 401.
  - A tabela `bookings` ganha o índice por jogador e início que a Stack já pedia (`ix_bookings_player_id_starts_at`).
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
  - O cancelamento feito pelo jogador (US19, `POST /api/bookings/{id}/cancel`) só muda o estado da reserva: o email a avisar vem aqui, em segundo plano, sem fazer falhar o cancelamento.
  - O cancelamento feito pelo dono (US12, `POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`) guarda o motivo e a hora (`cancellation_reason`, `cancelled_at`) e só muda o estado da reserva: o email com o motivo vem aqui, em segundo plano, sem fazer falhar o cancelamento.
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
- Estado: feita (com a US18). A exclusion constraint `bookings_no_overlap` é a da Stack (`btree_gist`, `pitch_id WITH =`, `tstzrange(starts_at, ends_at) WITH &&`, `WHERE (status = 'Confirmed')`), criada na migração `AddBookings`; o erro 23P01 é 409 `slot_taken`. Reservas e bloqueios trancam a mesma linha do campo (`SELECT … FOR UPDATE`) antes de verificar a sobreposição entre si. Testes: 20 pedidos simultâneos para o mesmo slot dão exatamente um 201 e 19 × 409, nunca 500; uma reserva e um bloqueio ao mesmo tempo nunca ganham os dois; uma reserva cancelada deixa o slot livre para a constraint.
- Registos (logs) do conflito (decidido): perder a corrida por um slot é esperado, por isso não deve aparecer como erro. O `BookingStore` regista-o ao nível Information (campo e slot, nunca o jogador) e os dois eventos do EF para esta falha, `SaveChangesFailed` e `CommandError`, descem para Debug (sem filtrar as categorias do EF). O `FailedCommandLogger` volta a registar ao nível Error, na mesma categoria e com o mesmo texto do comando, todos os comandos falhados exceto a violação da exclusion constraint `bookings_no_overlap`; por isso nenhuma outra falha (uma consulta, uma chave estrangeira, um índice único) fica em silêncio. Uma falha que ninguém trate continua a chegar ao tratador global de exceções, que a regista ao nível Error e responde 500. Há testes para as duas situações. Alternativa não escolhida: baixar o `CommandError` para todos os comandos falhados.
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
- A base de dados tem dois utilizadores. O migration bundle corre como o dono da base de dados, que cria a extensão `btree_gist` (extensão de confiança no PostgreSQL 13 ou superior) e o resto do esquema. A API liga-se com um utilizador de aplicação separado, só com direitos de leitura e escrita nos dados e sem DDL, por isso nunca altera o esquema nem cria extensões.
- Existe um endpoint `/health` para verificar se a API está a responder.
- Os endpoints públicos de leitura (`/api/clubs`, `/api/localities`, a página do clube e os slots) têm um limite de pedidos por IP, folgado o suficiente para uma navegação normal (pesquisar, abrir clubes, mudar de data e de campo), para que ninguém os possa usar para extrair todos os dados ou sobrecarregar a API. Responde 429 `too_many_requests` com `Retry-After`, como nas rotas de autenticação, e usa o mesmo IP do cliente (ver o critério de `ForwardedHeaders` abaixo).
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