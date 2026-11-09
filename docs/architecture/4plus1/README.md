# Vistas 4+1 (Kruchten)

Vistas do sistema, cada uma para um leitor e uma preocupação. Os nomes são os dos diagramas C4 de [`../c4/`](../c4/README.md) e do [inventário](../inventory.md): o mesmo nome é o mesmo elemento. Os SVG estão em [`../out/`](../out/); o índice de todos os diagramas está no [README da arquitetura](../README.md#índice).

## Vista lógica: [`logical.md`](logical.md)

Diagramas: `logical-*.puml` e, como componentes (nível 3 do C4), `../c4/03-components-*.puml`.

**Para:** quem programa ou acrescenta funcionalidades.
**Decisão documentada:** Clean Architecture no back end, em quatro projetos com as dependências só para dentro (Api → Infrastructure → Application → Domain), sem repositório genérico: a Application usa o `IAppDbContext`. As entidades têm setters privados e métodos com intenção, e as falhas esperadas são resultados, não exceções. O que a base de dados tem de garantir fica na base de dados: a exclusion constraint só cobre as reservas `Confirmed` ([ADR 0003](../adr/0003-exclusion-constraint-reservas.md)), e os slots são calculados, nunca guardados ([ADR 0007](../adr/0007-slots-calculados.md)). Uma conta, um refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application ([Limitações conhecidas](../README.md#limitações-conhecidas), 3).

## Vista de processo: [`process.md`](process.md)

Diagramas: `process-*.puml`.

**Para:** quem programa ou depura pedidos, transações, concorrência, sessões e emails.
**Decisões documentadas:**
- **Concorrência:** reservar, bloquear e cancelar tomam o trinco do campo (`SELECT … FOR UPDATE`) e voltam a ler o que vão alterar; a sobreposição com outra reserva é da exclusion constraint, que dá 409 `slot_taken` ([ADR 0003](../adr/0003-exclusion-constraint-reservas.md)). A secção 3 do texto diz o que está e o que não está protegido.
- **Sessões:** cookies httpOnly, refresh com rotação, e o servidor nunca renova a sessão ([ADR 0002](../adr/0002-sessao-em-cookies-httponly.md)).
- **Emails:** gravados no outbox na transação de quem os pede e enviados por um worker com `SKIP LOCKED`, que corre no processo da API ([ADR 0004](../adr/0004-outbox-para-emails.md)).

## Vista de desenvolvimento: [`development.md`](development.md)

Diagramas: `development-*.puml`.

**Para:** quem vai mudar o código, o CI ou o deploy.
**Decisão documentada:** um repositório com o back end, o front end, a documentação e os workflows. Cada camada regista os seus serviços e o `Program.cs` só compõe. Os tipos da API no front end são gerados a partir do OpenAPI, e o CI falha se o OpenAPI, os tipos ou os SVG dos diagramas não corresponderem ao que o código e os `.puml` produzem. O deploy corre depois do CI verde na `main`, com as migrações aplicadas antes da API ([ADR 0009](../adr/0009-alvo-de-implantacao.md)).

## Vista física: [`physical.md`](physical.md)

Diagrama: [`physical.puml`](physical.puml). A implantação em C4 está em [`../c4/README.md`](../c4/README.md#implantação).

**Para:** quem faz deploy, opera ou dá suporte.
**Decisão documentada:** o site na Vercel, a API e o worker de emails no mesmo serviço público do Render, a base de dados no Supabase (sempre pelo pooler em modo sessão) e o SMTP no Brevo, todos em planos gratuitos; o browser só conhece a origem da Vercel ([ADR 0009](../adr/0009-alvo-de-implantacao.md)). O texto cobre o que é público, o TLS, o IP do cliente, os papéis da base de dados, os segredos, as migrações e os riscos em aberto.

## Cenários (+1): [`scenarios.md`](scenarios.md)

Diagrama: [`scenarios-use-cases.puml`](scenarios-use-cases.puml). Não há uma sequência por cenário: cada um liga aos `process-*.puml`.

**Para:** toda a gente (produto, quem programa, quem avalia).
**Decisão documentada:** que casos de uso atravessam as outras vistas e as validam. Se um cenário não se conseguir contar com os elementos e os diagramas das outras vistas, falta alguma coisa nelas.

| Cenário | Histórias | Testes |
|---|---|---|
| SC-01 · Visitante pesquisa e reserva um slot | US16, US17, US18, US01, US03, US22 | ver a [tabela](scenarios.md#cenários-histórias-diagramas-e-testes) |
| SC-02 · Dono configura clube, campos, horários e preços | US02, US06, US07, US08, US09, US10 | idem |
| SC-03 · Dono cancela uma reserva no calendário | US11, US12, US22 | idem |
| SC-04 · Reserva semanal com uma data saltada | US20, US18 | idem |
| SC-05 · Recuperação da palavra-passe | US05 | idem |
| SC-06 · Admin desativa um dono | US29, US04 | idem |
| SC-07 · Corrida pelo mesmo slot | T03, US18 | idem |
