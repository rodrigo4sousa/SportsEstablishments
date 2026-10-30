# 0008 · Chaves do Data Protection na base de dados

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da US05).
- **Elementos:** CC-DATAPROT, DB-DPKEYS, API-AUTH-RESET, C-API, C-DB.

## Contexto

As ligações de recuperação de palavra-passe (US05) levam um token do fornecedor do Identity, protegido com o Data Protection do ASP.NET Core e válido 1 hora. O Data Protection guarda as chaves num anel de chaves; por omissão, num ficheiro local do contentor. Uma ligação enviada antes de um reinício, de um novo deploy ou de uma segunda instância deixaria de ser aceite, e a pessoa receberia «ligação inválida ou expirada» sem culpa.

Alternativas:

1. **Chaves num ficheiro local** (a omissão). Perdem-se quando o contentor é recriado, e cada instância teria as suas.
2. **Um volume ou armazenamento partilhado.** Resolve, mas é uma peça nova a administrar, e alguns alvos de implantação não têm disco persistente.
3. **Chaves no PostgreSQL**, que a aplicação já usa.

## Decisão

Opção 3: `AddDataProtection().SetApplicationName("SportsBooking").PersistKeysToDbContext<AppDbContext>()`; as chaves ficam na tabela `data_protection_keys` (DB-DPKEYS), com `AppDbContext` a implementar `IDataProtectionKeyContext`.

- O nome da aplicação é fixo (`DependencyInjection.DataProtectionApplicationName`) e **nunca se muda**: faz parte de todos os tokens protegidos.
- A validade do token é `PasswordResetRules.LinkLifetime` (1 hora). O token é feito pelo worker no envio do email; o outbox guarda só o id da conta ([ADR 0004](0004-outbox-para-emails.md)).

## Consequências

**Boas**

- Reinícios, novos deploys e várias instâncias continuam a aceitar as ligações já enviadas; nada precisa de disco persistente.
- Fica tudo na base de dados, com as mesmas cópias e a mesma ligação.

**Más ou a vigiar**

- O código não cifra as chaves em repouso (não há `ProtectKeysWith…`): **quem tem uma cópia da base de dados tem as chaves**, por isso uma cópia de segurança é tão sensível como os segredos.
- O gerador de tokens do Identity lê o relógio do sistema, não o `TimeProvider`: a validade não se testa com um relógio falso, e os testes fabricam tokens antigos no formato do Identity.
- Mudar o nome da aplicação invalida todas as ligações em circulação.
