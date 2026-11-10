# Variáveis de ambiente de produção (T06)

Alvo: **Next.js na Vercel** (Hobby), **API em Docker no Render** (plano gratuito), **PostgreSQL no Supabase**, **SMTP no Brevo** e **GitHub Actions** para os passos do deploy. O browser só fala com a Vercel; o Next.js reescreve `/api/*` para a API, por isso os cookies são first-party e não há CORS.

Aqui só há formas de exemplo, **nunca valores reais**. Uma tabela por sítio. Os passos manuais estão no [runbook](runbook.md). Conferido em 2026-11-09; o que depende de um fornecedor está na última secção, com o que foi lido, o que o primeiro deploy real confirmou e o que continua por confirmar.

## O que quer dizer «lida no build»

`API_INTERNAL_URL` é lida duas vezes, em momentos diferentes:

- **No build** (`next build`, em `next.config.ts`): o destino do rewrite de `/api/*` fica **escrito no resultado do build**. Mudar a variável não altera um deploy que já existe: é preciso um **novo deploy**.
- **Em execução** (`lib/server/api-url.ts`, código só de servidor): os Server Components chamam a API com o cookie do utilizador. Não é `NEXT_PUBLIC_`, por isso nunca chega ao browser.

Na Vercel é a mesma variável para os dois momentos. O build falha de propósito se ela faltar, não for uma origem simples (sem caminho nem barra final) ou não for `https`. Essa verificação usa a variável de sistema `VERCEL`, por isso a opção «Automatically expose System Environment Variables» do projeto tem de ficar ligada.

Quando o build recusa o valor, o erro mostra o que recebeu, entre aspas e com os caracteres invisíveis escritos como `\uXXXX`, e o motivo (vazio, barra final, caminho, esquema errado, espaços, aspas, caracteres invisíveis). A origem não é um segredo, por isso pode aparecer no registo do build.

`ENABLE_HSTS` também é lida só no build: os cabeçalhos ficam escritos no resultado.

## Vercel

Root Directory do projeto: `frontend`.

| Nome | Quando é lida | Para que serve | Forma de exemplo | De onde vem |
| --- | --- | --- | --- | --- |
| `API_INTERNAL_URL` | Build e execução | Destino do rewrite de `/api/*` e endereço usado pelos Server Components | `https://<serviço>.onrender.com` | URL público do serviço no Render, sem barra final |
| `ENABLE_HSTS` | Build | Liga o HSTS e o `upgrade-insecure-requests` no site servido por HTTPS | `true` | Escolha; só com HTTPS |

Depois de mudar qualquer uma, é preciso um novo deploy. Os previews da Vercel têm outro endereço, que não está em `Security__AllowedOrigins__*`: os pedidos que alteram dados recebem 403 `origin_not_allowed`.

## Render

Serviço web do tipo Docker, com `backend/Dockerfile` e `backend` como contexto. O Render passa as variáveis de ambiente como argumentos de build; o `Dockerfile` não usa nenhum, por isso nenhum segredo fica na imagem. A API não aplica migrações no arranque em Production.

