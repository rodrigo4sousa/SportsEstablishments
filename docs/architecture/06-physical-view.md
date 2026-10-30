# 06 · Vista física

<!-- Diagramas de implantação do C4 · vista 4+1: física. Ver README.md. -->

Onde corre cada contentor, como se chega a ele, em que se confia e que configuração muda o comportamento. Dois níveis: os **diagramas de implantação** (desenvolvimento, que existe, e produção, que está **planeada para a T06**) e a **rede e confiança** (o que é público, TLS, origem, IP do cliente, base de dados, segredos). Os ids estão no [inventário](00-inventory.md); os nós `DEP-` e os novos `PL-` foram acrescentados com esta vista.

O que se diz do desenvolvimento foi conferido em 2026-10-30, no commit `452be5a`, com `docker-compose.yml`, `.env.example`, `backend/Dockerfile`, `frontend/Dockerfile`, `frontend/next.config.ts`, `frontend/src/proxy.ts`, `Api/Program.cs`, os `appsettings*.json` e as classes de opções. **Nada de produção existe ainda**: não há deploy, nem `render.yaml`, nem contas configuradas. O que se diz dos fornecedores (Render, Supabase, Brevo) vem da documentação deles, lida em 2026-10-30, e está marcado **«a confirmar (2026-10-30)»**: os planos, preços e limites mudam, e têm de ser verificados outra vez quando a T06 começar. A decisão está na [ADR 0009](adr/0009-alvo-de-implantacao.md).

## 1. Desenvolvimento (docker compose)

![Implantação em desenvolvimento](diagrams/06-physical-dev.svg)

Fonte: [06-physical-dev.puml](diagrams/06-physical-dev.puml).

Uma máquina (DEP-DEVHOST) com Docker. `docker compose up --build` cria o projeto `sportsbooking` com a rede por omissão do compose (DEP-COMPOSE-NET), onde os serviços se chamam pelo nome (`api`, `postgres`, `mailpit`).

| Serviço | Elemento | Imagem ou build | Portas (anfitrião → contentor) | Volumes | Arranca depois de | Ambiente (do `docker-compose.yml`) |
| --- | --- | --- | --- | --- | --- | --- |
| `postgres` | C-DB | `postgres:18.6` | `5432 → 5432` | `postgres-data` (DEP-PGVOLUME) em `/var/lib/postgresql` | — | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` (do `.env`, por omissão `sportsbooking`). Health check `pg_isready` a cada 5 s. |
| `mailpit` | X-MAILPIT | `axllent/mailpit:v1.31.6` | `8025 → 8025` (web), `1025 → 1025` (SMTP) | — | — | — |
| `api` | C-API, C-WORKER | build de `./backend` (`backend/Dockerfile`: SDK 10.0 → `aspnet:10.0`, utilizador `$APP_UID`, `EXPOSE 8080`) | `5080 → 8080` | — | `postgres` saudável, `mailpit` arrancado | `ASPNETCORE_ENVIRONMENT=Development`; `ConnectionStrings__Default` (`Host=postgres;Port=5432;…;GSS Encryption Mode=Disable`); `AdminSeed__Email`/`AdminSeed__Password` (de `ADMIN_SEED_*` no `.env`, vazios por omissão); `Email__Host=mailpit`, `Email__Port=1025`, `Email__Security=None`. |
| `frontend` | C-WEB | build de `./frontend` (`frontend/Dockerfile`: `node:22-alpine`, `output: "standalone"`, utilizador `node`) com o argumento de build `API_INTERNAL_URL` | `3000 → 3000` | — | `api` (arrancado, não saudável) | `API_INTERNAL_URL=http://api:8080`, definido uma vez (`x-api-internal-url`) e usado como argumento de build e como variável de ambiente. |

O que vem do `appsettings.Development.json` (porque o ambiente é Development) e não do compose: `Jwt:SigningKey` (chave só de desenvolvimento), `Email:FromAddress`, `PublicSite:BaseUrl=http://localhost:3000` e `Security:AllowedOrigins` (`http://localhost:3000` e `http://localhost:5080`).

**Consequências do ambiente Development:**

