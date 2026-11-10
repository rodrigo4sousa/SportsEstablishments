# Backlog — App de Reservas de Campos
 
Oct 22, 2026 · @Rodri
 
O MVP soma 92 pontos (19 histórias de utilizador e 6 técnicas); o que fica para depois soma 45. Uma só app Next.js (React) com três áreas: `/` (jogador), `/manage` (dono) e `/admin` (admin), sobre uma API ASP.NET Core.
 
## Pontos a rever no T06

- Limite de pedidos dos endpoints públicos de leitura: mantém-se 120 pedidos por minuto e por IP (`RateLimiting:Public`), a rever na T06.
- **Alvo da T06 (decidido pelo dono do projeto, substitui a máquina virtual com Caddy da Stack; a ADR 0009, reescrita, foi aceite em 2026-11-09):** Next.js na Vercel, API em Docker no plano gratuito do Render, PostgreSQL no Supabase, SMTP no Brevo, GitHub Actions. As variáveis estão em `docs/deployment/environment-variables.md`.
- **Provisório (IP do cliente):** a API é pública no Render e os endereços dos seus proxies não se podem listar, por isso em produção usa `ForwardedHeaders__TrustAnyProxy=true`, com um `ForwardedHeaders__ForwardLimit` explícito (1). Quem ligar diretamente à API pode forjar o `X-Forwarded-For`, por isso **o limite por IP é só de melhor esforço e a proteção real contra adivinhar palavras-passe é o bloqueio de conta do Identity**. **Fechado:** `ForwardLimit=1` deu o IP certo do cliente no primeiro deploy (2026-11-09). Fica provisório o resto: o limite por IP é de melhor esforço, e o IP nas chamadas dos Server Components não foi reportado.
- **Provisório (SMTP em produção):** sem `Email__Host` e `Email__FromAddress`, a API arranca em Production, avisa no log e não envia nada; as mensagens ficam no outbox até o SMTP existir. Substitui, só em Production, a regra da US22 e da T06 «sem eles a API não arranca». Uma das duas sozinha, ou um valor inválido, continua a impedir o arranque.
- **Fechado (Brevo):** o SMTP na porta 2525 com `StartTls` funcionou no primeiro deploy (2026-11-09); a porta 2525 é a que serve, porque o plano gratuito do Render bloqueia a saída nas portas 25, 465 e 587.
- **Provisório (migrações):** em Production a API nunca as aplica no arranque, mesmo com `Database__ApplyMigrationsOnStartup=true`; essa definição só conta fora de Production. O esquema novo fica ativo antes da API nova, por isso **cada migração tem de funcionar com a versão anterior da API**.
- **Provisório (CLI da Vercel):** o `deploy.yml` corre a CLI a partir da raiz do repositório e não de `frontend`. Com o Root Directory do projeto em `frontend`, a CLI à partida de `frontend` procura `frontend/frontend` e falha (testado com a versão fixada).
- **Provisório (espera pela API):** o `/health` não diz que versão está a correr e, durante um build, o Render continua a servir a instância anterior. Um 200 confirma que a API responde, não que o commit novo já esteja ativo; o site pode ser publicado antes disso.
- **Provisório (keepalive do Supabase):** a frequência (duas vezes por semana) e o que conta como atividade para o Supabase e para o GitHub não estão definidos em número nas documentações; ver a nota sob a T06.
- **Provisório (versão do PostgreSQL):** os testes correm em 18.6 e as páginas lidas do Supabase não dizem que versão dá aos projetos novos. As migrações e o `seed-demo` foram também corridos em 17.

## Diagramas de arquitetura

- **Provisório (`process-runtime` e `logical-layers`):** o pedido de alinhar os diagramas com os do Pokédex manda passar a diagrama de sequência todos os que incluem C4 fora de `c4/`, mas estes dois não são sequências: mostram processos e dependências, sem ordem no tempo. Ficaram como diagramas UML de componentes, com as mesmas caixas e setas, sem nenhum include do C4.

## Melhorias de UX (auditoria)

A auditoria de UX e acessibilidade (`docs/ux-audit.md`, 30 achados em quatro lotes) não é uma história: as correções entram em branches `feat/ux-batch-N-...` próprias, sem mudar regras de negócio nem a API. O estado de cada achado está na secção 9 do relatório.

- **Lote 1, fundações: feito.** Foco visível, contraste nos tokens, foco depois das ações, 404 e páginas de erro em pt-PT, títulos, esqueletos, estados vazios, toast para erros inesperados. Os achados que ficaram para outros lotes estão listados no relatório.
- **Lote 2, fluxo do jogador: feito** (relatório, secção 9): a página do clube com os horários primeiro e, em mobile, uma barra com a ação principal (`position: sticky`, sem dependências), os resultados da pesquisa como ligações inteiras, a lista de reservas por dia, o cabeçalho de mobile com um menu «Conta», o tempo de espera depois de um 429 (`Retry-After`), a introdução na página inicial, e a legenda dos horários. O texto da página do clube diz «por horário» (e não «por slot») ao lado de «horário»; os ecrãs do dono passaram a dizer «horário» no lote 3.
- **Lote 3, área do dono: feito** (relatório, secção 9): o menu «Conta» também no cabeçalho do dono, atalhos «Calendário», «Estatísticas» e «Editar» em cada clube de `/manage`, o calendário numa página mais larga, um campo de cada vez em mobile (seletor «Campo»), os detalhes da reserva ao lado da grelha com Escape e o foco de volta à caixa, nomes das caixas iguais ao texto visível, estatísticas em cartões em mobile, o nome do clube nas páginas, o foco dos formulários, e «horário» em vez de «slot» nos ecrãs do dono (o campo da API continua `pricePerSlot`). **Decidido depois do lote 2:** a altura das reservas em mobile fica como está (215 px) e não há coluna de contactos em desktop.
- **Lote 4, admin e passagem final: feito** (relatório, secção 9 e «Fecho»): gráficos do admin com séries a 3:1 e números por cima das barras, os dados de cada gráfico numa tabela que qualquer pessoa abre, as tabelas do admin em cartões em mobile, nomes dos donos como ligações, a página do dono com esqueletos, a coluna fixa das horas e as colunas flexíveis no calendário, o nome do clube e do dono nos títulos do separador, e a passagem de acessibilidade a todos os perfis (zoom a 200 %, reflow a 320 px, teclado, movimento reduzido). A aplicação não tem tema escuro (os tokens `.dark` existem, mas nada aplica a classe): não se acrescentou. Fica de fora, a decidir: tema escuro, cor de marca, teste com leitor de ecrã e em telemóvel real.
- **Decidido (direção visual):** tema neutro, sem cor de marca por agora; uma introdução curta na página inicial entra no lote 4 (UX-030).
- **Decidido (navegação, lote 3):** atalhos por clube («Calendário», «Estatísticas») em `/manage` e um menu no cabeçalho do dono e do admin (UX-013).
- **Decidido (calendário, lote 3):** em mobile, um campo de cada vez com seletor; em desktop, página mais larga só para o calendário (UX-005).
- **Decidido (página do clube, lote 2):** os slots passam para cima dos contactos; a barra fixa de reserva em mobile só entra se o esforço for S ou M (UX-010).
- **Erro corrigido, US19 e outros ecrãs (UX-019):** os erros inesperados (500, falha de rede, qualquer coisa que não seja uma resposta 4xx da API) iam para uma mensagem na página, por vezes fora de vista, em vez do toast que o AGENTS.md descreve (`isExpectedApiError` e `unexpectedErrorMessage` em `lib/api/expected-error.ts`). Agora vão para o toast; só o login, a recuperação de palavra-passe, a nova palavra-passe e a reserva continuam a descrever «sem ligação» dentro do formulário, de propósito.

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
- Sem nenhum proxy configurado, o `X-Forwarded-For` é ignorado. Em produção é **provisório** (T06, «Pontos a rever no T06»): a API acredita em qualquer ligação, com um limite explícito de entradas, por isso o IP pode ser forjado por quem liga diretamente à API.
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
  - **Proxies de confiança:** por omissão nenhum; o IP é o da ligação. Em produção, **provisório** (T06, «Pontos a rever no T06»): `ForwardedHeaders:TrustAnyProxy` com `ForwardedHeaders:ForwardLimit`, porque os proxies do Render não têm endereços que se possam listar. O contentor do Next.js nunca é de confiança: o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser. Em desenvolvimento todos os pedidos partilham um limite (o do contentor do Next.js), o que chega para um programador.
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
- Mudar a palavra-passe do admin faz-se com o comando `set-admin-password` da API (ver o README, «Changing the admin password»): lê a nova palavra-passe da variável de ambiente `ADMIN_NEW_PASSWORD`, nunca da linha de comandos, valida-a com as regras da US01, atualiza a palavra-passe do admin, termina as sessões dele e nunca escreve o valor em lado nenhum. O procedimento antigo (apagar a conta de admin) já não existe.
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
- Estado: feita no back end (`POST /api/auth/password-reset/request` e `POST /api/auth/password-reset/confirm`, os dois emails); a página do pedido (`/forgot-password`) e a da ligação (`/reset-password`) no front end.
- Decisões de implementação (decididas):
  - O pedido responde sempre 202 com a mesma mensagem, exista ou não a conta; só um email vazio ou mal escrito é um 400 no campo `email` (isso não revela nada). Só se pesquisa a conta e se põe trabalho na fila: não há email nem hash a calcular no pedido.
  - Só um jogador ou dono **ativo** recebe o email. Contas desconhecidas, donos e jogadores desativados e o admin não recebem nada (o admin muda a palavra-passe com `set-admin-password`, US04).
  - No máximo **um email de recuperação por conta a cada 5 minutos**; os pedidos a mais recebem a mesma resposta e não fazem nada. Rate limiting por IP como nas outras rotas de autenticação: **5** pedidos por minuto no pedido e **10** na confirmação (`RateLimiting:Auth:PasswordResetRequestPermitLimit` e `…ConfirmPermitLimit`).
  - A mensagem da fila (`password_reset`) só guarda o id da conta. O token é criado pelo worker quando envia o email, com o fornecedor de tokens de recuperação do Identity (Data Protection) e validade de **1 hora** (`DataProtectionTokenProviderOptions.TokenLifespan`; esse fornecedor usa o relógio do sistema, não o `TimeProvider`). O token inclui o security stamp da conta, por isso deixa de valer quando a palavra-passe muda: cada ligação funciona uma vez, e uma ligação antiga deixa de funcionar quando se usa uma mais recente.
  - A ligação é `{PublicSite:BaseUrl}/reset-password?email=…&token=…` (o email e o token escapados). `PublicSite:BaseUrl` é obrigatório (validado no arranque): `http://localhost:3000` em desenvolvimento, o endereço HTTPS público em produção (T06).
  - A confirmação recebe `{ email, token, newPassword }`. A nova palavra-passe segue a US01 (400 no campo `newPassword`); qualquer ligação má (email desconhecido, token errado, expirado ou já usado, conta desativada ou admin) é o mesmo 400 `invalid_or_expired_reset_link`, sem campos.
  - Se resultar: 204, palavra-passe mudada pelo Identity (novo security stamp), todos os refresh tokens da conta revogados, bloqueio por tentativas limpo, e um email «A tua palavra-passe foi alterada» na fila (`password_changed`, sem ligação). **Não entra automaticamente**: a pessoa inicia sessão com a nova palavra-passe.
  - Nunca vão para os logs o email, o token, a ligação ou a palavra-passe (tem teste).
  - As chaves de Data Protection que protegem os tokens ficam na base de dados (tabela `data_protection_keys`, `PersistKeysToDbContext`, nome de aplicação fixo `SportsBooking`), por isso uma ligação enviada antes de um reinício, de um deploy ou de uma segunda instância continua a funcionar (tem teste). **As cópias de segurança da base de dados guardam também as chaves**: têm de ser tratadas como segredos.
