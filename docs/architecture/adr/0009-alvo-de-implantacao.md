# 0009 · Alvo de implantação

- **Estado:** Proposta (planeado para a T06; nada implantado).
- **Data:** 2026-10-30.
- **Elementos:** C-WEB, C-API, C-WORKER, C-DB, PL-RENDER, PL-RENDER-EDGE, PL-RENDER-NET, PL-RENDER-WEB, PL-RENDER-API, PL-SUPABASE, PL-SUPAPOOL, PL-BREVO, PL-DNS, PL-DBUSERS, PL-MIGBUNDLE, PL-BACKUP. Detalhe na [vista física](../06-physical-view.md).

## Contexto

A T06 pede um URL público com HTTPS, deploy automático depois de cada merge na `main` com o CI verde, segredos só em variáveis de ambiente, migrações aplicadas no deploy e dois utilizadores da base de dados.

O que o código impõe a qualquer alvo:

- **Uma só origem.** A sessão vive em cookies httpOnly e a API verifica o `Origin`: o browser tem de ver as páginas e `/api/*` no mesmo endereço. Hoje é o Next.js (C-WEB) que reencaminha `/api/*` para a API (WEB-REWRITE), com o endereço da API fixado no build.
- **Um processo sempre ligado.** O worker de emails (C-WORKER) corre dentro da API; se a API parar, os emails param.
- **PostgreSQL com `btree_gist`** (DB-GIST) para a `bookings_no_overlap`, transações com `FOR UPDATE` e `SKIP LOCKED`, e as chaves do Data Protection na base de dados (nada precisa de disco local).
- **SMTP** com StartTls (Brevo previsto desde a US22).

A [Stack](../../stack.md) previa uma máquina virtual com docker compose, com o Caddy (PL-CADDY) à frente a tratar do HTTPS e a juntar front end e API na mesma origem, imagens no GitHub Container Registry (PL-GHCR), deploy por SSH (PL-DEPLOY) e `pg_dump` diário (PL-BACKUP); como alternativa, Azure Container Apps.

Alternativas consideradas:

1. **Máquina virtual com Caddy** (a da Stack). Controlo total e um só custo fixo, mas é preciso administrar o sistema, as atualizações, o firewall, os certificados e as cópias da base de dados à mão.
2. **Vercel (front end) + Render (API).** O Next.js fica na Vercel e a API num serviço do Render. Para manter uma só origem, a Vercel teria de reencaminhar `/api/*` para a API pela internet, por isso a API teria de ter um URL público (a segunda entrada que se quer evitar), e o tráfego entre os dois passaria pela internet pública, entre dois fornecedores. Pôr o front end e a API em domínios diferentes tornaria os cookies de terceiros, o que a Stack já exclui.
3. **Os dois no Render, com a API privada.** O Next.js num serviço web público, a API num serviço privado sem URL público, ligados pela rede privada do Render; base de dados e SMTP geridos fora.

## Decisão

Proposta para a T06: **opção 3.**

- **Render:** C-WEB num serviço web público (PL-RENDER-WEB), a **única entrada pública**, com o domínio próprio e o TLS da borda do Render (PL-RENDER-EDGE). C-API com C-WORKER num **serviço privado** (PL-RENDER-API), sem URL público, sempre ligado, alcançável só pela rede privada (PL-RENDER-NET). As duas imagens são as dos `Dockerfile` do repositório.
- **O Next.js continua a ser o proxy reverso**: os seus rewrites reencaminham `/api/*` para o endereço interno da API. **Não há Caddy.**
- **Supabase** (PL-SUPABASE) para o PostgreSQL, ligado pelo pooler em modo sessão (PL-SUPAPOOL), com dois papéis (PL-DBUSERS).
- **Brevo** (PL-BREVO) para o SMTP, com remetente num domínio próprio (PL-DNS).

### Condições (a cumprir antes de implantar)

