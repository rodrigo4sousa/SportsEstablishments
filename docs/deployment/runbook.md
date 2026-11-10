# Runbook de implantação (T06)

Os passos que só o dono do projeto pode fazer, pela ordem certa. O alvo é Vercel, Render, Supabase, Brevo e GitHub Actions, com **custo zero**. As variáveis estão explicadas em [environment-variables.md](environment-variables.md).

Estes passos foram seguidos no primeiro deploy real (2026-11-09), sem diferenças em relação ao que está escrito.

Os nomes dos botões e campos podem mudar nos painéis dos fornecedores: onde está «(o nome pode variar)», procura a definição descrita.

## Valores a anotar

Guarda estes valores num sítio privado. Nunca os escrevas no repositório.

| Valor | De onde vem |
| --- | --- |
| `<REF>` | Referência do projeto Supabase |
| `<POOLER_HOST>` | Anfitrião do pooler em modo sessão, do Supabase |
| `<OWNER_PASSWORD>` | Palavra-passe da base de dados, escolhida ao criar o projeto Supabase |
| `<APP_PASSWORD>` | Palavra-passe de `sportsbooking_app`, escolhida por ti (longa e aleatória, só letras e números) |
| `<SMTP_LOGIN>`, `<SMTP_KEY>`, `<REMETENTE>` | Brevo |
| `<RENDER_URL>` | `https://<serviço>.onrender.com`, sem barra final |
| `<VERCEL_URL>` | `https://<projeto>.vercel.app`, sem barra final (o endereço de produção, não o de um deploy) |
| `<DEPLOY_HOOK>` | Render |
| `<VERCEL_TOKEN>`, `<VERCEL_ORG_ID>`, `<VERCEL_PROJECT_ID>` | Vercel |

## 1. Supabase

- [ ] Cria uma conta e um projeto no plano gratuito, numa **região perto de Frankfurt**. Guarda `<OWNER_PASSWORD>`. Na instalação atual é West EU (Irlanda), a região que a Política de Privacidade indica.
- [ ] Anota `<REF>`.
- [ ] No SQL Editor, confirma que a extensão está disponível (tem de devolver uma linha). **Não a atives**: a migração cria-a.
  ```sql
  select name from pg_available_extensions where name = 'btree_gist';
  ```
- [ ] Abre [`supabase-roles.sql`](supabase-roles.sql), troca `REPLACE_WITH_A_LONG_RANDOM_PASSWORD` por `<APP_PASSWORD>`, cola no SQL Editor e corre. Corre **antes** da primeira migração. Não guardes o ficheiro alterado no repositório.
- [ ] No diálogo de ligação do projeto, escolhe o **Session pooler** e copia o anfitrião: é o `<POOLER_HOST>`. Não o deduzas da região.
- [ ] Monta as duas ligações. **Têm de estar neste formato**, não no formato `postgresql://` que o painel mostra:
  - Dono (segredo do GitHub):
    `Host=<POOLER_HOST>;Port=5432;Database=postgres;Username=postgres.<REF>;Password=<OWNER_PASSWORD>;SSL Mode=Require`
  - Aplicação (Render):
    `Host=<POOLER_HOST>;Port=5432;Database=postgres;Username=sportsbooking_app.<REF>;Password=<APP_PASSWORD>;SSL Mode=Require;Maximum Pool Size=5`
- [ ] Usa sempre a porta **5432** do pooler. Não uses o anfitrião direto `db.<REF>.supabase.co` (é IPv6 e os runners do GitHub não têm IPv6) nem o modo transação (porta 6543).

## 2. Brevo

- [ ] Cria uma conta gratuita.
- [ ] Adiciona e **verifica um remetente por email**: é o `<REMETENTE>`. Sem domínio próprio, os emails podem ir para o spam.
- [ ] Nas definições de SMTP e API, gera uma **chave SMTP** (não uma chave de API). Anota `<SMTP_LOGIN>` e `<SMTP_KEY>`.
- [ ] Usa o servidor `smtp-relay.brevo.com` e a porta **2525** com STARTTLS (funcionou no primeiro deploy). O plano gratuito do Render bloqueia a saída nas portas 25, 465 e 587.

## 3. As duas URLs dependem uma da outra