### US30 · Apagar a conta e exportar os meus dados · D · 5 pontos

Como utilizador, quero apagar a minha conta e descarregar os meus dados, para exercer os meus direitos do RGPD sem ter de escrever um email. Enquanto não existir, a Política de Privacidade (T07) indica o pedido por email, tratado à mão.

- Na área da conta, um jogador ou dono pode descarregar os seus dados num formato de uso comum (conta e reservas).
- Pode pedir o apagamento da conta, com confirmação; os dados pessoais deixam de estar associados às reservas, que continuam a existir para os clubes sem identificar a pessoa.
- O que acontece às contas de dono com clubes e às reservas futuras fica decidido antes de começar (hoje as reservas nunca se apagam e um clube não pode ficar sem dono).
- Estado: por fazer (nasceu da T07).
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
- Estado: feita. Back end: faixas, preço único por slot (`Pitch.PriceForSlot`), preço da faixa nas reservas, nos slots públicos e na pré-visualização do dono. Front end: o botão «Faixas de preço» em cada campo de `/manage/clubs/{id}` (ao lado de «Horário» e «Bloqueios», um painel de cada vez) abre a lista das faixas (dias em pt-PT a começar na segunda-feira, horas e preço), o formulário para criar e editar (dias, início, fim, preço; os erros da API vão para o campo certo e a sobreposição aparece por cima dos botões) e a remoção com confirmação; a pré-visualização do dono, o formulário «Nova reserva», a lista de slots do jogador e o resumo da reserva mostram o preço de cada slot, e a página do clube mostra o preço base e uma nota quando o campo tem faixas.
- Decidido (confirmado):
  - Uma faixa tem dias da semana (um ou mais), início e fim em hora local do clube, de meia em meia hora (como a US08), fim `00:00` = meia-noite, sem faixas que passem da meia-noite, e um preço com os limites do preço do campo (> 0, ≤ 500 €, duas casas decimais). Id público GUID v7. Máximo de 20 faixas por campo (422 `price_band_limit_reached`).
  - O preço de um slot (`Pitch.PriceForSlot`) é o da faixa que contém a **hora local de início** do slot nesse dia da semana (início incluído, fim excluído); fora de qualquer faixa, o preço base. Um slot que atravessa o fim de uma faixa (ex.: 90 minutos a partir das 21:00 numa faixa até às 22:00) fica com o preço da faixa onde começa. Na hora repetida de 25 de outubro, os dois slots das 01:00 têm o mesmo preço (conta a hora local).
  - Faixas do mesmo campo não se sobrepõem se partilharem um dia da semana e as horas se cruzarem; faixas que se tocam (uma acaba quando a outra começa) são aceites. Sobreposição: 409 `price_band_overlap`; os outros erros são 400 `validation_failed` com mensagem por campo (`days`, `start`, `end`, `price`). Criar, alterar e apagar correm sob o trinco do campo (`IPitchLock`), o mesmo das reservas.
  - As faixas são configuração, não histórico: apagar é apagar a linha (`DELETE …/price-bands/{bandId}`, 204). Alterar ou apagar faixas nunca toca nas reservas existentes, que guardam o preço com que foram feitas.
  - A pesquisa (US16) continua sem mostrar preços. A página do clube mostra o preço base («Preço base: 18,50 € por horário») e uma nota quando o campo tem faixas (`hasPriceBands`); o preço de cada slot vem em `price` nos slots públicos e na pré-visualização do dono.
  - Os dias da semana viajam na API como nomes em inglês (`monday` … `sunday`), como as chaves do horário; a interface mostra-os em pt-PT, a começar na segunda-feira.
  - Só a sobreposição (409 `price_band_overlap`), o limite (422 `price_band_limit_reached`) e o «não encontrada» (404 `price_band_not_found`) têm código próprio; os erros de campo (dias vazios, fim antes do início, hora fora do passo, preço fora dos limites) são 400 `validation_failed` com a mensagem em `days`, `start`, `end` ou `price`.
  - Texto da página do clube: o preço do campo aparece como «Preço base: 18,50 € por horário» e, por baixo, «Alguns horários têm outro preço; o preço de cada horário aparece na escolha do horário.» quando o campo tem faixas.
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
- Estado: feita. Back end (`GET /api/manage/clubs/{clubId}/calendar` e `GET …/bookings/{bookingId}`) e interface (a vista diária e a semanal, `/manage/clubs/{id}/calendar`).
- Decidido (confirmado):
  - Back end: o intervalo do calendário tem no máximo 7 dias, ambos incluídos; as datas têm de estar entre 2000 e 2100 (limite defensivo); o dono pode ver qualquer data (passado e futuro, sem a janela de 30 dias dos jogadores).
  - Back end: uma reserva que atravessa a meia-noite local aparece nos dois dias.
  - Back end: uma falta (`no_show`) não é um cancelamento e aparece sempre; só as canceladas (pelo jogador ou pelo clube) ficam escondidas sem `includeCancelled=true`.
  - Back end: um campo inativo só vem na lista se tiver reservas visíveis no intervalo (o mesmo critério que decide que reservas se veem).
  - Back end: o nome do jogador é o único dado pessoal que o dono vê, e só nos seus clubes: não há email, telefone nem id da conta (há um teste que procura esses valores na resposta).
  - Interface: a grelha alarga-se (em horas inteiras, dentro do dia) para incluir uma reserva ou um bloqueio fora do horário de abertura, por exemplo de um horário antigo.
  - Interface: reservas e bloqueios que se sobrepõem (por exemplo uma reserva cancelada e a que a substituiu) ficam lado a lado, em faixas.
  - Interface: mudar de dia, de vista, de semana ou de campo fecha a reserva aberta.
  - Interface: um campo que não está no clube, no endereço da vista semanal, conta como o primeiro; um campo inativo que a API devolva aparece marcado «Inativo».
  - Interface: reservas e bloqueios posicionam-se pelo tempo real decorrido desde o início do dia de Lisboa, e não pelas horas do relógio: o dia 25 de outubro de 2026 (25 horas) tem 25 linhas, com «01:00 (verão)» e «01:00 (inverno)», e o dia 29 de março de 2026 (23 horas) tem 23, sem a 01:00. As horas só se mostram por `lib/datetime.ts`. Um horário de abertura que não existe nesse dia avança para depois do salto, como na API (01:30 → 02:30).
  - Interface: numa semana com mudança de hora a coluna desse dia mostra «25 h» (ou «23 h») e uma nota por cima diz que as reservas desse dia estão desenhadas pelo tempo realmente decorrido, por isso depois da mudança ficam uma hora desfasadas da escala das horas (a de um dia normal); a hora escrita em cada reserva é sempre a certa.
- Decidido (confirmado) — decisões da interface:
  - O calendário diário está em `/manage/clubs/{id}/calendar`, com uma ligação «Calendário de reservas» na página do clube. Uma coluna por campo (os que a API devolve) e uma linha por hora, desde a abertura mais cedo até ao fecho mais tarde desse dia, em horas inteiras. Sem nenhum campo aberto nem reservas ou bloqueios, diz-se isso em vez de se mostrar a grelha.
  - Cada reserva mostra o nome do jogador, a hora e o estado («Confirmada», «Cancelada pelo jogador», «Cancelada pelo clube», «Falta»), com um estilo por estado; as canceladas ficam riscadas e a tracejado. Um bloqueio tem um estilo à parte (tracejado, riscas) e mostra «Bloqueio», a hora e o motivo, e não se abre. O que está fora do horário de abertura do campo fica sombreado, e um campo fechado nesse dia diz «Fechado».
  - Navegação: «Dia anterior», «Hoje» (o dia de hoje em Lisboa, não o do navegador), «Dia seguinte» e um campo de data.
  - O estado da vista está no endereço: `?date=AAAA-MM-DD` (hoje não se escreve, por isso «Hoje» é o endereço simples), `&cancelled=1` e `&booking=<id>`. Uma data inválida conta como hoje e um id que não é GUID é ignorado.
  - As reservas canceladas estão escondidas por omissão, com a opção «Mostrar reservas canceladas».
  - Vista semanal («Vista semanal» / «Vista diária»): um campo de cada vez, escolhido numa lista «Campo» (o primeiro por omissão), em sete colunas, de segunda a domingo, com os nomes dos dias em português («Segunda-feira, 04/05/2026»), e linhas por hora, com as mesmas caixas e a mesma regra de posição da vista diária (cada dia pelo tempo real decorrido desde o seu início, em `layoutWeek`, que usa o `layoutDay`). A grelha é partilhada pelas sete colunas, da abertura mais cedo à hora de fecho mais tarde da semana. Um pedido à API por semana (segunda a domingo, 7 dias), com todos os campos; mudar de campo não faz outro pedido.
  - Navegação semanal: «Semana anterior», «Hoje» (a semana de hoje, em Lisboa) e «Semana seguinte»; o campo de data leva à semana que contém essa data. O estado da vista está no endereço: `?view=week&date=AAAA-MM-DD&pitch=<id>&cancelled=1&booking=<id>` (a vista diária é o valor por omissão e não se escreve; o campo só se escreve na vista semanal).
  - Abrir uma reserva (carregar nela) mostra os detalhes por baixo da grelha: jogador, campo, data, hora, preço, estado, quando foi feita e, se estiver cancelada, quando e porquê. «Fechar» fecha. Uma reserva que já não existe diz «Esta reserva já não existe.». Um clube que não é do dono (ou não existe) mostra a mesma página «Clube não encontrado» da edição do clube.
- Decidido (confirmado) — decisões de implementação:
  - `GET /api/manage/clubs/{clubId}/calendar?from=AAAA-MM-DD&to=AAAA-MM-DD&includeCancelled=false` (perfil Dono, pela T02: o clube de outro dono, ou inexistente, é 404 `club_not_found`, antes de se olhar para a query). `from` e `to` são datas locais do clube, ambas incluídas; `from` não pode ser depois de `to` (400 em `from`/`to`/`includeCancelled`, mensagens em português).
  - A resposta tem `from`, `to`, `pitches`, `bookings` e `blocks`. Os campos do clube: os campos que a API lista, ordenados pelo nome; cada um com `id`, `name`, `sport`, `status`, `slotDurationMinutes` e `days` (uma entrada por data do intervalo, com `opens` e `closes` locais «HH:mm», ou `null` num dia fechado; fecho «00:00» é meia-noite). Os instantes das reservas e dos bloqueios vão em UTC; a interface mostra-os em hora de Lisboa.
  - Uma reserva pertence ao intervalo se se sobrepuser a ele (meio-aberto), e um dia é o dia local: a 25 de outubro de 2026 (25 horas) o intervalo é [2026-10-24T23:00Z, 2026-10-26T00:00Z), e as duas 01:30 contam.
  - Reservas: `id`, `pitchId`, `startsAt`, `endsAt`, `status`, `price` e `bookerName`, por ordem de início. As canceladas (pelo jogador ou pelo clube) só vêm com `includeCancelled=true`. Bloqueios: `id`, `pitchId`, `startsAt`, `endsAt`, `reason`, dos campos listados.
  - Jogadores e admins recebem 403 nestes endpoints, e sem sessão 401.
  - `GET /api/manage/clubs/{clubId}/bookings/{bookingId}`: os detalhes (campo, desporto, início, fim, preço, estado, `bookerName`, `createdAt`, `cancelledAt`, `cancellationReason` e `canCancel`, verdadeiro para uma reserva confirmada que ainda não começou). Uma reserva de outro clube, mesmo do mesmo dono, e um id desconhecido são o mesmo 404 `booking_not_found`.
  - Feito na US13: para uma reserva que o dono fez para um cliente sem conta, `bookerName` traz o nome do cliente e `createdByClub` é verdadeiro (a interface ainda não mostra a marca). O telemóvel só vem nos detalhes.
