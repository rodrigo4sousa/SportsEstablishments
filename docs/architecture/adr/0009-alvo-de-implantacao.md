# 0009 · Alvo de implantação

- **Estado:** Proposta (o código e os workflows da T06 estão feitos; muda depois do primeiro deploy real).
- **Data:** 2026-11-09 (reescrita; a primeira versão, de 2026-10-30, propunha a API num serviço privado do Render).
- **Elementos:** C-WEB, C-API, C-WORKER, C-DB, X-GHA, PL-VERCEL, PL-VERCEL-EDGE, PL-RENDER, PL-RENDER-EDGE, PL-RENDER-API, PL-SUPABASE, PL-SUPAPOOL, PL-BREVO, PL-DBUSERS, PL-MIGBUNDLE, PL-DEPLOY, PL-DEPLOYHOOK, PL-PRODDATA. Detalhe na [vista física](../06-physical-view.md).

## Contexto

A T06 pede um URL público com HTTPS, deploy automático depois de cada merge na `main` com o CI verde, segredos só em variáveis de ambiente, migrações aplicadas no deploy e dois utilizadores da base de dados. A aplicação é uma demonstração de portfólio, que vai ser desligada mais tarde: **o custo tem de ser zero**.

O que o código impõe a qualquer alvo:

- **Uma só origem.** A sessão vive em cookies httpOnly e a API verifica o `Origin`: o browser tem de ver as páginas e `/api/*` no mesmo endereço.
- **O worker de emails** (C-WORKER) corre dentro da API; se a API parar, os emails param.
- **PostgreSQL com `btree_gist`** para a `bookings_no_overlap`, transações com `FOR UPDATE` e `SKIP LOCKED`, e as chaves do Data Protection na base de dados (nada precisa de disco local).
- **SMTP** para o Brevo.

Alternativas consideradas:

1. **Máquina virtual com Caddy** (a da Stack). Tem um custo fixo e obriga a administrar o sistema, os certificados e as cópias.
2. **Os dois no Render, com a API privada** (a primeira versão desta ADR). Os serviços privados não têm plano gratuito, por isso não cumpre o custo zero.
3. **Vercel (front end) + Render gratuito (API pública).** Custo zero. A API fica com um URL público, e o Next.js continua a ser o proxy da mesma origem.

## Decisão

Proposta para a T06: **opção 3.**

- **Vercel Hobby** (PL-VERCEL): o site. O browser só fala com esta origem; o rewrite de `/api/*` envia para a API e fica fixado no build (`API_INTERNAL_URL`). Por isso os cookies são first-party e **não há CORS nem Caddy**.
- **Render gratuito** (PL-RENDER-API): a API com o worker, num serviço web Docker público, com o deploy automático desligado.
- **Supabase gratuito** (PL-SUPABASE): PostgreSQL, sempre pelo pooler em modo sessão (porta 5432, PL-SUPAPOOL). O anfitrião direto é só IPv6 e os runners do GitHub não têm IPv6; o modo transação não serve as transações da API.
- **Brevo** (PL-BREVO): SMTP, com um remetente verificado por email, porque não há domínio próprio.
- **GitHub Actions** (X-GHA): `deploy.yml` (PL-DEPLOY) corre depois do CI verde na `main`, por esta ordem: migrações, deploy da API por deploy hook (PL-DEPLOYHOOK), deploy do site com a CLI da Vercel, smoke. `prod-data.yml` (PL-PRODDATA) corre à mão as migrações, o `set-admin-password` e o `seed-demo`, porque o plano gratuito do Render não tem shell nem one-off jobs.
- **Dois papéis** (PL-DBUSERS): `postgres` para as migrações e os comandos, `sportsbooking_app`, sem DDL, para a API ([`supabase-roles.sql`](../../deployment/supabase-roles.sql)).

Os passos manuais estão no [runbook](../../deployment/runbook.md).

## Condições e riscos

