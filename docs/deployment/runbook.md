# Runbook de implantação (T06)

Os passos que só o dono do projeto pode fazer, pela ordem certa. O alvo é Vercel, Render, Supabase, Brevo e GitHub Actions, com **custo zero**. As variáveis estão explicadas em [environment-variables.md](environment-variables.md).

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

- [ ] Cria uma conta e um projeto no plano gratuito, numa **região perto de Frankfurt**. Guarda `<OWNER_PASSWORD>`.
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
- [ ] **Confirma na documentação atual do Brevo** o servidor (`smtp-relay.brevo.com`), a porta **2525** e se essa porta oferece STARTTLS. O plano gratuito do Render bloqueia a saída nas portas 25, 465 e 587.

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
  | `ForwardedHeaders__ForwardLimit` | `1` (ver a secção 8) |
  | `Email__Host` | `smtp-relay.brevo.com` (a confirmar) |
  | `Email__Port` | `2525` (a confirmar) |
  | `Email__Security` | `StartTls` (a confirmar) |
  | `Email__Username` | `<SMTP_LOGIN>` |
  | `Email__Password` | `<SMTP_KEY>` |
  | `Email__FromAddress` | `<REMETENTE>` |

- [ ] Não definas `PORT` (o Render dá-a), `AdminSeed__*`, `ADMIN_NEW_PASSWORD` nem `DemoData__AllowInProduction`. O admin é criado pelo `prod-data`.
- [ ] Copia o **Deploy Hook** do serviço: é o `<DEPLOY_HOOK>`. É um segredo: quem o tiver pode lançar deploys.

## 5. Vercel

- [ ] Cria um projeto a partir do repositório, numa conta **Hobby** (só uso não comercial).
- [ ] **Root Directory:** `frontend`. Framework: Next.js.
- [ ] Variáveis de ambiente, no ambiente Production:

  | Nome | Valor |
  | --- | --- |
  | `API_INTERNAL_URL` | `<RENDER_URL>` |
  | `ENABLE_HSTS` | `true` |

- [ ] Mantém ligada a exposição das variáveis de sistema da Vercel (o nome pode variar).
- [ ] Se existir a definição, escolhe uma região das Functions perto de Frankfurt, para os Server Components ficarem perto da API.
- [ ] **Desliga os deploys automáticos do Git**, para só o workflow publicar: desliga o repositório nas definições Git do projeto (o nome pode variar). A CLI funciona sem ligação ao Git.
- [ ] Cria um token de acesso na tua conta: é o `<VERCEL_TOKEN>`.
- [ ] Para obter os ids, numa pasta temporária fora do repositório, corre `npx vercel@65.2.0 link` e lê `.vercel/project.json`: `orgId` é o `<VERCEL_ORG_ID>` e `projectId` é o `<VERCEL_PROJECT_ID>`.

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
  Se for o teu endereço, está certo (em IPv6, aparece a tua rede /64). Se não for, sobe `ForwardedHeaders__ForwardLimit` em 1, faz um novo deploy e repete. Usa o valor mais baixo que dê o endereço certo.
- [ ] Muda o estado da ADR 0009 depois deste primeiro deploy.

## 9. Limitações conhecidas

- **Arranque a frio:** a API adormece ao fim de 15 minutos sem tráfego e demora cerca de um minuto a acordar.
- **Emails e lembretes** só saem enquanto a API estiver acordada; um lembrete cuja reserva já começou é ignorado.
- **Rate limiting de melhor esforço:** a API é pública, e quem lhe ligar diretamente pode forjar o IP. A proteção real contra adivinhar palavras-passe é o bloqueio de conta.
- **As palavras-passe das contas de demonstração são públicas** (estão no README): qualquer pessoa pode entrar com elas enquanto a demo estiver online.
- **Supabase:** um projeto gratuito com pouca atividade durante uma semana é pausado. Reativa-o no painel.
- **Sem cópias de segurança** e sem domínio próprio.

## 10. Desligar tudo e evitar qualquer cobrança

- [ ] **GitHub:** muda `DEPLOY_ENABLED` para `false` e apaga os secrets e as variables da secção 6.
- [ ] **Render:** apaga o serviço web.
- [ ] **Vercel:** apaga o projeto e revoga o token.
- [ ] **Supabase:** apaga o projeto.
- [ ] **Brevo:** apaga a chave SMTP.
- [ ] Confirma que nenhum dos fornecedores tem um meio de pagamento associado.