### US12 · Cancelar reserva como dono · M · 2 pontos
 
Como dono, quero cancelar uma reserva, para resolver problemas como um campo avariado.
 
- O dono pode cancelar qualquer reserva futura dos seus clubes, sem o limite da janela de cancelamento.
- É obrigatório indicar um motivo.
- A reserva passa a «Cancelada pelo clube» (não é apagada) e o slot volta a ficar livre.
- O jogador recebe um email com o motivo (US22).
- Estado: feita. Back end (`POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`) e a ação nos detalhes da reserva do calendário.
- Decidido (confirmado):
  - Corpo `{ "reason": "…" }`: o motivo é obrigatório, sem espaços nas pontas, com no máximo 200 caracteres (400 em `reason`).
  - Ordem das respostas: clube de outro dono 404 `club_not_found` → reserva que não é deste clube 404 `booking_not_found` (as duas antes de validar o corpo) → 400 → 422 `booking_not_confirmed` (já cancelada, pelo jogador ou pelo clube, ou falta; verifica-se primeiro) → 422 `booking_already_started` (já começou; começar agora conta como começada).
  - O cancelamento feito pelo jogador (US19) também guarda o instante (`cancelled_at`).
- Decidido (confirmado) — decisões da interface:
  - Nos detalhes, uma reserva que a API diz poder cancelar (`canCancel`: confirmada e ainda não começou) tem o botão «Cancelar reserva»; o ecrã nunca o calcula. Abre um formulário com «Motivo do cancelamento» (obrigatório, até 200 caracteres, com a mesma mensagem da API) e «Confirmar cancelamento» / «Manter reserva». Uma reserva confirmada que já começou diz «Esta reserva já começou, por isso já não pode ser cancelada.» e não tem botão.
  - Depois de cancelar diz «Reserva cancelada. O horário voltou a ficar livre.», os detalhes passam a mostrar o estado, a hora e o motivo, e o calendário recarrega-se (a reserva sai da grelha, a menos que as canceladas estejam à vista). Não se promete nenhum email: o aviso ao jogador é da US22.
  - As recusas da API mostram a mensagem do código (422 `booking_not_confirmed`, 422 `booking_already_started`, 404 `booking_not_found`), fecham o formulário e recarregam a reserva e o calendário; a mensagem fica mesmo que a reserva recarregada já não ofereça o formulário. Um 400 em `reason` vai para o campo do motivo.
- Decidido (confirmado) — decisões de implementação:
  - Não há janela de cancelamento para o clube: pode cancelar até ao instante em que a reserva começa.
  - Corre sob o trinco do campo (o mesmo das reservas, dos bloqueios e do cancelamento do jogador) e lê a reserva outra vez lá dentro; por isso, se o dono e o jogador cancelam ao mesmo tempo, só um tem sucesso e o outro recebe 422 `booking_not_confirmed`, nunca um 500. A regra está na entidade (`Booking.CancelByClub`).
  - A reserva passa a `cancelled_by_club` e guarda o motivo (`cancellation_reason`) e o instante (`cancelled_at`); nunca é apagada e o slot volta a ficar livre. A resposta é a reserva, como nos detalhes.
### US13 · Reserva em nome de um cliente · M · 3 pontos
 
Como dono, quero criar uma reserva para um cliente que ligou ou apareceu, para que todas as reservas fiquem no sistema.
 
- O dono escolhe campo e slot livre e indica nome e telemóvel do cliente; o cliente não precisa de conta.
- Aplica-se a mesma regra contra reservas duplicadas da US18 e da T03.
- A reserva aparece no calendário marcada como «criada pelo clube».
- Não é enviado email, porque o cliente não tem conta.
- Estado: feita. Back end (`POST /api/manage/clubs/{clubId}/bookings`) e interface no calendário do dono (vista diária e semanal).
- Decidido (confirmado):
  - O telemóvel do cliente é obrigatório (na US01 é opcional).
  - A janela de 30 dias (hoje até hoje + 30, em Lisboa) aplica-se também ao dono: uma reserva por telefone para daqui a mais de 30 dias é 422 `outside_booking_window`.
  - Um dono desativado com a sessão ainda válida recebe 422 `club_unavailable` ao criar uma reserva.
  - O telemóvel que o dono escreveu só se mostra nos detalhes da reserva, nunca no calendário.
  - Interface: as horas vazias só se oferecem num campo ativo, dentro do horário de abertura, numa hora que ainda não acabou e até 30 dias à frente; são botões fora da ordem do teclado, e o botão «Nova reserva» é a entrada por teclado.
  - Interface: uma data fora de hoje..+30 volta a hoje no formulário.
  - Interface: abrir o formulário fecha os detalhes da reserva aberta.
  - Interface: a verificação de que um horário já começou usa o instante em que a página foi aberta; quem decide, no fim, é a API.
- Decidido (confirmado) — decisões da interface:
  - Há duas entradas para o formulário «Nova reserva» no calendário: o botão «Nova reserva» (campo e dia que se estão a ver; na vista semanal, o campo escolhido) e carregar numa hora vazia de um campo (essa hora, esse campo e esse dia, com o slot já escolhido se estiver livre). O botão fica desativado se o clube não tiver campos ativos.
  - O formulário pede campo (só os ativos), data (de hoje a 30 dias à frente, em Lisboa), um horário livre, o nome e o telemóvel do cliente. Os horários vêm da pré-visualização do dono (`GET …/pitches/{pitchId}/slots`): só se escolhem os livres; os reservados («Reservado»), os bloqueados («Bloqueado») e os que já começaram («Já começou», incluindo o que começa agora) aparecem desativados. Mudar o campo ou a data limpa o horário escolhido.
  - As validações são as da API, em português: horário («Escolhe um horário livre.»), nome obrigatório e até 100 caracteres, telemóvel obrigatório com 9 a 15 algarismos e «+» opcional (os espaços tiram-se antes de enviar, como na API). Os erros 400 da API vão para os campos.
  - Num conflito (409 `slot_taken` ou `slot_blocked`) ou num horário que já não existe (422 `invalid_slot`, `slot_started`, `outside_booking_window`, `pitch_inactive`, 404 `pitch_not_found`) mostra-se a mensagem do código no formulário, limpa-se o horário, recarregam-se os horários e o calendário, e o formulário fica aberto com o nome e o telemóvel já escritos. Qualquer outro erro vai para um aviso.
  - Depois de criar, o calendário vai para o dia da reserva (e, na vista semanal, para o campo dela), mostra-a com os detalhes abertos, e diz «Reserva criada para <nome>.». O formulário diz que o cliente não tem conta e por isso não recebe email.
  - Uma reserva criada pelo clube diz «Criada pelo clube» nas duas vistas (no quadrado da reserva, com uma barra lateral azul, e na etiqueta para leitores de ecrã) e nos detalhes, onde «Jogador» passa a «Cliente» e aparecem o «Telemóvel» que o dono escreveu e a «Origem: Criada pelo clube». Cancela-se como qualquer reserva (US12), com motivo, e continua a dizer de onde veio.
- Decidido (confirmado) — decisões de implementação:
  - Uma reserva é de um jogador ou de um cliente (nome e telemóvel), nunca das duas coisas nem de nenhuma: a check constraint `ck_bookings_player_or_customer` substitui a `ck_bookings_has_player` (migração `AddCustomerBookings`, colunas `customer_name` e `customer_phone`). A exclusion constraint `bookings_no_overlap` não mudou, por isso um cliente e um jogador disputam os mesmos slots.
  - `POST /api/manage/clubs/{clubId}/bookings` (perfil Dono, pela T02: o clube de outro dono é 404 `club_not_found`, antes de validar o corpo) com `{ pitchId, startsAt, customerName, customerPhone }`. `startsAt` é o instante do início do slot com fuso, como as listas de slots o dão; o fim e o preço vêm do campo, nunca do pedido. 201 com a reserva (a mesma resposta dos detalhes) e `Location: /api/manage/clubs/{clubId}/bookings/{id}`, que `GET …/bookings/{id}` resolve.
  - Nome e telemóvel seguem as regras da US01: o nome é obrigatório e tem no máximo 100 caracteres; o telemóvel tem um «+» opcional e 9 a 15 algarismos e é guardado sem espaços (400 em `customerName` e `customerPhone`, mensagens em português).
  - As regras do slot são as da US18, pelo mesmo código (`BookingSlotCheck`, que a US18 também usa) e sob o mesmo trinco do campo: campo inativo 422 `pitch_inactive` → data fora de hoje..+30 em Lisboa 422 `outside_booking_window` → não é um slot 422 `invalid_slot` → já começou 422 `slot_started` → bloqueio 409 `slot_blocked`; outra reserva confirmada que se sobreponha é 409 `slot_taken` (a exclusion constraint, nunca um 500). Um campo que não é deste clube é 404 `pitch_not_found`. A ordem completa: clube (404) → validação (400) → campo (404) → dono ativo → trinco → regras do slot → inserção.
  - O preço é o do campo no momento e fica guardado; mudar o preço depois não o altera. Não é enviado email (o cliente não tem conta), e a US22 diz isso.
  - O calendário (`bookerName` e `createdByClub`) e os detalhes (`bookerName`, `createdByClub` e `customerPhone`) mostram estas reservas com o nome do cliente e a marca de criada pelo clube. `bookerName` é o nome de quem fez a reserva: o do jogador, ou o do cliente quando a reserva é do clube.
  - O dono cancela-as como na US12 (`…/bookings/{id}/cancel`, com motivo) e o slot volta a ficar livre.
  - Nunca aparecem em lista nenhuma de um jogador (`GET /api/me/bookings` só lê reservas com o `player_id` de quem pede), um jogador que as peça ou cancele por id recebe 404 `booking_not_found`, as páginas públicas só mostram o slot como indisponível (sem nome, telemóvel nem id), e o admin não tem acesso aos endpoints do dono (403).
### US14 · Ocupação e receita · D · 5 pontos
 
Como dono, quero ver a ocupação e a receita dos meus clubes por período, para perceber como está o negócio.
 