| Nome | Obrigatória | Para que serve | Forma de exemplo | De onde vem |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Sim | Liga as regras de produção | `Production` | Fixo |
| `PORT` | Dada pelo Render | Porta em que a API escuta, em `0.0.0.0` (`8080` se não existir) | `10000` | Render |
| `ConnectionStrings__Default` | **Sim, a API não arranca sem ela** | Ligação da API à base de dados, como `sportsbooking_app`, sem direitos de DDL (segredo) | `Host=<pooler>;Port=5432;Database=postgres;Username=sportsbooking_app.<ref>;Password=<…>;SSL Mode=Require;Maximum Pool Size=5` | Supabase, modo sessão (porta 5432), nunca o modo transação. Formato do Npgsql, não `postgresql://`. O pool é pequeno porque o plano gratuito tem 15 ligações |
| `Jwt__SigningKey` | **Sim, com pelo menos 32 bytes** | Assina os access tokens (segredo) | `<48 bytes aleatórios em base64>` | `openssl rand -base64 48`; só deste ambiente |
| `Security__AllowedOrigins__0` | **Sim, a API não arranca sem ela** | Única origem que pode fazer POST, PUT, PATCH e DELETE | `https://<projeto>.vercel.app` | O endereço público do site, sem barra final |
| `PublicSite__BaseUrl` | **Sim** | Endereço do site nas ligações dos emails | `https://<projeto>.vercel.app` | Igual ao anterior |
| `ForwardedHeaders__TrustAnyProxy` | Sim, para os limites por IP funcionarem | Acredita em `X-Forwarded-For` e `X-Forwarded-Proto` vindos de qualquer ligação (ver abaixo) | `true` | Fixo neste alvo |
| `ForwardedHeaders__ForwardLimit` | Não (1) | Quantas entradas se leem, da direita para a esquerda | `1` | Confirmado no primeiro deploy (2026-11-09); ver «Encontrar o `ForwardLimit`» se mudares de fornecedor |
| `AdminSeed__Email`, `AdminSeed__Password` | Não definir aqui | O admin é criado pelo `prod-data`. Definidas, a API passaria a precisar da base de dados para arrancar | — | Sem elas, a API avisa no log e arranca |
| `Email__Host` | Com `Email__FromAddress`, ou nenhuma | Servidor SMTP | `smtp-relay.brevo.com` | Brevo |
| `Email__Port` | Não (587) | Porta SMTP | `2525` | Funcionou no primeiro deploy. O plano gratuito do Render bloqueia a saída nas portas 25, 465 e 587 |
| `Email__Security` | Não (`StartTls`) | `StartTls`, `SslOnConnect` ou `None` | `StartTls` (funcionou na porta 2525 no primeiro deploy) | Nunca `None` com credenciais reais |
| `Email__Username`, `Email__Password` | As duas ou nenhuma | Credenciais SMTP (segredo: a chave SMTP, não a chave de API) | `<login SMTP>`, `<chave SMTP>` | Brevo, «SMTP & API» |
| `Email__FromAddress` | Com `Email__Host`, ou nenhuma | Remetente | `reservas@<domínio verificado>` | Remetente verificado no Brevo |
| `Email__FromName`, `Email__TimeoutSeconds` | Não | Nome do remetente; limite de cada envio | `Reservas de Campos`, `30` | Opcional |
| `RateLimiting__Auth__*`, `RateLimiting__Public__*` | Não | Limites por IP | `RateLimiting__Public__ReadPermitLimit=120` | Opcional (valores provisórios) |
| `Outbox__*` | Não | Worker de emails | `Outbox__PollIntervalSeconds=10` | Opcional |

**Não definir no Render:** `ADMIN_NEW_PASSWORD` e `DemoData__AllowInProduction` (só para o processo do comando, abaixo). `Database__ApplyMigrationsOnStartup` é ignorada em Production.

**Sem SMTP** (`Email__Host` e `Email__FromAddress` os dois vazios), a API arranca em Production, avisa no log e não envia nada; as mensagens ficam no outbox até o SMTP existir. Só uma das duas definida, ou um valor inválido, impede o arranque. Reservas e cancelamentos nunca dependem do envio.

## GitHub Actions (segredos)

Os comandos correm no runner contra o Supabase, porque o plano gratuito do Render não tem shell nem one-off jobs. Arrancam o mesmo host da API, por isso as definições obrigatórias têm de existir: os workflows dão-nas, e nenhuma delas é um segredo adicional.

| Nome | Usado por | Para que serve | Forma de exemplo | De onde vem |
| --- | --- | --- | --- | --- |
| `DB_OWNER_CONNECTION_STRING` | `deploy.yml`, `prod-data.yml` | Ligação como `postgres`, para as migrações e os comandos (segredo) | `Host=<pooler>;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<…>;SSL Mode=Require` | Supabase, modo sessão (porta 5432). O runner não tem IPv6, por isso nunca o anfitrião direto |
| `RENDER_DEPLOY_HOOK_URL` | `deploy.yml` | Lança o deploy da API (segredo) | `https://api.render.com/deploy/srv-<…>?key=<…>` | Render |
| `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` | `deploy.yml` | Publicam o site com a CLI (segredos) | `<token>`, `team_<…>`, `prj_<…>` | Vercel |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | `prod-data.yml` (`set-admin-password`) | Criam o admin, se não existir, e definem a sua palavra-passe, sempre pelo ambiente (segredos) | `admin@<domínio>`, `<palavra-passe ≥ 8>` | Escolha |

Variáveis do repositório (não são segredos):

| Nome | Para que serve | Forma de exemplo |
| --- | --- | --- |
| `DEPLOY_ENABLED` | Sem o valor `true`, os dois workflows não fazem nada | `true` |
| `RENDER_API_URL` | Onde o workflow confirma `/health` | `https://<serviço>.onrender.com` |
| `SITE_URL` | O smoke e as definições do arranque dos comandos | `https://<projeto>.vercel.app` |

Definidos no próprio workflow: `ASPNETCORE_ENVIRONMENT=Production`, `Security__AllowedOrigins__0` e `PublicSite__BaseUrl` (a partir de `SITE_URL`), `Jwt__SigningKey` (gerada em cada execução e nunca usada) e, só no passo do `seed-demo`, `DemoData__AllowInProduction=true`. As migrações leem `ConnectionStrings__Default` do ambiente, por isso a ligação nunca aparece numa linha de comandos.

Sem `DemoData__AllowInProduction=true`, o `seed-demo` recusa-se em Production (código de saída 1); com ela, continua a recusar uma base de dados que já tenha donos ou jogadores. As contas de demonstração têm a palavra-passe pública do README: qualquer pessoa pode entrar com elas enquanto a demo estiver online.