O Render precisa do endereço da Vercel (`Security__AllowedOrigins__0`, `PublicSite__BaseUrl`) e a Vercel precisa do endereço do Render (`API_INTERNAL_URL`). Para quebrar o ciclo:

1. Cria primeiro o serviço no Render (secção 4), com `https://placeholder.invalid` nas duas variáveis do site. O primeiro deploy pode falhar, sem problema: o endereço já existe. Anota `<RENDER_URL>`.
2. Cria o projeto na Vercel (secção 5), com `API_INTERNAL_URL=<RENDER_URL>`. Anota `<VERCEL_URL>`.
3. Volta ao Render e troca as duas variáveis pelo `<VERCEL_URL>` real.

## 4. Render

- [ ] Cria um **Web Service** a partir do repositório do GitHub, ramo `main`.
- [ ] **Não adiciones nenhum meio de pagamento.** Sem ele, ultrapassar um limite suspende o serviço em vez de cobrar.
- [ ] Definições:

  | Definição (o nome pode variar) | Valor |
  | --- | --- |
  | Language / Runtime | `Docker` |
  | Root Directory | `backend` |
  | Dockerfile Path | `Dockerfile` (relativo ao Root Directory) |
  | Docker Build Context Directory | `.` (relativo ao Root Directory) |
  | Instance Type | `Free` |
  | Region | Frankfurt |
  | Auto-Deploy | **Off** |
  | Health Check Path, se existir no plano gratuito | `/health` |

- [ ] Variáveis de ambiente:

  | Nome | Valor |
  | --- | --- |
  | `ASPNETCORE_ENVIRONMENT` | `Production` |
  | `ConnectionStrings__Default` | A ligação da **aplicação** (secção 1) |
  | `Jwt__SigningKey` | O resultado de `openssl rand -base64 48` |
  | `Security__AllowedOrigins__0` | `<VERCEL_URL>` |
  | `PublicSite__BaseUrl` | `<VERCEL_URL>` |
  | `ForwardedHeaders__TrustAnyProxy` | `true` |
  | `ForwardedHeaders__ForwardLimit` | `1` (confirmado no primeiro deploy; ver a secção 8) |
  | `Email__Host` | `smtp-relay.brevo.com` |
  | `Email__Port` | `2525` |
  | `Email__Security` | `StartTls` |
  | `Email__Username` | `<SMTP_LOGIN>` |
  | `Email__Password` | `<SMTP_KEY>` |
  | `Email__FromAddress` | `<REMETENTE>` |

- [ ] Não definas `PORT` (o Render dá-a), `AdminSeed__*`, `ADMIN_NEW_PASSWORD` nem `DemoData__AllowInProduction`. O admin é criado pelo `prod-data`.
- [ ] Copia o **Deploy Hook** do serviço: é o `<DEPLOY_HOOK>`. É um segredo: quem o tiver pode lançar deploys.

## 5. Vercel

O projeto `sports-establishments` **já existe** na Vercel e tem de ficar com o Git desligado: ligado, cada push lança um deploy fora do workflow, que falha sem `API_INTERNAL_URL`. Se o estiveres a usar, salta a criação e confirma as definições abaixo.

- [ ] Se o projeto não existir, cria-o a partir do repositório, numa conta **Hobby** (só uso não comercial).
- [ ] **Root Directory:** `frontend`. Framework: Next.js.
- [ ] Variáveis de ambiente, no ambiente Production:

  | Nome | Valor |
  | --- | --- |
  | `API_INTERNAL_URL` | `<RENDER_URL>` |
  | `ENABLE_HSTS` | `true` |

- [ ] Mantém ligada a exposição das variáveis de sistema da Vercel (o nome pode variar).
- [ ] Se existir a definição, escolhe uma região das Functions perto de Frankfurt, para os Server Components ficarem perto da API. Na instalação atual é Washington DC (EUA). Se mudares uma região, atualiza a Política de Privacidade (`/privacy`), que as indica.
- [ ] **Desliga os deploys automáticos do Git**, para só o workflow publicar: desliga o repositório nas definições Git do projeto (o nome pode variar) e retira também à app da Vercel no GitHub o acesso a este repositório. A CLI funciona sem ligação ao Git. Confirma num commit novo: não pode aparecer nenhum check `Vercel` no GitHub.
- [ ] Cria um token de acesso na tua conta: é o `<VERCEL_TOKEN>`.
- [ ] Para obter os ids, numa pasta temporária fora do repositório, corre `npx vercel@65.14.0 link` e lê `.vercel/project.json`: `orgId` é o `<VERCEL_ORG_ID>` e `projectId` é o `<VERCEL_PROJECT_ID>`.