- Filtros por clube, campo e intervalo de datas.
- Ocupação = horas reservadas a dividir pelas horas abertas no período, por campo.
- Receita = soma dos preços das reservas não canceladas.
- Os números batem certo com o calendário para o mesmo período (verificado por teste).
- Estado: feita. Back end (`GET /api/manage/clubs/{clubId}/statistics?from=&to=&pitchId=` e `GET /api/manage/statistics?from=&to=`, «por clube») e ecrãs (`/manage/statistics` e `/manage/clubs/{id}/statistics`).
- Decisões de implementação (decididas):
  - **Um só cálculo** para o dono e para o admin (US27), `OccupationService`. Horas reservadas = tempo real das reservas **confirmadas e faltas** que se sobrepõem ao período, cortadas nos limites do período; as canceladas (pelo jogador ou pelo clube) ficam de fora. Receita = soma dos preços guardados dessas mesmas reservas, **por inteiro** (uma reserva cortada no limite do período conta o preço todo).
  - Horas abertas = o tempo reservável: para cada dia de Lisboa, os slots que o horário **em vigor agora** gera (`SlotGenerator`) vezes a duração do slot, em tempo real (não há histórico de horários). Um campo aberto 00:00–00:00 tem 25 slots de 60 minutos a 25/10/2026 e 23 a 29/03/2026; um dia fechado conta 0; os minutos depois do último slot que cabe não se podem reservar e não contam (um campo de 90 minutos aberto 4 h tem 3 h abertas).
  - Um campo **inativo** não tem horas abertas: entra só com as reservas que já tem no período, e não aparece se não tiver nenhuma.
  - Ocupação = reservadas / abertas, 0 quando não houve nada aberto; arredondada a 4 casas (0,8125 = 81,25 %), horas a 2 casas. Os totais somam minutos e dividem no fim, nunca fazem a média das taxas.
  - Período: datas de Lisboa `AAAA-MM-DD`, as duas incluídas; sem datas, os últimos 30 dias (hoje incluído); `from` ≤ `to`; no máximo **366 dias**; datas entre 2000 e 2100; erros 400 por campo em pt-PT. O filtro `pitchId` tem de ser um campo do clube (404 `pitch_not_found`).
  - As reservas somam-se na base de dados (uma consulta agrupada por campo, com `LEAST`/`GREATEST`); só os horários (poucas linhas por campo) são lidos para memória.
  - A lista «por clube» tem todos os clubes do dono, também os que não tiveram nada aberto nem reservado (com zeros).
### US15 · Marcar falta · D · 2 pontos
 
Como dono, quero marcar uma reserva passada como falta, para saber que jogadores não aparecem.
 
- Só se podem marcar reservas confirmadas cuja hora de fim já passou.
- O estado passa a «Falta» e a marcação pode ser revertida.
- O dono vê quantas faltas um jogador tem nos seus clubes.
- Estado: feita. Back end (`POST /api/manage/clubs/{clubId}/bookings/{bookingId}/no-show` e `…/no-show/undo`) e, nos detalhes da reserva do calendário, os botões «Marcar falta» e «Anular falta» e o número de faltas do jogador.
- Decisões de implementação (decididas):
  - Marcar: só uma reserva **confirmada** cuja hora de fim já passou (a hora de fim conta como passada). Primeiro 422 `booking_not_confirmed` (cancelada, ou já é uma falta), depois 422 `booking_not_ended`. Reverter: a falta volta a `confirmed`; uma reserva que não é falta é 422 `booking_not_no_show`. Dono de outro clube, ou reserva de outro clube: 404 (T02); jogador e admin: 403.
  - Tudo corre sob o `IPitchLock` do campo, como os cancelamentos, e lê a reserva outra vez lá dentro. Se ao reverter outra reserva confirmada já ocupou o horário (uma falta não o ocupa), a constraint `bookings_no_overlap` recusa e a resposta é **409 `slot_taken`**, nunca 500; o pedido não deixa nada a meio.
  - Pode marcar-se uma reserva de cliente (US13), mas não conta por pessoa: `playerNoShows` é `null` nessas reservas.
  - Os detalhes da reserva do dono ganham `playerNoShows` (as faltas do jogador em **todos** os clubes desse dono, a reserva incluída; nunca as de outro dono), `canMarkNoShow` (confirmada e já terminou) e `canUndoNoShow` (é uma falta). O dono continua a ver só o nome do jogador.
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
- Decidido (confirmado) — decisões de implementação:
  - Visível = o dono não está desativado (US29) e o clube tem pelo menos um campo ativo (US07). Tudo o que é público parte de `VisibleClubs`; o que não é visível não existe: um clube desconhecido, sem campos ativos ou de um dono desativado dão o mesmo 404 `club_not_found`.
  - `GET /api/clubs?sport=&locality=&date=&page=`: 20 por página (fixo), ordenado pelo nome sem distinguir maiúsculas e depois pelo id. `date` é `AAAA-MM-DD`, hoje em Lisboa por omissão, de hoje até hoje + 30; fora disso ou mal escrita é 400 em `date`. Filtros vazios contam como ausentes.
  - Cada resultado traz só `slug`, `name`, `locality`, `sports` (os desportos dos campos ativos) e `hasFreeSlots` (algum campo ativo tem um slot livre na data, pela mesma regra da US17). Não traz morada nem contactos. A disponibilidade só se calcula para os clubes da página.
  - `GET /api/clubs/{slug}`: nome, morada, localidade, telefone, email de contacto e os campos ativos (`id`, `name`, `sport`, `pricePerSlot` atual), ordenados pelo nome. O `slug` tem de coincidir exatamente. O id do clube não aparece (é da área do dono).
- Decidido (confirmado) — decisões da interface:
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
- Decidido (confirmado) — decisões de implementação:
  - A resposta é `date` e `slots`; cada slot traz `startsAt` e `endsAt` (instantes UTC com fuso), `price` (o preço atual do campo) e um só booleano `available`. Nunca diz porque não está livre, nem quem reservou, nem o id de uma reserva ou de um bloqueio. Um dia fechado devolve a lista vazia.
  - `available` = não se sobrepõe, nem em parte, a uma reserva confirmada ou a um bloqueio, está dentro do horário e ainda não começou (começar agora conta como começado, como na US18). É uma só conta (`PitchDayCalendar` e `DaySlot.IsFreeAt`), partilhada com a pré-visualização do dono e com o «há slots livres» da pesquisa, e um teste confirma que concordam.
  - Em 25 de outubro a API devolve os slots como instantes distintos (a hora das 01:00 às 02:00 aparece duas vezes, uma em cada fuso); a etiqueta «(verão)/(inverno)» é da interface.
- Decidido (confirmado) — decisões da interface:
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
- Estado: feita. Back end com a T03 (`POST /api/bookings`, perfil Jogador); interface na página do clube, com a US16 e a US17. O email de confirmação é da US22; ver as minhas reservas e cancelar é a US19.
- Decidido (confirmado):
  - O pedido traz o campo e o início do slot como instante com fuso (`2026-10-25T00:00:00Z`, como as listas de slots o dão), para a hora repetida de outubro nunca ser ambígua.
  - Códigos: 404 `pitch_not_found` (campo desconhecido); 422 `club_unavailable` (dono desativado), `pitch_inactive`, `outside_booking_window` (data local fora de hoje até 30 dias em Lisboa, incluindo dias passados), `invalid_slot` (um início que não seja um slot gerado: dia fechado, fora do horário, fora da grelha, segundos a mais), `slot_started`; 409 `slot_blocked` (um bloqueio sobrepõe-se, mesmo em parte) e `slot_taken` («Este horário acabou de ser reservado. Escolhe outro.»). Os 422 são verificados por esta ordem; os 409 só depois de trancar a linha do campo.
  - Um slot que começa exatamente agora conta como começado (`slot_started`).
  - A desativação de um campo conta as reservas confirmadas que ainda não acabaram: as que estão a decorrer também impedem.
  - Os estados na API são `confirmed`, `cancelled_by_player`, `cancelled_by_club` e `no_show`; na base de dados ficam com o nome do enum (`Confirmed`, …), como pede a constraint.
  - A resposta 201 traz o cabeçalho `Location: /api/bookings/{id}` (relativo, como os outros), que `GET /api/bookings/{id}` resolve (US19).
- Decidido (confirmado) — decisões de implementação:
  - O fim vem sempre do horário do campo e o preço é o preço por slot atual do campo.
  - A transação tranca a linha do campo, volta a ler o campo (estado, preço e horário como estão nesse momento), verifica os bloqueios e insere; a sobreposição com outra reserva fica só a cargo da exclusion constraint (23P01 → 409 `slot_taken`), que é a última defesa e a regra também para a US13.
  - A resposta 201 traz a reserva (`id`, `pitchId`, `clubId`, início e fim em UTC, `price`, `status` = `confirmed`).
  - Preparado para a US13 e feito nela: `player_id` pode ser nulo na tabela, e a check constraint `ck_bookings_player_or_customer` exige «um jogador, ou o nome e o telemóvel de um cliente», nunca as duas coisas nem nenhuma (antes era a `ck_bookings_has_player`).
- Decidido (confirmado): a interface da reserva
  - O que o jogador escolhe (campo, data e slot) fica no endereço (`?pitch=&date=&slot=<instante com fuso>`), não numa store Zustand: é isso que permite voltar ao mesmo slot depois do login e partilhar a ligação. Escolher um slot livre abre, por baixo da lista, o «Resumo da reserva» (clube, campo, data, hora e preço); «Cancelar» fecha-o, e mudar de campo ou de data também.
  - Sem sessão, o resumo oferece «Entrar para reservar», que vai para `/login?next=<este endereço com campo, data e slot>`; depois do login o jogador volta ao mesmo clube, campo, data e slot, com o resumo aberto. O `safeNext` não precisou de mudar: já aceita uma página pública com query para um jogador, e manda um dono ou um admin para a sua área, porque não podem reservar. Se o slot entretanto deixou de estar livre (ou já não existe), aparece «Este horário já não está disponível. Escolhe outro.» e não há resumo.
  - Só um jogador vê «Confirmar reserva». Donos e admins veem os slots mas não os podem escolher, nem há botão de reservar, e uma nota diz «Só os jogadores podem fazer reservas. Entra com uma conta de jogador para reservar.»
  - Em 409 (`slot_taken`, `slot_blocked`) ou 422 (`slot_started`, `invalid_slot`, `outside_booking_window`, `pitch_inactive`, `club_unavailable`) mostra-se a mensagem desse código («Este horário acabou de ser reservado. Escolhe outro.» para `slot_taken`), fecha-se o resumo e recarrega-se a lista de slots. Um 401 (sessão terminada) volta ao login, com regresso ao slot; sem ligação ao servidor mantém-se o resumo para tentar de novo.
  - Para um visitante, `club_not_found` diz «Este clube já não está disponível.» (a mensagem geral fala a um dono).