## Encontrar o `ForwardLimit`

**Provisório.** A API é pública no Render, por isso quem lhe ligar diretamente pode enviar o seu próprio `X-Forwarded-For`: o limite por IP é só de melhor esforço, e a proteção real contra adivinhar palavras-passe é o bloqueio de conta do Identity (5 tentativas, 15 minutos).

Quantas entradas chegam à API depende da Vercel, da Cloudflare e dos balanceadores do Render, e isso não está documentado. Para o descobrir, depois de um deploy novo:

1. Com `ForwardedHeaders__ForwardLimit=1`, fazer mais de 10 logins num minuto, no site, a partir da própria ligação, com um email que não exista (para não bloquear nenhuma conta), até receber 429.
2. Procurar no log do Render a linha `Too many requests from client <endereço> to /api/auth/login`.
3. Se o endereço for o da própria ligação, está certo. Se for outro (da Vercel, por exemplo), subir o limite em 1 e repetir, com um novo deploy.
4. Usar o valor mais baixo que dê o endereço certo: quanto maior, mais fácil é forjá-lo.

**Resultado do primeiro deploy (2026-11-09):** o valor que deu o IP certo do cliente foi `1`.

## Limites conhecidos desta configuração

- **Render gratuito:** o serviço adormece ao fim de 15 minutos sem tráfego e demora cerca de um minuto a acordar. Enquanto dorme, o worker de emails para: os emails saem quando alguém acordar a API, e um lembrete cuja reserva já começou é ignorado.
- **Primeiro pedido depois de adormecer:** a Vercel espera no máximo 120 segundos por um rewrite externo.
- **Base de dados parada:** `/health` só diz que o processo está vivo e não toca na base de dados. Mas com `AdminSeed__*` definidas, a API lê a base no arranque e não arranca se ela não responder.

## O que foi confirmado nos fornecedores (2026-11-09)

**Lido na documentação atual:**

- **Vercel** ([Request headers](https://vercel.com/docs/headers/request-headers), [Rewrites](https://vercel.com/docs/rewrites), [Limits](https://vercel.com/docs/limits), [Environment variables](https://vercel.com/docs/environment-variables)): reescreve o `X-Forwarded-For` com o IP público do visitante; num rewrite externo envia à origem `x-forwarded-for`, `x-real-ip`, `x-forwarded-host` e `x-forwarded-proto`; limite de 120 segundos; a alteração de uma variável só vale num novo deploy; `VERCEL` existe no build e em execução, se as variáveis de sistema estiverem expostas.
- **Render** ([Free](https://render.com/docs/free), [Web services](https://render.com/docs/web-services), [Docker](https://render.com/docs/docker), [Health checks](https://render.com/docs/health-checks), [artigo sobre DDoS](https://render.com/articles/how-render-handles-ddos-attacks)): adormece após 15 minutos, acorda em cerca de um minuto; portas 25, 465 e 587 bloqueadas à saída; sem shell nem one-off jobs; `PORT` (por omissão 10000) e `0.0.0.0`; o tráfego passa pela Cloudflare e pelos balanceadores do Render, e a aplicação deve ler `x-forwarded-for`; as variáveis de ambiente passam a argumentos de build.
- **Brevo** ([SMTP integration](https://developers.brevo.com/docs/smtp-integration)): portas 587 e 2525, e 465 com SSL/TLS; usar uma chave SMTP.

- **Supabase** ([Connecting to Postgres](https://supabase.com/docs/guides/database/connecting-to-postgres), [IPv4](https://supabase.com/docs/guides/platform/ipv4-address), [Securing the Data API](https://supabase.com/docs/guides/api/securing-your-api)): o anfitrião direto é IPv6, ou IPv4 com um add-on pago; o pooler partilhado é IPv4; o modo sessão usa a porta 5432 e suporta prepared statements; o utilizador é `<papel>.<project-ref>`; `sslmode=require` é o recomendado.

**Confirmado no primeiro deploy real (2026-11-09):**

- `ForwardedHeaders__ForwardLimit=1` deu o IP certo do cliente.
- Brevo: o SMTP na porta 2525 com `StartTls` funcionou.
- O login e um pedido que altera dados pelo rewrite da Vercel funcionaram, com o `Origin` e o `Set-Cookie`.
- Cache da Vercel nos rewrites: nada ficou em cache.
- Render: o Dockerfile, o contexto de build e o health check usados foram os do runbook.
- Nada mais diferiu do que a documentação dizia.

**Não reportado (continua por confirmar):**

- O `X-Forwarded-For` nas chamadas dos Server Components a caminho do Render: o valor `1` foi confirmado, mas o resultado reportado não as distingue.
- A versão do PostgreSQL que o Supabase dá aos projetos novos.