- A API aplica as migrações no arranque (`Program.cs`), corre o seed do admin e mostra o Swagger em `/swagger` (só em Development).
- O browser fala com `http://localhost:3000`; os cookies são `Secure` mas os browsers aceitam-nos em `localhost` sem HTTPS (comentário em `Api/Auth/AuthCookies.cs`).
- A API tem uma segunda entrada, `localhost:5080`, para ferramentas ([Notas de arquitetura](README.md#notas-de-arquitetura), 1). O PostgreSQL também está publicado em `localhost:5432`, para correr a API fora do Docker (`dotnet run`, com a ligação do `appsettings.Development.json`) e para clientes SQL.
- Os dados ficam no volume `postgres-data`: sobrevivem a `docker compose down` e só se apagam com `docker compose down -v`.
- Os comandos de linha (C-CLI) correm como `docker compose run --rm api seed-demo` (ou `set-admin-password`, com `ADMIN_NEW_PASSWORD`): um contentor de curta duração na mesma rede.

## 2. Integração contínua

O CI (X-GHA, `.github/workflows/ci.yml`) não implanta nada. Os runners `ubuntu-latest` constroem e testam; os testes de integração arrancam um `postgres:18.6` próprio com Testcontainers, e o job `diagrams` arranca a imagem do PlantUML. Detalhe em [05 · Vista de desenvolvimento](05-development-view.md#4-pipeline-de-build-e-qualidade). Na T06 o CI passa também a fazer parte do deploy (secção 5).

## 3. Produção, planeado (T06)

![Implantação em produção, planeado (T06)](diagrams/06-physical-prod.svg)

Fonte: [06-physical-prod.puml](diagrams/06-physical-prod.puml). **Planeado (T06): nada disto existe ainda.** Os contornos tracejados são o que se prevê.

| Nó ou serviço | Elemento | Corre | Público? | Nota |
| --- | --- | --- | --- | --- |
| DNS do domínio próprio | PL-DNS | Registos do domínio | Sim (DNS) | Aponta o domínio para o Render; tem os registos do Brevo (SPF, DKIM, DMARC). |
| Borda do Render | PL-RENDER-EDGE | TLS, certificado, balanceamento | Sim | Gerida pelo Render. A documentação do Render diz que o tráfego passa pelo Cloudflare e pelos balanceadores do Render, e que a aplicação vê o IP do proxy e tem de ler `X-Forwarded-For` ([How Render handles DDoS attacks](https://render.com/articles/how-render-handles-ddos-attacks)); a confirmar (2026-10-30) que cabeçalho e que posição são de confiança. |
| Serviço web público | PL-RENDER-WEB | C-WEB, imagem de `frontend/Dockerfile` | Sim, só por HTTPS no domínio | **A única entrada pública.** |
| Rede privada | PL-RENDER-NET | — | Não | Os serviços da mesma conta e região partilham uma rede privada ([Render free](https://render.com/docs/free), a confirmar 2026-10-30). |
| Serviço privado | PL-RENDER-API | C-API com C-WORKER, imagem de `backend/Dockerfile` | **Não**: sem URL público | Só a rede privada lhe chega. Tem de estar sempre ligado (o worker vive aqui). |
| Supabase | PL-SUPABASE, PL-SUPAPOOL, C-DB | PostgreSQL gerido e o pooler | Sim (endpoint na internet, com palavra-passe e TLS) | Ligação pelo pooler em modo sessão (secção 4.6). |
| Brevo | PL-BREVO | SMTP | — (fornecedor) | Porta 587 com StartTls; remetente no domínio próprio. |
| GitHub Actions | X-GHA | CI e passos do deploy | — | Aplica as migrações ou dispara o deploy (a decidir, secção 4.8). |

## 4. Rede e confiança

### 4.1 O que é público e o que não é

| O quê | Hoje (compose) | Produção planeada |
| --- | --- | --- |
| Páginas e `/api/*` | `localhost:3000` (C-WEB) | `https://<domínio>`, pelo serviço web público (PL-RENDER-WEB) |
| A API diretamente | `localhost:5080` (Swagger) | **Não.** Serviço privado, sem URL público; o Swagger só existe em Development |
| O PostgreSQL | `localhost:5432` | Endpoint do Supabase na internet, protegido por palavra-passe e TLS; ninguém além da API e do passo de migração o deve usar |
| O SMTP | Mailpit em `localhost:1025`/`8025` | Brevo (fornecedor externo) |
| O URL `*.onrender.com` do serviço web | — | Existe por omissão em cada serviço web (a confirmar 2026-10-30 se se pode desligar). Como a API só aceita o `Origin` do domínio próprio, um `POST` feito a partir desse endereço recebe 403 `origin_not_allowed`; o site tem de ser usado só no domínio próprio. |

### 4.2 TLS

- **Browser → borda:** HTTPS, terminado pela borda do Render com o certificado do domínio próprio (gerido pelo Render; a confirmar 2026-10-30). Os cookies já são `Secure` (`AuthCookies`).
- **HSTS** está definido mas desligado: o Next.js envia `Strict-Transport-Security: max-age=31536000` (só este anfitrião, sem `includeSubDomains` nem `preload`) e `upgrade-insecure-requests` quando o build tem `ENABLE_HSTS=true`, nunca em desenvolvimento (WEB-HEADERS). Os cabeçalhos ficam escritos no build, por isso é um argumento de build do `frontend/Dockerfile` (por omissão `false`; o compose não o liga). **Liga-se na T06**, quando o site for servido por HTTPS: uma vez enviado, o browser recusa HTTP nesse domínio durante um ano. A API não chama `UseHsts`: não é pública, e o que ela responde chega ao browser pelo Next.js.
- **Borda → C-WEB e C-WEB → C-API:** HTTP dentro do Render (rede privada). A API escuta em HTTP na porta 8080 e não faz `UseHttpsRedirection`, o que está certo atrás de um proxy.
- **C-API → Supabase:** tem de ser TLS. A ligação de desenvolvimento não pede TLS; a de produção deve levar `SSL Mode=VerifyFull` (ou pelo menos `Require`) na `ConnectionStrings__Default`. A confirmar que certificado o Supabase apresenta no pooler.
- **C-WORKER → Brevo:** `Email__Security=StartTls` na porta 587 (o valor por omissão de `EmailOptions`); `StartTls` recusa o envio se o servidor não suportar TLS.

### 4.3 Uma só origem: cookies first-party, sem CORS, `Origin`

O browser só conhece `https://<domínio>`: as páginas e `/api/*` vêm do mesmo serviço (C-WEB), que reencaminha `/api/*` para a API pela rede privada. Por isso:

- Os cookies `access_token` (`Path=/`, `SameSite=Lax`) e `refresh_token` (`Path=/api/auth`, `SameSite=Strict`), ambos httpOnly e Secure, são **first-party**: o browser não sabe que a API existe noutro serviço.
- **Não há CORS** (nenhum pedido sai para outra origem) e a API não o configura.
- A **verificação do `Origin`** (CC-ORIGIN) aceita só a lista `Security:AllowedOrigins`. Em produção: `Security__AllowedOrigins__0=https://<domínio>`, exatamente o mesmo valor de `PublicSite__BaseUrl` (os dois sem barra final; o validador de `Security` recusa um caminho). Sem pelo menos uma origem a API não arranca. Em produção não entra `http://localhost:*`.
- Quem chama a API a partir do servidor (Server Components) só faz `GET`, que não precisa de `Origin`. Uma Route Handler ou Server Action que altere dados teria de enviar o `Origin` do site; hoje não há nenhuma.

### 4.4 O IP do cliente

**O problema.** O rate limiting (CC-RATE) conta por IP do cliente. A API só lê `X-Forwarded-For` quando a ligação vem de um endereço de `ForwardedHeaders:KnownProxies`/`KnownNetworks` (vazios por omissão; `UseForwardedHeaders` é o primeiro middleware). Em produção quem liga à API é sempre o Next.js, por isso:

- **Sem nenhum proxy de confiança** (a configuração de hoje), a API vê o endereço do serviço Next.js em todos os pedidos: **todos os utilizadores partilham um só limite**. Com os valores de hoje, isso são 10 logins, 5 registos e 120 leituras públicas por minuto **para o site inteiro**. Implantar assim torna o site quase inutilizável assim que houver tráfego.
- **Confiar no Next.js tal como está** é proibido pela regra da US03: o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser, por isso qualquer pessoa escolheria o seu próprio IP e fugiria aos limites.

**O desenho seguro para a T06 (não implementado; precisa de uma mudança de código e de configuração na T06).**

1. **Saber de onde vem o IP verdadeiro.** Confirmar na documentação do Render, e com um pedido real, que cabeçalho a borda põe com o IP do cliente e em que posição (por exemplo, a última entrada de `X-Forwarded-For`, acrescentada pela borda, ou um cabeçalho próprio). O browser controla as primeiras entradas de `X-Forwarded-For`; essas nunca contam. A confirmar (2026-10-30).
2. **O Next.js reescreve, não acrescenta.** Em `proxy.ts` (WEB-PROXY), com o matcher alargado a `/api/:path*`, o Next.js **substitui** `X-Forwarded-For` por um único valor, o IP obtido no passo 1, antes do rewrite. Verificado na documentação do Next.js 16 que vem no repositório (`node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/proxy.md`, «Execution order»): o proxy corre antes dos `rewrites` do `next.config.ts`. Fica por confirmar se o próprio rewrite acrescenta outra entrada (o endereço de quem lhe ligou) ao cabeçalho: a API, com o `ForwardLimit` por omissão do ASP.NET Core (1), lê só a **última** entrada, por isso o que chega à API tem de ser verificado com um pedido real.
3. **As chamadas do servidor também.** `getPublicClub` (`Web/lib/server/public-club.ts`) chama `GET /api/clubs/{slug}`, que tem o limite público, a partir do servidor e sem `X-Forwarded-For`: todas as páginas de clube partilhariam a partição do Next.js mesmo depois dos passos 1 e 2. O pedido do servidor tem de levar o IP do cliente da página (lido dos cabeçalhos do pedido original, como no passo 2), ou essa chamada tem de ficar fora do limite. `GET /api/auth/me` não tem limite.
4. **Só então a API confia no Next.js.** `ForwardedHeaders__KnownProxies__0` com o endereço privado do serviço Next.js, se for fixo, ou `ForwardedHeaders__KnownNetworks__0` com a rede privada do Render. A confirmar (2026-10-30) se o endereço privado de um serviço muda a cada deploy; confiar na rede inteira significa confiar em qualquer serviço da conta nessa região, o que só é aceitável se lá estiverem apenas os nossos.
5. **Regras e testes.** A regra da US03 («nunca confiar no contentor do Next.js») passa a «confiar no Next.js só porque ele reescreve o cabeçalho»: o `AGENTS.md`, o backlog e os testes do `proxy.ts` mudam com a T06, e o limite público de 120 por minuto é revisto (já está em «Pontos a rever no T06» do backlog).

Até esta mudança estar feita, a configuração conservadora é a de hoje (nenhum proxy de confiança), com o custo descrito acima.

### 4.5 Os rewrites são fixados no build

`next.config.ts` lê `API_INTERNAL_URL` quando o Next.js **constrói** a aplicação: o destino de `/api/*` fica escrito no build. Os Server Components leem a mesma variável **em execução** (`Web/lib/server/api-url.ts`). O `frontend/Dockerfile` exige o argumento de build (falha se estiver vazio) e não tem valor por omissão; fora do Docker, `next.config.ts` cai para `http://localhost:5080`.

O que isto quer dizer no Render:

- O serviço web tem de ser construído com o **Dockerfile** (não com o runtime Node nativo do Render), para que um `API_INTERNAL_URL` em falta pare o build em vez de cair em `localhost:5080` e falhar em silêncio.
- O Render passa as variáveis de ambiente de um serviço Docker como argumentos de build ([Docker on Render](https://render.com/docs/docker), a confirmar 2026-10-30), por isso **uma só** variável `API_INTERNAL_URL` serve o build e a execução, como no compose. Não é um segredo, o que importa porque os argumentos de build podem ficar nas camadas da imagem.
- O endereço interno da API (host e porta do serviço privado) tem de ser conhecido **antes do primeiro build** do front end: cria-se primeiro o serviço da API.
- **Mudar o endereço interno da API** (outro nome de serviço, outra porta) obriga a **reconstruir** o front end; reiniciá-lo não chega.

### 4.6 Base de dados: papéis, ligação e Supabase

**Dois papéis (PL-DBUSERS, critério da T06):**

| Papel | Usado por | Pode | Não pode |
| --- | --- | --- | --- |
| Dono do esquema | O passo de migração (PL-MIGBUNDLE), e nada mais | DDL: criar e alterar tabelas, índices, a `bookings_no_overlap`, o trigger DB-AUDITTRG e a extensão `btree_gist` (DB-GIST) | — |
| Utilizador da aplicação | C-API, C-WORKER e C-CLI | `SELECT`, `INSERT`, `UPDATE`, `DELETE` nas tabelas da aplicação; usar as sequências; `USAGE` no esquema | DDL, `TRUNCATE`, criar extensões |

O que já é compatível no código: a API só aplica migrações em Development ou com `Database:ApplyMigrationsOnStartup=true`, que fica desligado em produção. O seed do admin, as chaves do Data Protection e os comandos `seed-demo`/`set-admin-password` só fazem DML. Os privilégios de tabelas futuras precisam de `ALTER DEFAULT PRIVILEGES` do dono do esquema para o utilizador da aplicação, senão uma migração nova cria uma tabela que a API não consegue ler.

**Ligação pelo pooler do Supabase.** As transações da API dependem de estado de sessão: `PitchLock` abre uma transação e mantém `SELECT … FOR UPDATE` até ao `COMMIT`, o worker mantém `FOR UPDATE SKIP LOCKED` durante o lote, e o Npgsql prepara comandos. A documentação do Supabase diz que a porta 5432 do pooler partilhado é o **modo sessão** (prepared statements e estado de sessão funcionam) e que a 6543 é o **modo transação**, onde o pooler partilhado não suporta prepared statements ([Connect to your database](https://supabase.com/docs/guides/database/connecting-to-postgres), a confirmar 2026-10-30). Escolha conservadora: **modo sessão na porta 5432** (ou a ligação direta). A ligação direta é IPv6, ou IPv4 com um add-on pago; o pooler partilhado é IPv4 ([Dedicated IPv4 Address](https://supabase.com/docs/guides/platform/ipv4-address), a confirmar 2026-10-30). Em modo sessão cada ligação do pool do Npgsql ocupa uma ligação do pooler: `Maximum Pool Size` tem de caber no limite do plano.

**API de dados do Supabase.** O Supabase publica o esquema `public` numa API REST; uma tabela de `public` sem row level security pode ser lida e escrita por quem tiver a chave publicável do projeto, e o Supabase anunciou que as tabelas novas deixam de ser publicadas automaticamente, com aplicação a todos os projetos em 2026-10-30 ([Tables and data](https://supabase.com/docs/guides/database/tables), [changelog](https://supabase.com/changelog/45329-breaking-change-tables-not-exposed-to-data-and-graphql-api-automatically), a confirmar 2026-10-30). As tabelas da aplicação estão em `public` (por omissão do EF Core) e incluem `users` com emails e hashes. Na T06 é obrigatório confirmar que nenhuma é alcançável por essa API (retirar os privilégios de `anon` e `authenticated`, ligar RLS sem políticas, ou tirar `public` dos esquemas publicados).

**Versão.** O desenvolvimento e os testes usam PostgreSQL 18.6; confirmar que versão o Supabase dá e correr os testes de integração nessa versão antes de implantar.

### 4.7 Segredos e variáveis de ambiente por serviço

Tirado das classes de opções e dos validadores (`ValidateOnStart`: uma definição obrigatória em falta ou inválida impede a API de arrancar, e a mensagem nunca mostra o valor). Em produção `ASPNETCORE_ENVIRONMENT` é `Production` (o valor por omissão do ASP.NET Core quando não se define), o que desliga o Swagger e as migrações no arranque e faz o `seed-demo` recusar-se.

**Serviço da API (PL-RENDER-API):**

| Variável | Obrigatória | Segredo | Valor em produção | Classe |
| --- | --- | --- | --- | --- |
| `ConnectionStrings__Default` | Sim | **Sim** | Pooler do Supabase em modo sessão, **utilizador da aplicação**, `SSL Mode=VerifyFull` (ou `Require`) | `DatabaseOptions` |
| `Jwt__SigningKey` | Sim (≥ 32 bytes) | **Sim** | Aleatório, só deste ambiente | `JwtOptions` (`Issuer` e `Audience` vêm do `appsettings.json`) |
| `Security__AllowedOrigins__0` | Sim | Não | `https://<domínio>` | `OriginCheckOptions` |
| `PublicSite__BaseUrl` | Sim | Não | `https://<domínio>` (ligações dos emails) | `PublicSiteOptions` |
| `Email__Host` | Sim | Não | Servidor SMTP do Brevo | `EmailOptions` |
| `Email__FromAddress` | Sim | Não | Remetente verificado no domínio próprio | `EmailOptions` |
| `Email__Port`, `Email__Security` | Não (587, `StartTls`) | Não | 587, `StartTls` | `EmailOptions` |
| `Email__Username`, `Email__Password` | Os dois ou nenhum | **Sim** (palavra-passe) | Credenciais SMTP do Brevo | `EmailOptions` |
| `Email__FromName`, `Email__TimeoutSeconds` | Não («Reservas de Campos», 30) | Não | — | `EmailOptions` |
| `AdminSeed__Email`, `AdminSeed__Password` | Os dois ou nenhum (nenhum = sem admin e um aviso no log) | **Sim** | A conta do admin; mudar depois não tem efeito (usa-se `set-admin-password`) | `AdminSeedOptions` |
| `ForwardedHeaders__KnownProxies__0` ou `ForwardedHeaders__KnownNetworks__0` | Não para arrancar; **sim para os limites funcionarem** (secção 4.4) | Não | Só depois da mudança da secção 4.4 | `TrustedProxyOptions` |
| `RateLimiting__Auth__*`, `RateLimiting__Public__*` | Não (valores provisórios no código) | Não | Rever na T06 | `AuthRateLimitOptions`, `PublicRateLimitOptions` |
| `Outbox__*` | Não (`Enabled=true`, lote 20, 10 s) | Não | Por omissão | `OutboxOptions` |
| `Database__ApplyMigrationsOnStartup` | Não | Não | Não definir (falso) | `Program.cs` |
| `ADMIN_NEW_PASSWORD` | Só ao correr `set-admin-password` | **Sim** | Definida só para esse comando, nunca no serviço | `AdminPasswordCommand` |

**Serviço web (PL-RENDER-WEB):** `API_INTERNAL_URL` (argumento de build e variável de execução, secção 4.5), que não é segredo, e o argumento de build `ENABLE_HSTS` (secção 4.2; `true` só com HTTPS). O Next.js não lê nenhum segredo: os cookies chegam do browser e só são passados à API.

**Passo de migração (secção 4.8):** a ligação do **dono do esquema** (segredo). Não deve estar no ambiente do serviço da API, onde o processo em execução a poderia ler.

### 4.8 Onde correm as migrações e os comandos

A Stack e o `Program.cs` dizem: em produção as migrações aplicam-se no deploy, com um migration bundle, **antes** de a API arrancar. Como o serviço da API é privado, nada se lhe chega a partir de uma máquina local. Opções, a decidir na T06:

| Opção | Como | A favor | Contra |
| --- | --- | --- | --- |
| **Job do GitHub Actions** (escolha conservadora proposta) | Depois do CI verde na `main`, um job com a ligação do dono do esquema (segredo do GitHub) corre o bundle contra o Supabase, que é alcançável da internet; só depois dispara o deploy do Render (deploy hook ou API, a confirmar) | A credencial de DDL nunca entra no Render; a ordem migração → deploy é explícita | O Supabase tem de aceitar ligações dos runners (sem lista de IPs fixos); o deploy do Render não pode estar em «deploy automático em cada push» |
| **Pre-deploy command do Render** | Corre depois do build e antes de o deploy ficar ativo; se falhar, o deploy é cancelado ([Deploying on Render](https://render.com/docs/deploys), a confirmar 2026-10-30) | Ordem garantida pelo Render | Só em planos pagos; corre com o ambiente do serviço, por isso a ligação do dono do esquema teria de estar no ambiente da API |
| **One-off job do Render** | Uma tarefa sobre o último build do serviço, cobrada ao segundo ([One-Off Jobs](https://render.com/docs/one-off-jobs), a confirmar 2026-10-30) | Serve também para os comandos | Manual ou por API; mesma questão do ambiente |

Em qualquer opção, a versão antiga da API continua a servir enquanto a nova arranca: uma migração tem de ser compatível com a versão anterior (acrescentar antes de remover).

**Comandos (C-CLI) em produção.** `seed-demo` recusa-se em Production, por isso não corre lá. `set-admin-password` lê `ADMIN_NEW_PASSWORD`, precisa da ligação do utilizador da aplicação e não arranca o servidor: corre-se como one-off job do Render ou numa shell do serviço (as duas só em planos pagos; [SSH and Shell Access](https://render.com/docs/ssh), a confirmar 2026-10-30), ou a partir de um job manual do GitHub Actions com a imagem da API e os segredos. A variável tem de existir só nesse processo.

### 4.9 Chaves do Data Protection e cópias de segurança

- As chaves que protegem os tokens de recuperação (CC-DATAPROT) vivem na tabela `data_protection_keys` (DB-DPKEYS), com o nome de aplicação fixo `SportsBooking`. Por isso um reinício, um novo deploy ou uma segunda instância continuam a aceitar as ligações já enviadas, e nada precisa de disco persistente no Render.
- O código não configura cifra das chaves em repouso (não há `ProtectKeysWith…`): **quem tem uma cópia da base de dados tem as chaves**. Uma cópia de segurança é tão sensível como os segredos.
- **Cópias (PL-BACKUP).** A Stack previa um `pg_dump` diário. No Supabase, a documentação diz que o plano gratuito não tem cópias automáticas e que o Pro tem cópias diárias com 7 dias ([Pricing](https://supabase.com/pricing), [Production Checklist](https://supabase.com/docs/guides/deployment/going-into-prod), a confirmar 2026-10-30). Sem o plano Pro, é preciso um `pg_dump` agendado (job do GitHub Actions ou cron job do Render) para um armazenamento privado, com a ligação do dono do esquema, e um teste de reposição.

## 5. Riscos e pontos em aberto para a T06

«Verificado aqui» = confirmado no repositório em 2026-10-30. «A confirmar» = depende do fornecedor; o que se encontrou na documentação está anotado com a data e a página, e tem de ser verificado outra vez antes de decidir.

| # | Ponto | Estado | O que verificar |
| --- | --- | --- | --- |
| 1 | **Serviços privados do Render: plano e custo** | A confirmar. A documentação diz que os serviços privados não têm instância gratuita (o mais barato é Starter) e que um serviço web gratuito pode enviar pedidos para serviços pagos na rede privada mas não os pode receber ([Render free](https://render.com/docs/free), [Compute plans](https://render.com/docs/compute-plans), 2026-10-30) | Preço mensal do plano Starter; se o serviço web gratuito chega à API privada |
| 2 | **API sempre ligada** | Verificado aqui: o worker (C-WORKER) corre no processo da API; um serviço parado não envia emails, e os lembretes (3 h antes) chegam atrasados ou são ignorados (`Skip(booking_started)`) | Que o plano escolhido para a API nunca adormece (a documentação diz que os planos pagos não adormecem, [Render FAQ](https://render.com/docs/faq), a confirmar 2026-10-30) |
| 3 | **O front end pode adormecer?** | A confirmar. Serviços web gratuitos adormecem após 15 minutos sem tráfego e acordam no pedido seguinte, com cerca de um minuto de espera; há 750 horas gratuitas por mês ([Render FAQ](https://render.com/docs/faq), [Render free](https://render.com/docs/free), 2026-10-30) | Se um primeiro pedido de um minuto é aceitável para uma demo de portfólio; se não, plano pago também no front end |
| 4 | **Pooler do Supabase com EF Core** | Verificado aqui: as transações usam `FOR UPDATE`, `SKIP LOCKED` e prepared statements do Npgsql. A confirmar: modo sessão na porta 5432 ([Connect to your database](https://supabase.com/docs/guides/database/connecting-to-postgres), 2026-10-30) | Testar a reserva concorrente e o worker contra o Supabase em modo sessão; limite de ligações do plano e `Maximum Pool Size`; IPv4/IPv6 entre o Render e o Supabase |
| 5 | **`btree_gist` no Supabase** | A confirmar. A lista de extensões do Supabase inclui `btree_gist` ([Postgres Extensions](https://supabase.com/docs/guides/database/extensions), 2026-10-30) | Que o papel dono do esquema a consegue criar (ou que se ativa no painel antes da primeira migração); que `AddBookings` corre sem superutilizador |
| 6 | **API de dados do Supabase** | A confirmar (secção 4.6) | Que nenhuma tabela da aplicação, sobretudo `users`, é alcançável pela API REST do Supabase |
| 7 | **Versão do PostgreSQL** | Verificado aqui: desenvolvimento e testes em 18.6. A confirmar a versão do Supabase | Correr os testes de integração nessa versão |
| 8 | **Brevo precisa de domínio próprio** | A confirmar. A documentação do Brevo diz que um endereço gratuito (Gmail, etc.) não pode ser autenticado e que a autenticação usa um código Brevo, DKIM e DMARC no DNS ([Authenticate your domain](https://help.brevo.com/hc/en-us/articles/12163873383186-Authenticate-your-domain-with-Brevo-Brevo-code-DKIM-DMARC), 2026-10-30) | Comprar ou usar um domínio (PL-DNS); registos no DNS; limite diário do plano gratuito do Brevo |
| 9 | **IP do cliente** | Verificado aqui: não implementado; com a configuração atual todos partilham um limite (secção 4.4) | Os cinco passos da secção 4.4, com um pedido real no Render |
| 10 | **Tempo e tamanho dos pedidos na plataforma** | A confirmar. Uma página do Render fala de pedidos até 100 minutos ([FastAPI on Render](https://render.com/articles/fastapi-production-best-practices), 2026-10-30); limite de tamanho do corpo não encontrado | Limites da borda; a série semanal (até 4 transações seguidas) e o envio de emails não dependem de pedidos longos |
| 11 | **Limites dos planos gratuitos** | A confirmar. Supabase: projetos gratuitos pausam após uma semana de pouca atividade e não têm cópias automáticas ([Free project pausing](https://supabase.com/kb/faqs/platform/free-project-pausing), 2026-10-30). Render: 750 horas gratuitas por mês por conta | Se a API sempre ligada evita a pausa do Supabase (o worker consulta a base a cada 10 s); se é aceitável para uma demo |
| 12 | **Migrações, `seed-demo` e `set-admin-password` sem acesso local** | Verificado aqui: a API privada não se alcança de fora; `seed-demo` recusa-se em Production. A confirmar o que cada plano do Render permite (pre-deploy, one-off jobs e shell só em planos pagos, [Deploying on Render](https://render.com/docs/deploys), [SSH](https://render.com/docs/ssh), 2026-10-30) | Escolher a opção da secção 4.8; decidir se a demo pública tem dados de demonstração (hoje impossível em Production) |
| 13 | **Ordem migração → deploy** | Verificado aqui: `Program.cs` não migra fora de Development | Como se dispara o deploy do Render depois da migração (deploy hook, «deploy depois do CI», API); compatibilidade com a versão anterior |
| 14 | **Dependabot e cabeçalhos de segurança antes da T06** | Verificado aqui: feitos (`.github/dependabot.yml`; cabeçalhos do site e da API, testados e conferidos num browser). HSTS: definição pronta e desligada (secção 4.2). A política tem `'unsafe-inline'` em scripts e estilos (limite conhecido, ver o backlog) | Ligar `ENABLE_HSTS=true` no build do serviço web; conferir os cabeçalhos num pedido real no domínio próprio |
| 15 | **URL `*.onrender.com`** | A confirmar | Se se pode desligar ou redirecionar para o domínio próprio (o `Origin` já recusa alterações vindas dele) |
| 16 | **Health check** | Verificado aqui: `GET /health` (anónimo, verifica a base de dados) | Se o Render usa health checks num serviço privado e que caminho e porta espera (a API escuta na 8080) |

## 6. Notas

- **Ficheiro.** Esta vista substitui o `06-deployment.md` previsto (que estava vazio); a correspondência no [README](README.md) foi atualizada.
- **Ids novos** (no [inventário](00-inventory.md)): `DEP-DEVHOST`, `DEP-COMPOSE-NET`, `DEP-PGVOLUME` (prefixo novo `DEP-`, nós e infraestrutura de implantação que existem hoje), `PL-DNS`, `PL-RENDER-EDGE`, `PL-RENDER-NET`, `PL-SUPAPOOL`, `PL-DEPENDABOT`, `PL-SECHEADERS`.
- **O que esta vista substitui da Stack:** ver a [ADR 0009](adr/0009-alvo-de-implantacao.md). A Stack não foi alterada.
- **Desvios encontrados entre documentos e código** (corrigidos, exceto o critério da T06 do backlog):
  - O comentário de `TrustedProxyOptions` (`Api/Security/TrustedProxyOptions.cs`) dizia que a produção confia só no Caddy. **Corrigido** (pull request de endurecimento pré-deploy, T06): diz que hoje nenhum proxy é de confiança e que o plano é confiar só no Next.js. O critério da T06 no backlog diz o mesmo que o comentário dizia (`ForwardedHeaders__KnownProxies__0` = o endereço do Caddy); no plano do Render não há Caddy (secção 4.4), e está marcado como provisório em «Pontos a rever no T06» do backlog.
  - O `AGENTS.md` dizia «Caddy in prod» para a mesma origem e para o IP do cliente. **Corrigido** (planeado, nenhum proxy de confiança hoje). O `README.md` também dizia «Production: trust only Caddy»; **corrigido** ao reescrevê-lo (pull request #29).
  - `getPublicClub` chama um endpoint com limite por IP a partir do servidor, sem o IP do cliente (secção 4.4, passo 3). Hoje, no compose, todas as páginas de clube já partilham a partição do contentor do Next.js.
  - O comentário de `MigrateDatabaseAsync` e o de `Program.cs` diziam que a produção usa um migration bundle, que ainda não existe. **Corrigido** (pull request de endurecimento pré-deploy, T06): dizem que é uma decisão da T06 (ADR 0009, proposta).
  - O diagrama [02-containers-dev.svg](diagrams/02-containers-dev.svg) mostrava o rótulo da seta C-WEB → C-API partido, porque `http://` no argumento de tecnologia de um `Rel` é lido como itálico pelo PlantUML. **Corrigido** (a tecnologia é agora «HTTP, api:8080»); nos diagramas da vista física o esquema também foi tirado desses rótulos.