- Decidido (confirmado) — decisões da interface:
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
- Decidido (auditoria de UX, lote 1): um erro inesperado ao cancelar (500 ou falha de rede) mostra um toast, não uma mensagem no topo da lista; o resultado do cancelamento (sucesso ou recusa da API) recebe o foco, por isso fica à vista e é lido uma só vez pelo leitor de ecrã. Não é um aviso ao vivo.
- Decidido (confirmado):
  - `scope` é obrigatório na lista das minhas reservas: em falta ou desconhecido é 400 em `scope`.
  - «Anteriores» são as reservas que já acabaram mais as canceladas (pelo jogador ou pelo clube) e as marcadas como falta, da mais recente para a mais antiga; «Próximas» são as confirmadas que ainda não acabaram, da mais antiga para a mais recente, e uma reserva em curso conta como próxima. Em ambas a ordem é pelo início e depois pelo id.
  - O prazo de cancelamento (`cancellationDeadline`) só existe para reservas confirmadas; nas outras é `null`.
  - No instante exato do prazo já não se pode cancelar: só é possível estritamente antes dele.
  - `booking_not_confirmed` verifica-se antes da janela, por isso a resposta de uma reserva já cancelada não depende do relógio.
  - Cancelar responde 200 com a reserva já cancelada (`cancelled_by_player`).
  - As reservas de um clube que entretanto ficou escondido continuam na lista do jogador.
  - «Próximas» e «Anteriores» são duas ligações numa navegação (com `aria-current`, e cada uma com o seu endereço; antes tinham o papel de separadores, sem se comportarem como tal). A lista agrupa as reservas por dia, com o dia como título («Hoje · …», «Amanhã · …»), e cada reserva é uma linha compacta (hora e clube, depois campo, preço e série). Uma recusa do cancelamento aparece na linha da reserva; o sucesso aparece por cima da lista, na mesma região, e recebe o foco (auditoria de UX, lote 2).
  - O separador e a página da lista ficam no endereço (`?tab=past&page=2`; «Próximas» e a página 1 são o valor por omissão), por isso um recarregamento ou o botão de voltar mantêm o sítio.
  - O botão «Cancelar reserva» só segue o `canCancel` da API: quem decide se ainda se pode cancelar é a API, nunca o cálculo no navegador.
  - A mensagem do 422 `outside_cancellation_window` não leva o prazo (o front end só vê o código); o prazo vê-se na lista, que se recarrega logo a seguir.
- Decidido (confirmado) — decisões da interface:
  - A página `/bookings` é só para jogadores: sem sessão vai para `/login?next=<a página, com o separador e a página>` e volta a ela depois de entrar; um dono ou um admin vai para a sua área (`/manage`, `/admin`). A API continua a ser quem decide (401/403).
  - Mudar de separador volta à primeira página. Uma página do endereço que não é um número de 1 a 99999 conta como a primeira.
  - Cada reserva mostra o clube (ligação para `/clubs/{slug}`), o campo e o desporto, a data (com o dia da semana e o ano) e a hora em hora de Lisboa (`lib/datetime.ts`, com «(verão)»/«(inverno)» na hora repetida de outubro), o preço pago e o estado em português: «Confirmada», «Cancelada pelo jogador», «Cancelada pelo clube», «Falta».
  - Só as «Próximas» se podem cancelar (uma reserva confirmada que já acabou, em «Anteriores», não tem botão). O botão «Cancelar reserva» pede uma confirmação na própria linha («Sim, cancelar reserva» / «Manter reserva»; só uma de cada vez). Depois do prazo o botão fica desativado e diz «Já não é possível cancelar: o prazo terminou a dd/mm/aaaa hh:mm.»; antes do prazo diz «Podes cancelar até dd/mm/aaaa hh:mm.».
  - Depois de cancelar aparece «Reserva cancelada. O horário voltou a ficar livre.» e recarregam-se as listas e os horários livres das páginas públicas (`["my-bookings"]` e `["catalog"]`). Se o pedido for recusado (422 `outside_cancellation_window`, 422 `booking_not_confirmed`, 404 `booking_not_found`) mostra-se a mensagem do código e recarrega-se só a lista; uma sessão que acabou leva ao login. Uma reserva nova (US18) também recarrega «As minhas reservas».
  - O cabeçalho do jogador tem a ligação «As minhas reservas» (só para o perfil Jogador) e a confirmação de uma reserva tem «Ver as minhas reservas».
- Decidido (confirmado) — decisões de implementação:
  - `GET /api/me/bookings?scope=upcoming|past&page=` (perfil Jogador, só as reservas de quem pede, 20 por página). `page` de 1 a 10000, e não numérica também é 400 em `page` (mensagem em português).
  - Cada reserva traz `id`, `clubName`, `clubSlug`, `pitchName`, `sport`, `startsAt`, `endsAt`, `price` (o que foi pago, não o preço atual do campo), `status`, `canCancel` e `cancellationDeadline` (instante UTC). `canCancel` é verdadeiro se a reserva está confirmada e agora é antes do início menos a janela atual do clube.
  - `GET /api/bookings/{id}` lê uma reserva própria; a de outro jogador e um id desconhecido são o mesmo 404 `booking_not_found`.
  - `POST /api/bookings/{id}/cancel` (perfil Jogador). A reserva nunca é apagada e o slot volta a ficar livre, porque a exclusion constraint só olha para as confirmadas. Ordem das recusas: 404 `booking_not_found` → 422 `booking_not_confirmed` → 422 `outside_cancellation_window`, com `cancellationDeadline` (instante UTC) e a janela em horas na mensagem.
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
- Estado: feita. Back end: `POST /api/bookings/recurring/preview` e `POST /api/bookings/recurring`. Front end: no resumo da reserva (depois de escolher um slot livre) a opção «Repetir semanalmente» com 1 a 4 semanas (1 = sem repetição; a escolha vai no endereço, `?weeks=`, por isso o regresso depois de entrar a mantém); com mais de uma semana mostra cada data (hora de Lisboa e preço) como livre ou indisponível com o motivo em pt-PT, e «Confirmar N reservas» reserva as livres; o resultado mostra o que foi reservado e o que ficou de fora, com a ligação para «As minhas reservas», e quando nenhuma data se reserva mostra a mensagem e as datas pedidas com o motivo. Um visitante escolhe as semanas e entra; só o jogador vê as datas. Em «As minhas reservas» cada reserva de uma série mostra «Reserva semanal: 2 de 4».
- Decidido (confirmado):
  - `weeks` é o número de datas, a primeira incluída, de 1 a 4. A data n é o mesmo dia da semana e a mesma hora **local** n semanas depois (`ClubTime`), por isso uma mudança de hora não desloca a hora do jogo; a primeira data é exatamente o instante pedido (na hora repetida de outubro, o verão ou o inverno escolhido).
  - A pré-visualização só aceita um campo que o público vê (ativo, de um clube visível); outro é 404 `pitch_not_found`. Cada data traz início, fim, preço (`PriceForSlot` dessa data) e `free` ou `unavailable` com um código: `taken`, `blocked`, `not_a_slot`, `outside_window`, `closed` (o campo está fechado nesse dia), `pitch_inactive` e, além da lista pedida, `started` (só a primeira data pode já ter começado). Nunca diz quem reservou.
  - Reservar corre, data a data e por ordem, as mesmas regras da reserva simples (`BookingSlotCheck` sob o trinco do campo, preço de `PriceForSlot`, inserção pela restrição de exclusão), cada data na sua transação: uma data recusada não impede as outras. As reservas feitas partilham um `series_id`; uma data perdida para a restrição (23P01) fica de fora como `taken`. O campo desconhecido é 404 e o clube de um dono desativado é 422 `club_unavailable`, para o pedido todo, como na reserva simples.
  - Resposta 201 com as reservas feitas e as datas de fora (com o código) quando se fez pelo menos uma; 409 `series_unavailable` (com `skipped`) quando nenhuma. O `Location` aponta para a primeira reserva feita.
  - Emails: um só resumo da série para o jogador (as reservas feitas, com data, hora de Lisboa e preço, e as datas de fora com o motivo), numa transação final depois das datas (se o processo cair entre a última reserva e esse passo, perde-se o resumo, nunca uma reserva); nenhuma confirmação por reserva. O email ao dono (US23) e o lembrete (US21) continuam a ser um por reserva feita, na transação dessa reserva.
  - A lista do jogador e o detalhe de uma reserva trazem `series` (id, posição e total), contando só as reservas que a série fez, canceladas incluídas, por ordem de início («2 de 4»). Cancelar continua a ser por reserva (US19).
### US21 · Lembrete antes da reserva · D · 3 pontos
 
Como jogador, quero receber um lembrete antes da reserva, para não me esquecer.
 
- É enviado um email 3 horas antes do início.
- Reservas canceladas, ou feitas a menos de 3 horas do início, não geram lembrete.
- Cada reserva gera no máximo um lembrete, mesmo que o serviço reinicie.
- Estado: feita no back end (outbox e worker, sem interface nova).
- Decisões de implementação (decididas):
  - O lembrete entra na fila na mesma transação da reserva (a de um jogador), com a hora de envio `início − 3 h` (um instante: a mudança da hora de 25 de outubro não o desloca; um teste cobre o 01:00 de inverno desse dia). Só se esse momento ainda estiver no futuro: uma reserva feita a **menos de 3 horas, ou a exatamente 3 horas**, do início não gera lembrete (o caso exato é provisório). Reservas de cliente não geram.
  - A mensagem só guarda o id da reserva; o conteúdo (clube, morada, campo, data, hora de Lisboa, «(verão)»/«(inverno)») lê-se no momento do envio, para o email dizer o que é verdade nessa altura. O destinatário é o email atual do jogador.
  - Uma reserva feita a menos de 3 horas do início não recebe lembrete **atrasado**: simplesmente não tem lembrete.
  - Quando chega a hora, o worker só envia se a reserva **ainda está confirmada** e ainda não começou; senão marca a mensagem como **ignorada** (`skipped_at`, com o motivo `booking_not_confirmed` ou `booking_started`, sem contar como falha nem envio) e não envia nada.
  - Um máximo de um lembrete por reserva é garantido por um índice único (`ux_outbox_messages_booking_notification`, por tipo e id da reserva), por isso nem um bug nem um reinício o duplicam; uma mensagem enviada nunca volta a ser reclamada.
## Épico 5 — Notificações
 
### US22 · Emails ao jogador · M · 3 pontos
 
Como jogador, quero receber um email quando reservo e quando uma reserva minha é cancelada, para ter confirmação.
 
- O email de confirmação traz clube, morada, campo, data, hora e preço.
- O email de cancelamento indica quem cancelou e, se foi o clube, o motivo.
  - O cancelamento feito pelo jogador (US19, `POST /api/bookings/{id}/cancel`) só muda o estado da reserva: o email a avisar vem aqui, em segundo plano, sem fazer falhar o cancelamento.
  - Uma reserva que o dono cria para um cliente (US13) não gera email de confirmação, e o seu cancelamento pelo dono também não: o cliente não tem conta nem email, só um telemóvel.
  - O cancelamento feito pelo dono (US12, `POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`) guarda o motivo e a hora (`cancellation_reason`, `cancelled_at`) e só muda o estado da reserva: o email com o motivo vem aqui, em segundo plano, sem fazer falhar o cancelamento.