## 6. GitHub

Em Settings → Secrets and variables → Actions:

- [ ] **Secrets:**

  | Nome | Valor |
  | --- | --- |
  | `DB_OWNER_CONNECTION_STRING` | A ligação do **dono** (secção 1) |
  | `RENDER_DEPLOY_HOOK_URL` | `<DEPLOY_HOOK>` |
  | `VERCEL_TOKEN` | `<VERCEL_TOKEN>` |
  | `VERCEL_ORG_ID` | `<VERCEL_ORG_ID>` |
  | `VERCEL_PROJECT_ID` | `<VERCEL_PROJECT_ID>` |
  | `ADMIN_EMAIL` | O email do admin |
  | `ADMIN_PASSWORD` | A palavra-passe do admin (pelo menos 8 caracteres) |

- [ ] **Variables:**

  | Nome | Valor |
  | --- | --- |
  | `RENDER_API_URL` | `<RENDER_URL>` |
  | `SITE_URL` | `<VERCEL_URL>` |
  | `DEPLOY_ENABLED` | `true`, **só depois** de tudo acima estar feito |

## 7. Primeira execução, por esta ordem

- [ ] Em Actions → **Production data** → Run workflow, ramo `main`, ação **`migrate-only`**. Tem de terminar a verde.
- [ ] Em Actions → **Deploy**, volta a correr a última execução (Re-run all jobs), ou faz merge de uma alteração na `main`. Tem de terminar a verde, incluindo o `smoke`.
- [ ] **Production data** → **`set-admin-password`**. Cria o admin, se ainda não existir, com `ADMIN_EMAIL` e `ADMIN_PASSWORD`.
- [ ] **Production data** → **`seed-demo`**, se quiseres dados de demonstração. O log diz que correu.

Cada migração tem de funcionar com a versão anterior da API, porque o esquema novo fica ativo antes da API nova.

## 8. Verificação

- [ ] Em `<VERCEL_URL>`, entra com o admin e confirma que vais para `/admin`.
- [ ] Nas ferramentas do browser, confirma os cookies `access_token` e `refresh_token` no domínio da Vercel.
- [ ] Entra com um jogador de demonstração (ver o README), faz uma reserva e cancela-a.
- [ ] Confirma o email: o log do Render não deve mostrar falhas do worker, e a mensagem chega à caixa do jogador. Os jogadores de demonstração usam `example.test`, que não recebe email: para ver um email real, regista um jogador com um endereço teu. No SQL Editor:
  ```sql
  select type, sent_at, attempts, last_error from outbox_messages order by created_at desc limit 10;
  ```
- [ ] Confirma que nenhuma tabela é alcançável pela API de dados do Supabase. Tem de devolver `0`:
  ```sql
  select count(*) from information_schema.role_table_grants
  where grantee in ('anon', 'authenticated', 'service_role') and table_schema = 'public';
  ```
- [ ] **`ForwardLimit`:** com o valor `1`, faz mais de 10 logins num minuto no site, a partir da tua ligação, com um email que não exista, até receberes 429. Procura no log do Render:
  ```
  Too many requests from client <endereço> to /api/auth/login
  ```
  Se for o teu endereço, está certo (em IPv6, aparece a tua rede /64). Se não for, sobe `ForwardedHeaders__ForwardLimit` em 1, faz um novo deploy e repete. Usa o valor mais baixo que dê o endereço certo. No primeiro deploy, o valor `1` deu o endereço certo.
- [x] O estado da ADR 0009 passou a «Aceite» depois do primeiro deploy (2026-11-09).

## 9. Limitações conhecidas