Cada uma tem o que verificar na [vista física, secção 5](../06-physical-view.md#5-riscos-e-pontos-em-aberto-para-a-t06).

1. **IP do cliente:** o Next.js reescreve `X-Forwarded-For` com o IP que a borda do Render lhe dá (substitui, não acrescenta nem passa o do browser), as chamadas do servidor levam o IP do cliente, e só então a API confia no endereço do Next.js. É uma mudança de código e de configuração da T06, não está feita. Sem ela, todos os utilizadores partilham um limite de pedidos.
2. **API sempre ligada:** plano do Render que não adormece para o serviço privado.
3. **Base de dados:** modo sessão do pooler (ou ligação direta), nunca o modo transação; `btree_gist` criada pelo dono do esquema; nenhuma tabela alcançável pela API de dados do Supabase; testes de integração na versão do PostgreSQL do Supabase.
4. **Migrações:** aplicadas por um passo com a ligação do dono do esquema antes de a nova versão ficar ativa (job do GitHub Actions, proposto por manter a credencial de DDL fora do Render, ou pre-deploy do Render); a API liga-se como utilizador da aplicação, sem DDL.
5. **Email:** domínio próprio autenticado no Brevo (DKIM, DMARC).
6. **Cópias:** cópias diárias (plano do Supabase ou `pg_dump` agendado), tratadas como segredo, porque levam as chaves do Data Protection.
7. **Antes da T06:** Dependabot (PL-DEPENDABOT) e cabeçalhos de segurança (PL-SECHEADERS), já previstos no backlog. **Feitos**; falta ligar o HSTS (`ENABLE_HSTS=true` no build do serviço web) quando houver HTTPS.

Se uma condição não se puder cumprir a um custo aceitável, esta ADR volta a ser discutida antes de implantar.

## Consequências

**Boas**

- Uma só entrada pública; a API e o worker não estão na internet, por isso a única superfície HTTP pública é o Next.js e o que ele reencaminha.
- A mesma origem de hoje, sem peças novas: os cookies continuam first-party e não há CORS.
- O que existe em desenvolvimento (o Next.js como proxy) é o que corre em produção; os `Dockerfile` servem tal como estão.
- Nada de máquinas, certificados ou sistema operativo para administrar; base de dados com cópias e atualizações do fornecedor (conforme o plano).

**Más ou a vigiar**

- **Custos:** o serviço privado é pago (sem plano gratuito, a confirmar), e a API tem de estar sempre ligada.
- **IP do cliente** passa a depender do Next.js e do cabeçalho da borda do Render (condição 1); a regra da US03 muda.
- **Endereço da API fixado no build** do front end: mudar o nome ou a porta do serviço privado obriga a reconstruir o front end.
- **Sem acesso direto à API:** migrações e `set-admin-password` precisam de um job, de um one-off job ou de uma shell (alguns só em planos pagos); o `seed-demo` recusa-se em Production.
- **Três fornecedores** (Render, Supabase, Brevo) mais o DNS: mais contas, mais segredos, mais limites de planos a acompanhar.
- **Latência** entre o Render e o Supabase: escolher regiões próximas.

## O que esta ADR substitui na Stack

Quando for aceite, substitui, sem editar a [Stack](../../stack.md):

- **«Repositório, Git, CI e deploy» → «Deploy»:** a máquina virtual com docker compose, o GitHub Container Registry, o deploy por SSH e a alternativa Azure Container Apps.
- **Caddy como proxy reverso:** a linha «Infraestrutura» da tabela da secção «Stack» (Caddy), a frase «O browser só fala com o Caddy» da secção «Arquitetura», e o `Caddyfile` da árvore do repositório.
- **A explicação da mesma origem** em «Autenticação: cookies httpOnly» → «Mesma origem, obrigatório» («o Caddy (em produção)»): em produção a mesma origem passa a vir dos rewrites do Next.js, como em desenvolvimento.

Ficam como estão: o migration bundle e os dois utilizadores da base de dados (critérios da T06), o `pg_dump` como cópia se o plano do Supabase não as fizer, e o resto da Stack.

Os ids PL-CADDY, PL-HOST, PL-GHCR e PL-DEPLOY (na sua forma com SSH) ficam no inventário até esta ADR ser aceite; depois passam a «não adotado».
