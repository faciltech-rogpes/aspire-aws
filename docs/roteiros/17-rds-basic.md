# 17 - RDS Basic

## Tecnologias deste cenario

- [Amazon RDS](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/Welcome.html): servico gerenciado de banco de dados relacional na AWS. Neste cenario, o motor usado eh PostgreSQL.
- [PostgreSQL](https://www.postgresql.org/docs/): banco de dados relacional de codigo aberto. O AppHost sobe um container PostgreSQL real na porta `5433`.
- [Npgsql](https://www.npgsql.org/doc/index.html): biblioteca .NET para conectar e operar bancos PostgreSQL.
- [LocalStack](https://docs.localstack.cloud/): emula a API de controle do RDS (requer edicao Pro). O plano de dados (SQL real) vai direto ao container PostgreSQL.

## Conceitos base deste cenario

- `Plano de controle`: conjunto de APIs AWS para provisionar e gerenciar a infraestrutura (criar instancia, descrever status, modificar). Aqui eh a API do RDS.
- `Plano de dados`: a conexao SQL de verdade com o banco. Aqui eh feita via [Npgsql](https://www.npgsql.org/doc/index.html) direto no container PostgreSQL.
- `DBInstance`: representacao logica de um servidor de banco de dados no RDS.
- `Connection string`: endereco de conexao ao banco. Neste projeto: `Host=localhost;Port=5433;Database=testdb;Username=test;Password=test`.

## O que este cenario ensina

Este roteiro mostra dois planos em acao:

```
Plano de controle (AWS SDK):
  CreateDBInstance -> DescribeDBInstances -> ModifyDBInstance

Plano de dados (SQL direto):
  Npgsql -> INSERT / SELECT / UPDATE / DELETE / ROLLBACK
```

O ponto central: a API RDS nao eh o banco em si. Ela gerencia o ciclo de vida da instancia. O SQL de verdade passa pela connection string diretamente para o PostgreSQL.

## Conceitos em portugues simples

- [Amazon RDS](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/Welcome.html): servico que gerencia bancos de dados — voce nao administra o servidor, so usa.
- `DBInstance`: o "banco" que voce cria via API. No LocalStack Pro, essa API existe. No Community, nao.
- `Plano de dados`: o SQL em si. No projeto, vai direto ao container PostgreSQL real — independente do LocalStack.
- `Npgsql`: o driver que faz o C# conversar com o PostgreSQL.

## Como o cenario esta montado

O `Fixture`:

1. Aguarda o container PostgreSQL aceitar conexoes (polling por ate 60s)
2. Tenta criar uma `DBInstance` via API RDS no LocalStack — se a API nao estiver disponivel (edicao Community), registra o fato e continua
3. Cria a tabela `produtos` diretamente no PostgreSQL via Npgsql

Arquivo: [Fixture.cs](../../scenarios/17-RDS.Basic/Fixture.cs)

Trecho da criacao de tabela no PostgreSQL real:

```csharp
await using var cmd = new NpgsqlCommand(
    """
    CREATE TABLE IF NOT EXISTS produtos (
        id      TEXT PRIMARY KEY,
        nome    TEXT NOT NULL,
        preco   NUMERIC(10,2) NOT NULL,
        estoque INTEGER NOT NULL
    )
    """, conn);
await cmd.ExecuteNonQueryAsync();
```

## Sobre o container PostgreSQL

O AppHost sobe um container PostgreSQL real (nao emulado) junto com o LocalStack:

```csharp
// src/AppHost/Program.cs
builder
    .AddContainer("postgres-rds", "postgres", "16.9")
    .WithEnvironment("POSTGRES_USER", "test")
    .WithEnvironment("POSTGRES_PASSWORD", "test")
    .WithEnvironment("POSTGRES_DB", "testdb")
    .WithHttpEndpoint(port: 5433, targetPort: 5432, name: "tcp", isProxied: false);
```

A connection string esta em `LocalStackFixture.PostgresConnectionString`:

```
Host=localhost;Port=5433;Database=testdb;Username=test;Password=test
```

## O que os testes validam

Arquivo: [RdsBasicTests.cs](../../scenarios/17-RDS.Basic/RdsBasicTests.cs)

### Plano de controle — 3 testes (skipped no Community)

| Teste | O que verifica |
|-------|---------------|
| `CreateDBInstance_ShouldReturnInstanceIdentifier` | Identificador e motor da instancia criada via API RDS |
| `DescribeDBInstances_ShouldShowAvailableStatus` | Status `available` apos criacao |
| `ModifyDBInstance_ShouldUpdateAllocatedStorage` | API de modificacao aceita nova configuracao |

### Plano de dados — 5 testes (passam sempre)

| Teste | O que verifica |
|-------|---------------|
| `InsertProduct_ShouldPersistToDatabase` | INSERT + SELECT round-trip |
| `QueryProducts_ShouldReturnAllInserted` | SELECT com multiplos registros |
| `UpdateProduct_ShouldModifyPrice` | UPDATE altera o valor corretamente |
| `DeleteProduct_ShouldRemoveRecord` | DELETE remove o registro |
| `Transaction_WhenRolledBack_ShouldNotPersist` | ROLLBACK reverte o INSERT |

## Observacao importante

Os 3 testes de plano de controle estao marcados com `Skip`:

```csharp
[Fact(Skip = EnvironmentLimitations.LocalStackRdsApiReason)]
```

A mensagem: *"RDS control plane API is a Pro feature — not available in LocalStack Community 3.8."*

Os 5 testes de plano de dados rodam normalmente porque vao direto ao container PostgreSQL — sem depender do LocalStack.

## Passo a passo para rodar

1. Abra o Docker.
2. Rode:

```bash
dotnet test scenarios/17-RDS.Basic/
```

3. Resultado esperado:

```
Total tests: 8
     Passed: 5   ← plano de dados (Npgsql)
    Skipped: 3   ← plano de controle RDS (Pro feature)
```

## O que observar no resultado

- Os 5 testes que passam demonstram o PostgreSQL real funcionando dentro do ambiente de testes — INSERT, SELECT, UPDATE, DELETE e ROLLBACK de transacao.
- Os 3 skipped mostram onde a API RDS seria usada em producao (ou com LocalStack Pro).

## Arquivos principais

- [Fixture.cs](../../scenarios/17-RDS.Basic/Fixture.cs)
- [RdsBasicTests.cs](../../scenarios/17-RDS.Basic/RdsBasicTests.cs)
- [AppHost/Program.cs](../../src/AppHost/Program.cs) — container postgres-rds