- O envio é feito em segundo plano; uma falha no envio não falha a reserva nem o cancelamento e fica registada no log.
- Em desenvolvimento, os emails vão para um serviço de teste (por exemplo, Mailpit) e nunca para endereços reais.
- Estado: feita no back end (outbox, worker em segundo plano e SMTP). Não há interface: os emails vão para o Mailpit em desenvolvimento (http://localhost:8025).
- Decidido (confirmado):
  - Valores por omissão do worker (definições `Outbox`): lotes de 20, uma passagem a cada 10 segundos (logo a seguir se o lote veio cheio), a primeira nova tentativa 1 minuto depois, duplicando até um máximo de 1 hora (1, 2, 4, 8, 16 minutos…), e desiste-se ao fim de 6 tentativas falhadas: a mensagem fica com `failed_at` e é registado um Warning com o id e o tipo, sem o endereço. Uma mensagem para uma conta que já não existe desiste logo (`recipient_not_found`).
  - A entrega é «pelo menos uma vez»: se o processo parar depois de enviar e antes de registar o envio, essa mensagem volta a sair. Os trincos das linhas do lote ficam presos enquanto o lote é enviado.
  - Não há ligações para a aplicação nos emails até à T06 (falta o endereço público).
  - A API não arranca sem `Email:Host` e `Email:FromAddress`.
- Decidido (confirmado) — decisões de implementação:
  - Tabela `outbox_messages` (migração `AddOutboxMessages`): `id`, `type` (`booking_confirmed`, `booking_cancelled_by_player`, `booking_cancelled_by_club`), `payload` (jsonb, só o que o email mostra: o id do jogador, o nome, a morada e a localidade do clube, o fuso, o campo, o início, o fim, o preço e, numa cancelação do clube, o motivo), `created_at`, `attempts`, `next_attempt_at`, `sent_at`, `failed_at` e `last_error` (o nome do tipo da exceção, ou `recipient_not_found`; nunca a mensagem da exceção, que pode trazer o endereço). O endereço e o nome do jogador não ficam na tabela: leem-se no momento do envio, por isso o email vai sempre para o endereço atual.
  - A mensagem é escrita no mesmo `SaveChanges` e na mesma transação (a do trinco do campo) que a reserva ou o cancelamento: uma reserva que perde a corrida (23P01) ou qualquer recusa não deixa email nenhum, e de vários cancelamentos simultâneos só o que vence deixa um. As reservas para clientes sem conta (US13) não geram email, nem a criar nem a cancelar.
  - O envio é feito por um `BackgroundService` dentro da API, que nunca toca nos pedidos: uma falha ou uma lentidão do SMTP não falha nem atrasa uma reserva ou um cancelamento. Cada passagem cria o seu scope, tira até um lote de mensagens vencidas com `SELECT … FOR UPDATE SKIP LOCKED` (dentro de uma transação que dura até o lote ficar registado), por isso duas instâncias da API nunca enviam a mesma mensagem, e envia-as por SMTP (MailKit). Os trincos das linhas do lote ficam presos enquanto o lote é enviado.
  - Os emails são em português, com texto simples e uma versão HTML simples com o mesmo conteúdo. Confirmação: assunto «Reserva confirmada: <clube>, dd/mm/aaaa às hh:mm» e clube, morada, campo, data por extenso, hora e preço («18,50 €»). Cancelamento: «Reserva cancelada: …» quando foi o jogador, «O clube cancelou a tua reserva: …» quando foi o clube, que leva também «Motivo». As horas são do fuso do clube, com «(verão)»/«(inverno)» na hora repetida de outubro, como nas páginas.
  - Definições SMTP em `Email` (validadas no arranque, e sem `Host` e `FromAddress` a API não arranca, exceto em Production, provisório, ver «Pontos a rever no T06»: `Host`, `Port`, `Security` = `None`/`StartTls`/`SslOnConnect`, `Username` e `Password` juntos ou nenhum, `FromAddress`, `FromName`, `TimeoutSeconds`); em desenvolvimento apontam para o Mailpit (`localhost:1025`, e `mailpit:1025` no docker compose). Em produção vêm de variáveis de ambiente (T06). Endereços, corpos dos emails e credenciais SMTP nunca vão para o log.
### US23 · Email ao dono sobre nova reserva · D · 1 ponto
 
Como dono, quero receber um email quando entra uma reserva nova num dos meus clubes, para acompanhar sem abrir a app.
 
- O email traz clube, campo, data, hora e nome do jogador.
- O dono pode desligar estes emails nas definições do clube.
- As reservas criadas pelo próprio dono (US13) não geram email.
- Estado: feita. Back end e, nas definições do clube, o interruptor «Receber um email por cada nova reserva».
- Decisões de implementação (decididas):
  - Definição por clube `notifyOnNewBooking`, **ligada por omissão** (também nos clubes que já existiam, pela migração). Edita-se no `PUT /api/manage/clubs/{id}` (e pode vir no `POST`); é opcional nos dois: se um pedido não a traz, fica como estava, para que um formulário que não a conhece não desligue os emails.
  - Quando um jogador reserva, e a definição está ligada, o email do dono entra na fila na mesma transação (uma reserva que perde a corrida não deixa email). O email traz clube, campo, data, hora de Lisboa e o nome do jogador, e nada mais sobre ele.
  - O destinatário é o email atual do dono do clube, lido no momento do envio. As reservas que o dono faz (US13) não enviam nada, nem ao dono nem a ninguém. A mensagem só guarda o id da reserva.
  - Se o dono foi **desativado** (US29) antes de o email ser enviado, a mensagem é marcada como ignorada (`skipped_at`, motivo `owner_deactivated`), sem enviar, como um lembrete de uma reserva cancelada; o email nunca vai para os logs.
## Épico 6 — Administração
 
Todas as páginas e endpoints deste épico só respondem ao perfil Admin (403 para os outros) e mostram contagens, nunca nomes ou emails de jogadores.
 
### US24 · Indicadores globais · M · 3 pontos
 
Como admin, quero ver os totais da plataforma, para saber o tamanho dela.
 
- Mostra o número de donos, clubes, campos ativos, jogadores e reservas confirmadas.
- Contas e clubes desativados aparecem separados dos ativos.
- Os valores vêm de queries agregadas na base de dados, sem carregar listas para memória.
- Estado: feita. API (`GET /api/admin/stats`) e os cartões da página `/admin`.
- Decisões de implementação (decididas):
  - A resposta tem donos e jogadores como `{ active, deactivated }`, clubes como `{ total, visible, ofDeactivatedOwners, withoutActivePitches }` (os três últimos somam o total: visível = dono ativo e pelo menos um campo ativo, como nas páginas públicas), `activePitches` e `confirmedBookings`. Admins não contam como donos nem jogadores.
  - Os campos ativos são os campos ativos de donos ativos (os de um dono desativado não estão disponíveis, por isso não contam); as reservas confirmadas são todas as confirmadas, também as passadas, de jogadores e de clientes (US13).
  - São sete contagens e um `GROUP BY` sobre as contas (perfil e estado): nada é carregado para memória. O DTO das contas só tem perfil, data de registo e estado, por construção sem nome nem email.
### US25 · Reservas por período · M · 3 pontos
 
Como admin, quero ver as reservas ao longo do tempo num gráfico, para perceber se a plataforma está a crescer.
 
- Gráfico de reservas por dia, semana ou mês, à escolha.
- Intervalo de datas configurável, por omissão os últimos 30 dias.
- Períodos sem reservas aparecem com zero, em vez de desaparecerem do gráfico.
- As reservas canceladas ficam de fora por omissão, com opção de as incluir.
- Estado: feita. API (`GET /api/admin/bookings-over-time?granularity=&from=&to=&includeCancelled=`) e o gráfico da página `/admin`.
- Decisões de implementação (decididas):
  - Uma reserva conta no período em que foi **criada** (não naquele em que se joga), pela data de Lisboa: `date_trunc(unidade, created_at AT TIME ZONE 'Europe/Lisbon')`. Assim 00:30 de 25 de outubro de 2026 (23:30 UTC do dia 24 já é dia 25 em Lisboa) cai no dia 25. Índice novo `ix_bookings_created_at`.
  - Semanas de segunda a domingo (ISO), meses civis. O intervalo é alargado a períodos inteiros (a resposta diz o `from`/`to` efetivos), por isso nunca há um período parcial.
  - Por omissão: por dia, os últimos 30 dias de Lisboa (hoje e os 29 anteriores). `from <= to`, datas entre 2000 e 2100, formato `AAAA-MM-DD`; erros 400 com mensagens em pt-PT.
  - Máximos por pedido: 366 dias, 104 semanas ou 60 meses.
  - Todos os períodos do intervalo aparecem, com 0 onde não há reservas (preenchidos no serviço, depois de a base contar).
  - Canceladas (pelo jogador ou pelo clube) ficam de fora; uma falta (`no_show`) conta, porque não é um cancelamento. As reservas feitas pelo clube para um cliente (US13) contam, são reservas da plataforma.
### US26 · Lista de donos · M · 3 pontos
 
Como admin, quero ver a lista de donos com os números de cada um, para saber quem usa a plataforma e quanto.
 
- Cada linha mostra nome do dono, email, data de registo, número de clubes, de campos, de reservas e de jogadores distintos que reservaram nos seus clubes.
- Ordenável por qualquer coluna, com pesquisa por nome ou email do dono.
- Paginada.
- Estado: feita. API (`GET /api/admin/owners?search=&sort=&direction=&page=`) e a tabela da página `/admin`.
- Decisões de implementação (decididas):
  - Colunas: nome, email, data de registo, desativado, clubes, campos, reservas e jogadores. Ordena-se por `name`, `email`, `registeredAt`, `status`, `clubs`, `pitches`, `bookings` ou `players`, em `asc`/`desc`; nome e email ordenam sem distinguir maiúsculas; o desempate é sempre o id do dono, por isso a ordem é estável. Por omissão, nome ascendente.
  - A pesquisa procura um pedaço do nome ou do email, sem distinguir maiúsculas (e nunca encontra jogadores). 20 por página.
  - Campos: só os campos ativos do dono. Reservas: as não canceladas (a falta conta), incluindo as feitas para clientes. Jogadores: jogadores distintos (`COUNT(DISTINCT player_id)`) com reservas não canceladas nos clubes do dono; as reservas de clientes não têm jogador e não contam.
  - Tudo é calculado pela base de dados, só para os donos da página. Os donos desativados aparecem, com o sinal de desativado.
### US27 · Clubes com maior ocupação · D · 3 pontos
 
Como admin, quero ver os clubes com mais ocupação num período, para saber quais funcionam melhor.
 
- Top 10 por taxa de ocupação no período escolhido, com a mesma fórmula da US14.
- Mostra nome do clube, dono, localidade e taxa.
- Estado: feita. API (`GET /api/admin/clubs/top-occupation?from=&to=`) e a tabela «Clubes com maior ocupação» da página `/admin`, com o período partilhado com os gráficos.
- Decisões de implementação (decididas):
  - Exatamente a fórmula e o período da US14 (`OccupationService`): um clube tem a mesma taxa aqui e nas estatísticas do dono (tem teste). Clubes sem horas abertas no período ficam de fora.
  - Ordem: a taxa **exata** (reservadas ÷ abertas, antes de arredondar), depois as horas reservadas, depois o id do clube. Os clubes de um dono **desativado** ficam de fora (não estão disponíveis, US29), tal como os campos inativos de um clube não dão horas abertas.
  - Cada linha tem o id, o slug, o nome do clube, o nome do dono, a localidade, a taxa e as horas reservadas e abertas; nada sobre jogadores.
### US28 · Novos registos ao longo do tempo · D · 2 pontos
 
Como admin, quero ver os novos donos e jogadores ao longo do tempo, para acompanhar a adesão.
 
- Gráfico de registos por semana ou mês, separado por perfil.
- Usa o mesmo filtro de intervalo da US25.
- Estado: feita. API (`GET /api/admin/registrations?granularity=week|month&from=&to=`) e o gráfico de registos da página `/admin`, com o período partilhado com o gráfico de reservas.
- Decisões de implementação (decididas):
  - Conta pela data de Lisboa de `users.registered_at` (`date_trunc` depois de `AT TIME ZONE`, semanas de segunda a domingo), donos e jogadores em separado, nunca o admin; contas desativadas contam (registaram-se).
  - As regras do intervalo são as da US25, partilhadas no código (`ReportingPeriods.Range`): por omissão os últimos 30 dias, alargados a períodos inteiros, com o `from`/`to` efetivos na resposta, no máximo 104 semanas ou 60 meses, datas entre 2000 e 2100. Só `week` (por omissão) e `month`; `day` é um 400.
  - Todos os períodos aparecem, com 0 onde não houve registos. Índice novo `ix_users_registered_at`.
### US29 · Desativar e reativar dono · D · 5 pontos
 
Como admin, quero desativar um dono, em caso de abuso ou a pedido dele, para que deixe de usar a plataforma.
 
- É obrigatório indicar o motivo.
- O dono desativado não consegue entrar, e os seus clubes deixam de aparecer na pesquisa e de aceitar reservas.
- A desativação revoga os refresh tokens do dono, e o `POST /api/auth/refresh` recusa contas desativadas, por isso não é emitido nenhum token novo.
- **Compromisso aceite (JWT sem estado, validade curta):** um access token já emitido continua válido até expirar, no máximo 15 minutos depois da desativação, porque a API valida o JWT sem consultar a base de dados. Durante esse período o dono ainda consegue fazer pedidos; depois disso, a sessão termina.
- As reservas futuras não são canceladas automaticamente; o admin vê quantas há antes de confirmar.
- O admin pode reativar a conta.
- Cada desativação e reativação fica registada (quem, quando e motivo), e esse registo não pode ser editado.
- Estado: feita no back end (`GET /api/admin/owners/{id}`, `POST …/deactivate`, `POST …/reactivate`, `GET …/history`) e a página `/admin/owners/{id}` (números, estado, histórico e o botão «Desativar dono» / «Reativar dono»).
- Decisões de implementação (decididas):
  - `GET /api/admin/owners/{id}` devolve a linha da lista (US26) e `futureBookings`: as reservas confirmadas dos clubes do dono que ainda não terminaram (a que está a decorrer conta; as de clientes também), o número que o admin lê antes de desativar. Um id desconhecido ou de um jogador ou do admin é sempre 404 `owner_not_found`, antes de se ler o corpo.
  - `deactivate` e `reactivate` levam `{ reason }`: obrigatório, sem espaços nas pontas, no máximo **500** caracteres (400 no campo `reason`). Respondem 200 com o dono como o `GET` o dá. Já desativado: 422 `owner_already_deactivated`; não desativado: 422 `owner_not_deactivated`.
  - Desativar põe `deactivated_at`, revoga todos os refresh tokens do dono e escreve a linha de registo, tudo na mesma transação; a decisão é um `UPDATE … WHERE deactivated_at IS NULL`, por isso dois admins ao mesmo tempo dão uma mudança e um 422. Não cancela nenhuma reserva. Reativar limpa `deactivated_at` e não restaura sessões: o dono volta a entrar.
  - Registo em `owner_status_changes` (dono, ação `Deactivated`/`Reactivated`, motivo, admin da sessão, quando). Não há código de alteração nem de remoção, e um trigger da base de dados recusa `UPDATE`, `DELETE` e `TRUNCATE` (SQLSTATE 23001); as chaves para as contas são `RESTRICT`, por isso uma conta com registo não pode ser apagada.
  - `GET …/history` lista as mudanças, as mais recentes primeiro, com ação (`deactivated`/`reactivated`), motivo, `adminId`, `adminName` e `at`.
  - Os efeitos já existiam e têm teste: não entra (401 genérico) nem renova a sessão; os clubes saem da pesquisa e da página (404 `club_not_found`) e recusam reservas (422 `club_unavailable`); as reservas futuras ficam confirmadas; reativar repõe tudo.
  - Um jogador continua a poder cancelar uma reserva num clube de um dono desativado, com a janela de cancelamento normal do clube (tem teste).
  - O dono desativado não vê as reservas nem os clubes até ser reativado; o jogador vê as suas reservas como sempre.
## Histórias técnicas
 
Não são visíveis para o utilizador, mas sem elas as estimativas das outras histórias ficam erradas. Todas são MVP, exceto a T07, acrescentada depois.
 
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
- Só corre com uma flag explícita, nunca por engano em produção. Em Production exige ainda `DemoData__AllowInProduction=true` no processo do comando (T06, para a demo pública).
- Os dados geram números com sentido nos gráficos e nas listas do admin.
- Estado: feita. Comando `seed-demo` da API (ver o README, «Demo data»).
- Decisões de implementação (decididas):
  - O comando é `dotnet run --project backend/src/SportsBooking.Api -- seed-demo` (no docker compose, `docker compose run --rm api seed-demo`). Só corre com exatamente esse argumento, cria os dados e sai sem arrancar o servidor (por isso o worker de emails nunca corre). Recusa-se no ambiente Production sem `DemoData__AllowInProduction=true` e recusa-se se já existir algum dono ou jogador (uma conta de admin não o impede), com uma mensagem e o código de saída 1; é tudo numa só transação.
  - Dados: 5 donos e 10 clubes (2 por dono) em Porto, Vila Nova de Gaia, Matosinhos, Maia, Lisboa, Oeiras e Cascais, 43 campos dos quatro desportos, com preços realistas (ténis 7 a 16 €, padel 18 a 32 €, futsal 35 a 45 €, futebol 7 de 55 a 70 €), horários (alguns fecham à segunda ou à meia-noite) e slots de 30, 60 e 90 minutos; 5 bloqueios (dois já passados e três futuros, um deles a manutenção de um campo de padel daqui a 5 dias); 60 jogadores registados ao longo dos últimos 90 dias (os donos, antes disso); e cerca de 10 400 reservas dos últimos 60 dias e dos próximos 14: mais ao fim da tarde e ao fim de semana, a crescer ao longo do tempo e a diminuir para a frente, com reservas confirmadas, canceladas pelo jogador (dentro da janela do clube), canceladas pelo clube (com motivo) e faltas no passado, alguns slots libertados que outro jogador voltou a reservar, e cerca de 6 % de reservas para clientes (US13).
  - É determinístico: uma semente fixa (`20260101`) e tudo calculado a partir do dia de hoje em Lisboa (o «agora» dos dados é o início de hoje). O mesmo dia dá os mesmos nomes, datas, preços e estados; só mudam os ids e os hashes das palavras-passe.
  - Tudo passa pelas mesmas entidades e regras da aplicação (`Club.Create`, `Pitch.ChangeSchedule`, `Booking.ForPlayer`, `Booking.ForCustomer`, `CancelByPlayer`, `CancelByClub`): só se contorna a regra «não há reservas no passado», porque as reservas são construídas diretamente, sem o serviço. Cada reserva está num slot gerado do seu campo (`Pitch.SlotsOn`), ao preço do campo, fora dos bloqueios, sem sobreposição (a exclusion constraint aceita tudo), criada antes de começar, depois de o jogador se registar e a menos de 30 dias do início.
  - Uma falta (`no_show`) foi marcada por `Booking.MarkNoShow(now)`, que só aceitava uma reserva confirmada que já acabou (a base da US15, entretanto feita: o serviço do dono usa `Booking.RecordNoShow(now)` e `UndoNoShow()`; `MarkNoShow` é o mesmo que `RecordNoShow(now) == Marked` e só o seed o usa).
  - As contas têm a data de registo (`users.registered_at`, nova nesta história, preenchida pelo registo e pelo seed; as contas que já existiam ficam com a data da migração): é o que a lista de donos e o gráfico de registos do admin (US26, US28) vão ler.
  - Todos os endereços são do domínio reservado `example.test` (`dono01`..`dono05`, `jogador01`..`jogador60`, `geral.clube01`..`geral.clube10`) e todas as contas têm a mesma palavra-passe de desenvolvimento, `demo-password-1`, que está no README. Não são criadas mensagens no outbox, por isso o seed não envia email. O comando só escreve contagens, nunca contas nem a palavra-passe.
  - Contas que já existiam antes da migração ficam com a data da migração como `registered_at`; todas as contas de demonstração partilham o mesmo hash de palavra-passe.
  - Mistura de resultados das reservas passadas: cerca de 80 % realizadas, 11 % canceladas pelo jogador, 3 % canceladas pelo clube e 6 % faltas; cerca de 6 % das reservas são de clientes sem conta (US13).
### T06 · Deploy contínuo · 5 pontos
 
Como programador, quero que cada merge na main fique publicado automaticamente, para ter sempre uma versão online para mostrar.
 
- A aplicação está acessível num URL público com HTTPS.
- Cada merge na main que passe no CI faz deploy automático.
- Os segredos (ligação à base de dados, chave JWT, SMTP, conta de admin) vêm de variáveis de ambiente, nunca do repositório.
  - SMTP (US22, Brevo): `Email__Host`, `Email__Port` (2525, confirmado no primeiro deploy), `Email__Security` (`StartTls`), `Email__Username`, `Email__Password` e `Email__FromAddress` (um remetente verificado no Brevo); sem `Email__Host` e `Email__FromAddress` a API arranca em Production, avisa e não envia (provisório). Com mais de uma instância da API, o worker do outbox pode correr em todas (`SKIP LOCKED`).
- As migrações são aplicadas no deploy de forma controlada.
- A base de dados tem dois utilizadores. O migration bundle corre como o dono da base de dados, que cria a extensão `btree_gist` (extensão de confiança no PostgreSQL 13 ou superior) e o resto do esquema. A API liga-se com um utilizador de aplicação separado, só com direitos de leitura e escrita nos dados e sem DDL, por isso nunca altera o esquema nem cria extensões.
- Existe um endpoint `/health` para verificar se a API está a responder. Só diz se o processo está vivo e não toca na base de dados (parte A da T06).
- `PublicSite__BaseUrl` é o endereço HTTPS público do site, usado nas ligações dos emails (US05); sem ele a API não arranca.
- Os endpoints públicos de leitura (`/api/clubs`, `/api/localities`, a página do clube e os slots) têm um limite de pedidos por IP, folgado o suficiente para uma navegação normal (pesquisar, abrir clubes, mudar de data e de campo), para que ninguém os possa usar para extrair todos os dados ou sobrecarregar a API. Responde 429 `too_many_requests` com `Retry-After`, como nas rotas de autenticação, e usa o mesmo IP do cliente (ver o critério de `ForwardedHeaders` abaixo).
- Estado: **feita e em produção desde 2026-11-09 (primeiro deploy real).** Os workflows (`deploy.yml`, `prod-data.yml`), os papéis da base de dados (`docs/deployment/supabase-roles.sql`) e o [runbook](deployment/runbook.md) foram usados no primeiro deploy. O que o primeiro deploy confirmou: `ForwardLimit=1`, o Brevo na porta 2525 com `StartTls`, o login e um pedido que altera dados pelo rewrite da Vercel (`Origin`, `Set-Cookie`), nada em cache nos rewrites e os campos do Render (Dockerfile, contexto e health check) como no runbook; nada diferiu da documentação. Ficam provisórios os pontos de «Pontos a rever no T06»: IP do cliente (melhor esforço), SMTP em produção, migrações, CLI da Vercel, espera pela API e versão do PostgreSQL. O limite de pedidos dos endpoints públicos de leitura está feito: 120 pedidos por minuto e por IP, um só limite partilhado pelos quatro endpoints (`RateLimiting:Public:Window` / `ReadPermitLimit`, valores provisórios), com a mesma partição por IP e as mesmas regras de proxy de confiança das rotas de autenticação.
- O Dependabot está ligado (`.github/dependabot.yml`: NuGet, npm, GitHub Actions e os `Dockerfile` do back end e do front end; o Docker acrescentou-se na `chore/repo-hygiene`). **Feito**, antes da T06: todas as segundas-feiras, uma PR agrupada por ecossistema para as atualizações menores e de correção, no máximo 5 PRs abertas (3 nas Actions) e `cooldown` de 14 dias para respeitar a regra das versões com pelo menos duas semanas (as correções de segurança não esperam). As atualizações maiores vêm uma a uma e revêem-se à mão.
- Higiene do repositório (`chore/repo-hygiene`, depois da T06): licença MIT (`LICENSE`, também em `frontend/package.json`), `SECURITY.md` (como comunicar uma vulnerabilidade; demonstração pessoal, só a `main` tem suporte, sem prazos garantidos) e o CodeQL (`.github/workflows/codeql.yml`: C# e JavaScript/TypeScript, sem build, em cada PR, em cada `push` à `main` e às segundas-feiras; um workflow à parte, de que o `deploy.yml` não depende). O CodeQL (e o relato privado de vulnerabilidades do `SECURITY.md`) só funcionam com o repositório público.
- **Fechado (`fix/club-not-found`):** a página de um clube que não existe (`/clubs/<desconhecido>`) respondia HTTP 200 em vez de 404. Causa: o `loading.tsx` do grupo `(player)` começa a enviar a resposta antes de o `notFound()` correr, e o estado fica fixado no primeiro byte (o Next.js só acrescenta `noindex`). O mesmo valia para um GUID mal formado em `/manage/clubs/{id}…` e `/admin/owners/{id}`. Correção: o `proxy.ts` responde 404 antes de a página enviar (`lib/not-found-check.ts`): para um slug ou GUID impossível, sem perguntar à API, e para um clube público que a API não tem (um pedido por slug e por minuto, só em carregamentos reais; se a API falhar ou demorar, a página passa). Testes: `src/proxy-not-found.test.ts`, `src/lib/not-found-check.test.ts` e `frontend/scripts/check-not-found.mjs`, que o CI corre contra o build com uma API simulada. **Provisório:** um GUID bem formado que não existe (ou que é de outro dono) continua a responder 200 com o ecrã «não encontrado» no cliente, porque só se sabe com a sessão; são páginas atrás de login. O custo no caminho feliz é um pedido à API na primeira visita a cada clube, por minuto.
- Os cabeçalhos de segurança `Content-Security-Policy`, `X-Content-Type-Options` e `Referrer-Policy` estão definidos nas respostas, com testes. **Feito**, antes da T06: o site (Next.js, `frontend/src/lib/security-headers.ts`, aplicados em todas as páginas) envia também `Permissions-Policy` restritiva e `frame-ancestors 'none'` e já não envia `X-Powered-By`; a `/reset-password` mantém o seu `Referrer-Policy: no-referrer` e `Cache-Control: no-store`; a API envia `X-Content-Type-Options: nosniff` em todas as respostas e já não envia `Server`; o Swagger continua só em Development. O HSTS e o `upgrade-insecure-requests` ficam **definidos mas desligados**: ligam-se com `ENABLE_HSTS=true` no build do front end (argumento de build do `Dockerfile`; os cabeçalhos ficam escritos no build) e **só na T06**, quando o site for servido por HTTPS. O site publicado envia HSTS (verificado em 2026-11-10: `max-age=63072000; includeSubDomains; preload`, valor da plataforma, a Vercel). A API, que desde a [ADR 0009](architecture/adr/0009-alvo-de-implantacao.md) é pública no Render, envia hoje só `X-Content-Type-Options: nosniff` e não envia `Server` nem HSTS (o navegador só fala com a origem do site, por isso o HSTS que conta é o do site); se o Render acrescenta algum cabeçalho à frente da API, não se verificou. **Provisório:** a política permite `'unsafe-inline'` nos scripts e nos estilos, porque o Next.js com `cacheComponents` envia scripts em linha com os dados de cada página (nem um nonce por pedido nem um hash de build os cobrem; experimentou-se os dois num browser e a aplicação deixou de hidratar) e porque a grelha do calendário e o Sonner usam estilos em linha. Uma política estrita pede um nonce e todas as páginas dinâmicas, ou seja, abdicar do `cacheComponents`: decisão de arquitetura por tomar.
- O rate limiting só distingue os clientes em produção com `ForwardedHeaders__TrustAnyProxy=true` e `ForwardedHeaders__ForwardLimit=1` (confirmado no primeiro deploy); é de melhor esforço (provisório, ver «Pontos a rever no T06»).
- Manter a demo ativa (`keepalive.yml`, depois da T06): duas vezes por semana, uma consulta de leitura à base de dados pelo site (`GET /api/localities`), para o Supabase não pausar o projeto por inatividade; não usa segredos e só corre com `DEPLOY_ENABLED=true`. Ver o [runbook](deployment/runbook.md#10-manter-a-demo-ativa) e a [ADR 0009](architecture/adr/0009-alvo-de-implantacao.md).
  - **Provisório (frequência do keepalive):** a documentação do Supabase diz que um projeto gratuito é pausado com «low activity over a 7-day period» sem dar um número, e que «a few user requests to the database each day» costumam chegar; duas consultas por semana pode ser pouco. Aumentar a frequência é mudar o `cron`.
  - **Provisório (o que conta como atividade):** a documentação do Supabase não diz se uma visita ao painel conta como atividade na base de dados; a do GitHub não define «repository activity» para a regra dos 60 dias que desativa os workflows agendados nos repositórios públicos.
- Parte A (código e configuração) feita: proxy para a API definido no build, `Origin` configurável, IP do cliente, imagem Docker para o Render, variáveis em `docs/deployment/environment-variables.md`, comandos em Production.
### T07 · Termos e Política de Privacidade · 3 pontos

Como dono da plataforma, quero publicar os Termos e Condições e a Política de Privacidade e guardar a aceitação no registo, para que quem cria conta saiba o que aceita e que dados são tratados.

- Existem as páginas `/terms` e `/privacy`, geradas no servidor, com um layout comum, títulos e regiões corretos, e com «Última atualização» e a versão, que vive num só módulo.
- Todas as páginas têm no rodapé uma ligação para as duas.
- Cada afirmação de facto pode ser confirmada no repositório, em `docs/deployment` ou na aplicação a correr; o que não se pode confirmar diz «a confirmar».
- A Política diz quem é o responsável e o contacto, que dados são recolhidos e para quê, a base legal de cada finalidade, os subcontratantes e o que cada um recebe, quanto tempo os dados ficam, os cookies, os direitos do RGPD e como exercê-los, a queixa à CNPD e quem responde pelos dados dos clientes sem conta introduzidos pelos donos.
- Os Termos dizem o que é o serviço (demonstração pessoal, sem fins comerciais e sem garantias, que pode ser desligada e demora a acordar), as contas, as regras das reservas, as responsabilidades dos donos, a utilização aceitável, a disponibilidade e os limites de responsabilidade, as alterações, a lei portuguesa e o contacto.
- Só há cookies estritamente necessários e, por isso, não há banner; os cookies e o armazenamento descritos são os que se observam no browser.
- Os dois registos (jogador e dono) têm uma caixa obrigatória, «Li e aceito os Termos e Condições e tomei conhecimento da Política de Privacidade», com ligações para as duas páginas e mensagem de erro se não estiver marcada, utilizável com teclado e leitor de ecrã.
- A API guarda a versão aceite e a data (`TimeProvider`) em colunas opcionais de `users`, pelo mesmo caminho nos dois registos. Um registo sem aceitação é recusado com um código estável.
- Uma só migração compatível com a versão anterior da API (colunas opcionais, sem mexer em dados). As contas que já existem e as de demonstração ficam sem valor, não são bloqueadas e nunca se diz que aceitaram.
- O axe não encontra violações em `/terms`, `/privacy` e no formulário de registo.
- Estado: feita. Páginas em `frontend/src/app/(legal)`, versão e data em `features/legal/legal-info.ts`, rodapé no layout raiz (`SiteFooter`). Pedido de registo com `acceptedTermsVersion`; colunas `users.accepted_terms_version` e `users.terms_accepted_at` (migração `AddTermsAcceptance`); sem aceitação, 400 `terms_not_accepted`, com a mensagem em `errors.acceptedTermsVersion`. Os erros dos outros campos vêm primeiro (400 `validation_failed`). O `docs/deployment/supabase-roles.sql` não muda: os direitos são por tabela e as colunas novas herdam-nos.
- Verificado na aplicação a correr (docker compose): dois cookies, `access_token` (`/`, httpOnly, Secure, Lax, 15 minutos) e `refresh_token` (`/api/auth`, httpOnly, Secure, Strict, 7 dias), só depois de entrar; nada em `localStorage` nem em `sessionStorage`; nenhum pedido a outra origem (as fontes são servidas pelo próprio site).
- **Provisório (janela do deploy):** a API nova fica ativa antes do site novo, por isso, durante esses minutos, o registo pelo site antigo recebe 400. Não há forma barata de o evitar num só deploy: a alternativa, publicar primeiro o site e só depois exigir a aceitação na API, pede duas entregas e deixa contas sem registo de aceitação nesse intervalo. Aceite numa demonstração.
- **Provisório (textos legais):** as bases legais de cada finalidade e a divisão de responsabilidades sobre os dados dos clientes sem conta foram escritas de forma conservadora e sem revisão jurídica.
- **Provisório (a confirmar nas páginas):** a localização do Brevo e do resto do serviço da Vercel (além das funções), a existência de transferências para fora da União Europeia e o tempo de conservação dos registos (logs) no Render e na Vercel. As regiões de Frankfurt (Render, Supabase e funções da Vercel) foram indicadas pelo dono do projeto.
- **Provisório (versão aceite):** a API guarda a versão que o site envia (não vazia, até 32 caracteres) e não a compara com uma lista, para o site continuar a ser o único sítio que define as versões.
## Resumo de pontos
 
| Épico | MVP (histórias) | MVP (pontos) | Depois (histórias) | Depois (pontos) | Feitas (histórias) | Feitas (pontos) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 — Contas e acesso | 4 | 12 | 2 | 8 | 5 de 6 | 15 de 20 |
| 2 — Configuração do clube | 4 | 16 | 1 | 5 | 5 de 5 | 21 de 21 |
| 3 — Operação do clube | 3 | 13 | 2 | 7 | 5 de 5 | 20 de 20 |
| 4 — Pesquisa e reserva | 4 | 16 | 2 | 11 | 6 de 6 | 27 de 27 |
| 5 — Notificações | 1 | 3 | 1 | 1 | 2 de 2 | 4 de 4 |
| 6 — Administração | 3 | 9 | 3 | 10 | 6 de 6 | 19 de 19 |
| Histórias técnicas | 6 | 23 | 1 | 3 | 7 de 7 | 26 de 26 |
| **Total** | **25** | **92** | **12** | **45** | **36 de 37** | **132 de 137** |

Estado: falta a US30 (apagar a conta e exportar os dados), que nasceu da T07. A **T06 (deploy contínuo, 5 pontos)** está em produção desde 2026-11-09.
 
As duas histórias de 8 pontos (US11 e US20) são as primeiras a dividir se não couberem num sprint.