- **Arranque a frio:** a API adormece ao fim de 15 minutos sem tráfego e demora cerca de um minuto a acordar.
- **Emails e lembretes** só saem enquanto a API estiver acordada; um lembrete cuja reserva já começou é ignorado.
- **Rate limiting de melhor esforço:** a API é pública, e quem lhe ligar diretamente pode forjar o IP. A proteção real contra adivinhar palavras-passe é o bloqueio de conta.
- **As palavras-passe das contas de demonstração são públicas** (estão no README): qualquer pessoa pode entrar com elas enquanto a demo estiver online.
- **Supabase:** um projeto gratuito com pouca atividade na base de dados durante 7 dias é pausado. O workflow `keepalive.yml` faz uma consulta por esse motivo (secção 10), mas não é garantia: se o projeto for pausado, reativa-o no painel («Resume project»).
- **Sem cópias de segurança** e sem domínio próprio.

## 10. Manter a demo ativa

O plano gratuito do Supabase pausa um projeto com pouca atividade, e o `/health` não toca na base de dados, por isso não conta. O workflow `.github/workflows/keepalive.yml` faz um `GET {SITE_URL}/api/localities` pelo site público: é uma consulta de leitura à base de dados (os clubes visíveis), que também acorda a API no Render. Não usa segredos, só a variável `SITE_URL`, e não corre enquanto `DEPLOY_ENABLED` não for `true`.

- **Quando corre:** às segundas e quintas-feiras às 07:23 UTC, e à mão.
- **O que faz:** pede com `Cache-Control: no-cache` e um parâmetro `keepalive=<hora>` que muda de cada vez. Cada pedido espera até 130 s (um arranque a frio do Render demora cerca de um minuto e a Vercel dá 120 s ao rewrite); se falhar, repete de 20 em 20 s durante cerca de 5 minutos. Dá sucesso com HTTP 200 e um corpo que seja JSON. Nunca escreve o corpo no registo. Se não conseguir, o job falha com uma mensagem a dizer para ver o Render e o painel do Supabase.
- **Correr à mão:** Actions → Keepalive → *Run workflow*, ou `gh workflow run keepalive.yml`.
- **Desligar:** muda `DEPLOY_ENABLED` para `false` (desliga também o deploy), ou desativa só este workflow em Actions → Keepalive → «⋯» → *Disable workflow* (`gh workflow disable keepalive.yml`).
- **Só corre na branch por omissão** (`main`): a documentação do GitHub diz que os workflows agendados só correm aí, no último commit. O workflow não pôde ser experimentado antes de entrar na `main`; a primeira execução pode ser pedida à mão com `workflow_dispatch`.

**O que dizem as documentações** (lidas a 2026-11-09, sem data de atualização nas páginas):

- **Supabase** ([free-project-pausing](https://supabase.com/docs/guides/platform/free-project-pausing)): «Supabase pauses Free Plan projects that show low activity over a 7-day period» e um projeto é considerado inativo se não tiver «sufficient user database activity over the past week»; «Typically a few user requests to the database each day over the previous week is enough». O limiar de «low activity» não é dado em números. Avisam por email cerca de uma semana antes de pausar. Um projeto pausado pode ser restaurado até 1 ano depois, no painel (*Resume project*). **Não é claro** se uma visita ao painel conta como atividade na base de dados (a página sugere-o como forma de gerar atividade), nem se uma consulta duas vezes por semana chega: o texto fala em alguns pedidos por dia. Por isso a frequência é **provisória**.
- **GitHub** ([disabling and enabling a workflow](https://docs.github.com/en/actions/managing-workflow-runs-and-deployments/managing-workflow-runs/disabling-and-enabling-a-workflow)): «In a public repository, scheduled workflows are automatically disabled when no repository activity has occurred in 60 days.» A documentação **não define** «repository activity», e as páginas lidas não dizem se o dono é avisado. Reativar: Actions → o workflow → *Enable workflow*, ou `gh workflow enable keepalive.yml`; um commit com permissões de escrita que mude o `cron` também o reativa. A execução agendada pode atrasar-se nas horas de carga (por isso 07:23 e não 07:00) e, com muita carga, alguns jobs podem ser descartados.

## 11. Desligar tudo e evitar qualquer cobrança

- [ ] **GitHub:** muda `DEPLOY_ENABLED` para `false` e apaga os secrets e as variables da secção 6.
- [ ] **Render:** apaga o serviço web.
- [ ] **Vercel:** apaga o projeto e revoga o token.
- [ ] **Supabase:** apaga o projeto.
- [ ] **Brevo:** apaga a chave SMTP.
- [ ] Confirma que nenhum dos fornecedores tem um meio de pagamento associado.