| Tema | Consequência | O que fazemos |
| --- | --- | --- |
| **Arranque a frio** | Depois de 15 minutos sem tráfego, a API adormece e o primeiro pedido demora cerca de um minuto. | Aceite numa demonstração. |
| **Worker parado enquanto a API dorme** | Emails e lembretes só saem com a API acordada; um lembrete cuja reserva já começou é ignorado. | Aceite. O outbox guarda tudo e reservas e cancelamentos nunca dependem do envio. |
| **API pública e limites por IP de melhor esforço** | Quem ligar diretamente à API pode forjar `X-Forwarded-For`. | Bloqueio de conta do Identity como proteção real; o `Origin` continua a proteger o browser. `ForwardLimit` provisório, a confirmar no primeiro deploy. |
| **Tempo limite do rewrite da Vercel** | 120 segundos por pedido. Chega para um arranque a frio de cerca de um minuto, mas uma API muito lenta devolve erro. | Aceite. |
| **Limites e suspensões dos planos gratuitos** | Render: 750 horas por mês; sem meio de pagamento, ultrapassar a largura de banda suspende os serviços gratuitos até ao mês seguinte. Vercel Hobby: só uso não comercial. Supabase: 500 MB. | Sem meio de pagamento no Render, para nunca haver cobrança. Uma suspensão é aceitável. |
| **Supabase em pausa por inatividade** | Um projeto gratuito com pouca atividade durante uma semana é pausado, e a API deixa de ter base de dados. | Reativar no painel. `/health` não toca na base de dados, por isso o Render não reinicia a API por causa disto. |
| **Sem domínio próprio** | O Brevo usa só um remetente verificado por email, sem DKIM nem DMARC do domínio; os emails podem ir para o spam. | Aceite numa demonstração. |
| **Migrações antes da API** | O esquema novo fica ativo antes da API nova. | **Cada migração tem de funcionar com a versão anterior da API** (acrescentar antes de remover). |
| **API do Supabase sobre `public`** | As tabelas não têm row level security e `users` guarda emails e hashes. | `supabase-roles.sql` retira os privilégios de `anon`, `authenticated` e `service_role`. |
| **Sem cópias automáticas** | O plano gratuito não as inclui. | Aceite: os dados são de demonstração. |

## Consequências

**Boas**

- Custo zero, sem máquinas, certificados ou sistema operativo para administrar.
- A mesma origem de desenvolvimento: os cookies continuam first-party e não há CORS.
- A ordem do deploy é explícita e controlada por um só workflow.

**Más ou a vigiar**

- A API tem uma segunda entrada pública (`*.onrender.com`).
- O pedido atravessa dois fornecedores pela Internet pública, o que acrescenta latência.
- O `/health` não diz que versão está a correr: durante um build, o Render continua a servir a instância anterior, por isso o site pode ser publicado antes de a API nova estar ativa.
- Mudar o endereço da API obriga a um novo deploy do site (o rewrite fica no build).
- Quatro contas de fornecedores e vários segredos.

## O que esta ADR substitui na Stack

Quando for aceite, substitui, sem editar a [Stack](../../stack.md):

- **Secção «Arquitetura»:** a frase «O browser só fala com o Caddy».
- **Secção «Stack», linha «Infraestrutura»:** o Caddy como proxy com HTTPS.
- **Secção «Autenticação: cookies httpOnly», parágrafo «Mesma origem, obrigatório»:** «o Caddy (em produção)». Em produção, a mesma origem vem dos rewrites do Next.js. A frase sobre a Vercel continua verdadeira no que diz (front end e API em domínios diferentes, chamados diretamente pelo browser, dão cookies de terceiros); aqui o browser nunca chama a API diretamente.
- **Secção «Repositório, Git, CI e deploy», parte «Deploy» e árvore do repositório:** a máquina virtual com docker compose, o Caddy, o `Caddyfile`, o GitHub Container Registry, o deploy por SSH e a alternativa Azure Container Apps.

Substitui também a primeira versão desta ADR: a API num serviço privado do Render, com o Next.js no Render e a rede privada.

Ficam como estão: o migration bundle e os dois utilizadores da base de dados (critérios da T06) e o resto da Stack.

Os ids PL-CADDY, PL-HOST, PL-GHCR, PL-RENDER-WEB, PL-RENDER-NET e PL-DNS ficam no inventário, marcados como «não adotado».
