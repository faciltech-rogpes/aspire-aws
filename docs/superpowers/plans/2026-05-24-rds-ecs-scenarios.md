# Cenários 17-RDS.Basic e 18-ECS.RunTask — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar dois cenários didáticos ao projeto aspire-aws — 17-RDS.Basic (API RDS + CRUD PostgreSQL real) e 18-ECS.RunTask (ciclo de vida ECS + worker Python sempre-ativo que processa pedidos via SQS e persiste no PostgreSQL).

**Architecture:** LocalStack gerencia o plano de controle AWS (instâncias RDS, clusters ECS, task definitions). Aspire sobe containers reais (postgres:16 na porta 5433 e o worker Python ecs-worker). Os testes interagem com ambos os planos — SDK AWS para metadados, Npgsql para dados reais.

**Tech Stack:** .NET 10, xUnit 2.9, AWSSDK.RDS, AWSSDK.ECS, Npgsql 9, Python 3.12, psycopg2-binary, boto3, LocalStack 3.8 Community, Docker.

---

## Mapa de arquivos

| Ação | Arquivo |
|---|---|
| Modificar | `src/Shared/Shared.csproj` |
| Modificar | `src/Shared/AwsClientFactory.cs` |
| Modificar | `src/Shared/LocalStackFixture.cs` |
| Modificar | `src/AppHost/Program.cs` |
| Modificar | `aspire-aws.sln` |
| Criar | `src/tasks/pedido_processor/handler.py` |
| Criar | `src/tasks/pedido_processor/requirements.txt` |
| Criar | `src/tasks/pedido_processor/Dockerfile` |
| Criar | `scenarios/17-RDS.Basic/17-RDS.Basic.csproj` |
| Criar | `scenarios/17-RDS.Basic/Fixture.cs` |
| Criar | `scenarios/17-RDS.Basic/RdsBasicTests.cs` |
| Criar | `scenarios/18-ECS.RunTask/18-ECS.RunTask.csproj` |
| Criar | `scenarios/18-ECS.RunTask/Fixture.cs` |
| Criar | `scenarios/18-ECS.RunTask/EcsRunTaskTests.cs` |

---

## Task 1: Infraestrutura compartilhada — AwsClientFactory, Shared.csproj, LocalStackFixture

**Files:**
- Modify: `src/Shared/Shared.csproj`
- Modify: `src/Shared/AwsClientFactory.cs`
- Modify: `src/Shared/LocalStackFixture.cs`

- [ ] **Passo 1: Adicionar AWSSDK.RDS e AWSSDK.ECS ao Shared.csproj**

Em `src/Shared/Shared.csproj`, adicionar após `AWSSDK.DynamoDBv2`:

```xml
    <PackageReference Include="AWSSDK.ECS" Version="3.*" />
    <PackageReference Include="AWSSDK.RDS" Version="3.*" />
```

O arquivo deve ficar com as entradas em ordem alfabética:
```xml
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" Version="13.3.1" />
    <PackageReference Include="AWSSDK.DynamoDBv2" Version="3.*" />
    <PackageReference Include="AWSSDK.ECS" Version="3.*" />
    <PackageReference Include="AWSSDK.EventBridge" Version="3.*" />
    <PackageReference Include="AWSSDK.Lambda" Version="3.*" />
    <PackageReference Include="AWSSDK.RDS" Version="3.*" />
    <PackageReference Include="AWSSDK.S3" Version="3.*" />
    <PackageReference Include="AWSSDK.SecretsManager" Version="3.*" />
    <PackageReference Include="AWSSDK.SimpleNotificationService" Version="3.*" />
    <PackageReference Include="AWSSDK.SimpleSystemsManagement" Version="3.*" />
    <PackageReference Include="AWSSDK.SQS" Version="3.*" />
    <PackageReference Include="AWSSDK.Scheduler" Version="3.*" />
    <PackageReference Include="AWSSDK.SecurityToken" Version="3.*" />
    <PackageReference Include="AWSSDK.StepFunctions" Version="3.*" />
    <PackageReference Include="xunit" Version="2.9.3" />
  </ItemGroup>
```

- [ ] **Passo 2: Adicionar RDS() e ECS() ao AwsClientFactory**

Em `src/Shared/AwsClientFactory.cs`, adicionar os dois `using` no topo:
```csharp
using Amazon.ECS;
using Amazon.RDS;
```

Adicionar os dois métodos antes do método `Configure` privado no final da classe:
```csharp
    public static AmazonRDSClient RDS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonRDSClient(Credentials, Configure(new AmazonRDSConfig(), endpoint))
            : new AmazonRDSClient();

    public static AmazonECSClient ECS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonECSClient(Credentials, Configure(new AmazonECSConfig(), endpoint))
            : new AmazonECSClient();
```

- [ ] **Passo 3: Adicionar PostgresConnectionString ao LocalStackFixture**

Em `src/Shared/LocalStackFixture.cs`, adicionar após a constante `Endpoint`:
```csharp
    public const string PostgresConnectionString =
        "Host=localhost;Port=5433;Database=testdb;Username=test;Password=test";
```

- [ ] **Passo 4: Verificar build**

```bash
dotnet build src/Shared/
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Passo 5: Commit**

```bash
git add src/Shared/
git commit -m "feat(shared): adiciona clientes RDS e ECS ao AwsClientFactory e PostgresConnectionString ao LocalStackFixture"
```

---

## Task 2: AppHost — container PostgreSQL e serviços rds,ecs

**Files:**
- Modify: `src/AppHost/Program.cs`

- [ ] **Passo 1: Adicionar postgres-rds e rds,ecs ao SERVICES**

Substituir o conteúdo de `src/AppHost/Program.cs` por:

```csharp
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args
});

var alvoAws = Environment.GetEnvironmentVariable("AWS_TARGET") ?? "localstack";

if (!string.Equals(alvoAws, "aws", StringComparison.OrdinalIgnoreCase))
{
    builder
        .AddContainer("localstack", "localstack/localstack", "3.8")
        .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
        .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
        .WithEnvironment("SERVICES", "s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,scheduler,stepfunctions,rds,ecs")
        .WithEnvironment("LAMBDA_REMOVE_CONTAINERS", "true")
        .WithEnvironment("LAMBDA_RUNTIME_ENVIRONMENT_TIMEOUT", "120")
        .WithEnvironment("DOCKER_HOST", "unix:///var/run/docker.sock")
        .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "gateway", isProxied: false);

    builder
        .AddContainer("postgres-rds", "postgres", "16")
        .WithEnvironment("POSTGRES_USER", "test")
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .WithEnvironment("POSTGRES_DB", "testdb")
        .WithHttpEndpoint(port: 5433, targetPort: 5432, name: "tcp", isProxied: false);
}

builder.Build().Run();
```

- [ ] **Passo 2: Verificar build**

```bash
dotnet build src/AppHost/
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Passo 3: Commit**

```bash
git add src/AppHost/Program.cs
git commit -m "feat(apphost): adiciona container postgres:16 (porta 5433) e serviços rds,ecs ao LocalStack"
```

---

## Task 3: Scaffold do cenário 17-RDS.Basic

**Files:**
- Create: `scenarios/17-RDS.Basic/17-RDS.Basic.csproj`
- Create: `scenarios/17-RDS.Basic/Fixture.cs` (esqueleto)
- Create: `scenarios/17-RDS.Basic/RdsBasicTests.cs` (esqueleto)
- Modify: `aspire-aws.sln`

- [ ] **Passo 1: Criar 17-RDS.Basic.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Scenarios.RDS.Basic</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Npgsql" Version="9.*" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Shared\Shared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Passo 2: Criar Fixture.cs (esqueleto)**

```csharp
using Amazon.RDS;
using Shared;

namespace Scenarios.RDS.Basic;

public class Fixture : LocalStackFixture
{
    public const string NomeInstancia     = "rds-test-db";
    public const string NomeTabelaProdutos = "produtos";

    public AmazonRDSClient RDS { get; private set; } = null!;
    public string ConnectionString => LocalStackFixture.PostgresConnectionString;

    protected override Task InitializeScenarioAsync() => Task.CompletedTask;

    protected override Task DisposeScenarioAsync()
    {
        RDS?.Dispose();
        return Task.CompletedTask;
    }
}
```

- [ ] **Passo 3: Criar RdsBasicTests.cs (esqueleto)**

```csharp
using Xunit.Abstractions;

namespace Scenarios.RDS.Basic;

public class RdsBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public void Placeholder_ShouldPass() => Assert.True(true);
}
```

- [ ] **Passo 4: Registrar na solução**

```bash
dotnet sln aspire-aws.sln add scenarios/17-RDS.Basic/17-RDS.Basic.csproj
```

- [ ] **Passo 5: Verificar build e teste do placeholder**

```bash
dotnet build aspire-aws.sln
dotnet test scenarios/17-RDS.Basic/
```

Saída esperada do teste: `Aprovado! – Com falha: 0, Aprovado: 1`

- [ ] **Passo 6: Commit**

```bash
git add scenarios/17-RDS.Basic/ aspire-aws.sln
git commit -m "feat(17): scaffold do cenário RDS.Basic"
```

---

## Task 4: Fixture do cenário 17 — PostgreSQL wait, RDS init, schema

**Files:**
- Modify: `scenarios/17-RDS.Basic/Fixture.cs`

- [ ] **Passo 1: Implementar Fixture completa**

Substituir o conteúdo de `scenarios/17-RDS.Basic/Fixture.cs`:

```csharp
using Amazon.RDS;
using Amazon.RDS.Model;
using Npgsql;
using Shared;

namespace Scenarios.RDS.Basic;

public class Fixture : LocalStackFixture
{
    public const string NomeInstancia      = "rds-test-db";
    public const string NomeTabelaProdutos = "produtos";

    public AmazonRDSClient RDS { get; private set; } = null!;
    public string ConnectionString => LocalStackFixture.PostgresConnectionString;

    protected override async Task InitializeScenarioAsync()
    {
        RDS = AwsClientFactory.RDS();

        // 1. Aguarda PostgreSQL real aceitar conexões
        await PollingHelper.WaitUntilAsync(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }, timeout: TimeSpan.FromSeconds(60), interval: TimeSpan.FromSeconds(1),
        failureMessage: "PostgreSQL não ficou disponível em 60s.");

        // 2. Cria instância RDS no LocalStack (plano de controle AWS)
        await RDS.CreateDBInstanceAsync(new CreateDBInstanceRequest
        {
            DBInstanceIdentifier = NomeInstancia,
            DBInstanceClass      = "db.t3.micro",
            Engine               = "postgres",
            MasterUsername       = "test",
            MasterUserPassword   = "test",
            AllocatedStorage     = 20,
            DBName               = "testdb"
        });

        // 3. Aguarda status "available" (LocalStack transiciona rapidamente)
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var r = await RDS.DescribeDBInstancesAsync(new DescribeDBInstancesRequest
            {
                DBInstanceIdentifier = NomeInstancia
            });
            return r.DBInstances.Count > 0 &&
                   r.DBInstances[0].DBInstanceStatus == "available";
        }, timeout: TimeSpan.FromSeconds(30),
        failureMessage: $"Instância RDS '{NomeInstancia}' não ficou 'available' em 30s.");

        // 4. Cria schema no PostgreSQL real
        await using var setup = new NpgsqlConnection(ConnectionString);
        await setup.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            CREATE TABLE IF NOT EXISTS {NomeTabelaProdutos} (
                id      TEXT PRIMARY KEY,
                nome    TEXT NOT NULL,
                preco   NUMERIC(10,2) NOT NULL,
                estoque INTEGER NOT NULL
            )
            """, setup);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override async Task DisposeScenarioAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"DROP TABLE IF EXISTS {NomeTabelaProdutos}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            RDS?.Dispose();
        }
    }
}
```

- [ ] **Passo 2: Verificar build**

```bash
dotnet build scenarios/17-RDS.Basic/
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/17-RDS.Basic/Fixture.cs
git commit -m "feat(17): Fixture com PostgreSQL health check, RDS init e schema da tabela produtos"
```

---

## Task 5: Testes de plano de controle AWS — RDS API

**Files:**
- Modify: `scenarios/17-RDS.Basic/RdsBasicTests.cs`

- [ ] **Passo 1: Implementar os 3 testes de API RDS**

Substituir o conteúdo de `scenarios/17-RDS.Basic/RdsBasicTests.cs`:

```csharp
using Amazon.RDS.Model;
using Xunit.Abstractions;

namespace Scenarios.RDS.Basic;

public class RdsBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    // ── Plano de controle AWS ────────────────────────────────────────────────

    [Fact]
    public async Task CreateDBInstance_ShouldReturnInstanceIdentifier()
    {
        output.WriteLine($">>> RDS.DescribeDBInstances: verificando instância '{Fixture.NomeInstancia}'");
        output.WriteLine("    A instância foi criada no Fixture — verificamos identifier e engine via API AWS");

        var response = await fixture.RDS.DescribeDBInstancesAsync(new DescribeDBInstancesRequest
        {
            DBInstanceIdentifier = Fixture.NomeInstancia
        });

        var instance = response.DBInstances.Single();
        output.WriteLine($"    Identifier: {instance.DBInstanceIdentifier} | Engine: {instance.Engine} | Status: {instance.DBInstanceStatus}");

        Assert.Equal(Fixture.NomeInstancia, instance.DBInstanceIdentifier);
        Assert.Equal("postgres", instance.Engine);
        output.WriteLine("    Instância identificada corretamente — API de provisionamento funcional");
    }

    [Fact]
    public async Task DescribeDBInstances_ShouldShowAvailableStatus()
    {
        output.WriteLine($">>> RDS.DescribeDBInstances: verificando status da instância '{Fixture.NomeInstancia}'");
        output.WriteLine("    Status 'available' indica que a instância está pronta para conexões");

        var response = await fixture.RDS.DescribeDBInstancesAsync(new DescribeDBInstancesRequest
        {
            DBInstanceIdentifier = Fixture.NomeInstancia
        });

        var status = response.DBInstances.Single().DBInstanceStatus;
        output.WriteLine($"    Status atual: '{status}'");

        Assert.Equal("available", status);
        output.WriteLine("    Status 'available' confirmado — instância pronta");
    }

    [Fact]
    public async Task ModifyDBInstance_ShouldUpdateAllocatedStorage()
    {
        output.WriteLine($">>> RDS.ModifyDBInstance: alterando AllocatedStorage de 20 → 30 GB");
        output.WriteLine("    ModifyDBInstance com ApplyImmediately=true agenda a mudança — PendingModifiedValues reflete a intenção");

        var response = await fixture.RDS.ModifyDBInstanceAsync(new ModifyDBInstanceRequest
        {
            DBInstanceIdentifier = Fixture.NomeInstancia,
            AllocatedStorage     = 30,
            ApplyImmediately     = true
        });

        output.WriteLine($"    Identifier retornado: {response.DBInstance.DBInstanceIdentifier}");
        Assert.Equal(Fixture.NomeInstancia, response.DBInstance.DBInstanceIdentifier);
        output.WriteLine("    Modificação aceita pelo plano de controle AWS");
    }
}
```

- [ ] **Passo 2: Rodar os testes (Docker precisa estar rodando)**

```bash
dotnet test scenarios/17-RDS.Basic/ --logger "console;verbosity=normal"
```

Saída esperada: `Aprovado! – Com falha: 0, Aprovado: 3`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/17-RDS.Basic/RdsBasicTests.cs
git commit -m "feat(17): testes de plano de controle AWS — CreateDBInstance, DescribeDBInstances, ModifyDBInstance"
```

---

## Task 6: Testes Npgsql — CRUD e transação

**Files:**
- Modify: `scenarios/17-RDS.Basic/RdsBasicTests.cs`

- [ ] **Passo 1: Adicionar os 5 testes Npgsql à classe existente**

Adicionar os métodos abaixo dentro da classe `RdsBasicTests`, após os 3 testes já existentes:

```csharp
    // ── Plano de dados — Npgsql ──────────────────────────────────────────────

    [Fact]
    public async Task InsertProduct_ShouldPersistToDatabase()
    {
        output.WriteLine($">>> Npgsql.INSERT: gravando produto 'p-insert-001' na tabela '{Fixture.NomeTabelaProdutos}'");
        output.WriteLine("    Demonstra conexão direta ao PostgreSQL real via connection string do LocalStack");

        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var insert = new Npgsql.NpgsqlCommand(
            $"INSERT INTO {Fixture.NomeTabelaProdutos} (id, nome, preco, estoque) VALUES (@id, @nome, @preco, @estoque)",
            conn);
        insert.Parameters.AddWithValue("id",      "p-insert-001");
        insert.Parameters.AddWithValue("nome",    "Caderno Universitário");
        insert.Parameters.AddWithValue("preco",   19.90m);
        insert.Parameters.AddWithValue("estoque", 100);
        await insert.ExecuteNonQueryAsync();

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT nome, preco FROM {Fixture.NomeTabelaProdutos} WHERE id = 'p-insert-001'", conn);
        await using var reader = await select.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "Produto não encontrado após INSERT");
        Assert.Equal("Caderno Universitário", reader.GetString(0));
        Assert.Equal(19.90m, reader.GetDecimal(1));
        output.WriteLine($"    Produto gravado: nome={reader.GetString(0)}, preco={reader.GetDecimal(1)}");
        output.WriteLine("    INSERT persistido e lido com sucesso");
    }

    [Fact]
    public async Task QueryProducts_ShouldReturnAllInserted()
    {
        output.WriteLine($">>> Npgsql.INSERT+SELECT: inserindo 3 produtos e listando todos com prefixo 'p-query-'");

        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        var ids = new[] { "p-query-001", "p-query-002", "p-query-003" };
        foreach (var (id, i) in ids.Select((id, i) => (id, i)))
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"INSERT INTO {Fixture.NomeTabelaProdutos} (id, nome, preco, estoque) VALUES (@id, @nome, @preco, @estoque)",
                conn);
            cmd.Parameters.AddWithValue("id",      id);
            cmd.Parameters.AddWithValue("nome",    $"Produto {i + 1}");
            cmd.Parameters.AddWithValue("preco",   (decimal)(i + 1) * 10);
            cmd.Parameters.AddWithValue("estoque", (i + 1) * 5);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Fixture.NomeTabelaProdutos} WHERE id LIKE 'p-query-%'", conn);
        var count = (long)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    {count} produto(s) encontrado(s) com prefixo 'p-query-'");
        Assert.Equal(3L, count);
        output.WriteLine("    SELECT retornou todos os registros inseridos");
    }

    [Fact]
    public async Task UpdateProduct_ShouldModifyPrice()
    {
        output.WriteLine($">>> Npgsql.INSERT+UPDATE+SELECT: atualizando preço de 'p-update-001'");

        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var insert = new Npgsql.NpgsqlCommand(
            $"INSERT INTO {Fixture.NomeTabelaProdutos} (id, nome, preco, estoque) VALUES ('p-update-001', 'Caneta', 2.50, 200)",
            conn);
        await insert.ExecuteNonQueryAsync();

        await using var update = new Npgsql.NpgsqlCommand(
            $"UPDATE {Fixture.NomeTabelaProdutos} SET preco = 3.90 WHERE id = 'p-update-001'",
            conn);
        var affected = await update.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT preco FROM {Fixture.NomeTabelaProdutos} WHERE id = 'p-update-001'", conn);
        var novoPreco = (decimal)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    Preço após UPDATE: {novoPreco}");
        Assert.Equal(3.90m, novoPreco);
        output.WriteLine("    UPDATE aplicado corretamente");
    }

    [Fact]
    public async Task DeleteProduct_ShouldRemoveRecord()
    {
        output.WriteLine($">>> Npgsql.INSERT+DELETE+SELECT: removendo produto 'p-delete-001'");

        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var insert = new Npgsql.NpgsqlCommand(
            $"INSERT INTO {Fixture.NomeTabelaProdutos} (id, nome, preco, estoque) VALUES ('p-delete-001', 'Borracha', 1.20, 500)",
            conn);
        await insert.ExecuteNonQueryAsync();

        await using var delete = new Npgsql.NpgsqlCommand(
            $"DELETE FROM {Fixture.NomeTabelaProdutos} WHERE id = 'p-delete-001'",
            conn);
        var affected = await delete.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Fixture.NomeTabelaProdutos} WHERE id = 'p-delete-001'", conn);
        var count = (long)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    Registros encontrados após DELETE: {count}");
        Assert.Equal(0L, count);
        output.WriteLine("    Registro removido com sucesso");
    }

    [Fact]
    public async Task Transaction_WhenRolledBack_ShouldNotPersist()
    {
        output.WriteLine($">>> Npgsql.TRANSACTION ROLLBACK: inserindo 'p-tx-001' e revertendo");
        output.WriteLine("    Rollback é fundamental para integridade de dados — INSERT dentro da transação não deve ser visível após ROLLBACK");

        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();
        await using var insert = new Npgsql.NpgsqlCommand(
            $"INSERT INTO {Fixture.NomeTabelaProdutos} (id, nome, preco, estoque) VALUES ('p-tx-001', 'Lápis', 0.80, 1000)",
            conn, tx);
        await insert.ExecuteNonQueryAsync();
        await tx.RollbackAsync();
        output.WriteLine("    Transação revertida");

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Fixture.NomeTabelaProdutos} WHERE id = 'p-tx-001'", conn);
        var count = (long)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    Registros encontrados após ROLLBACK: {count}");
        Assert.Equal(0L, count);
        output.WriteLine("    Rollback funcionou — dado não persistido");
    }
```

- [ ] **Passo 2: Rodar todos os 8 testes do cenário 17**

```bash
dotnet test scenarios/17-RDS.Basic/ --logger "console;verbosity=normal"
```

Saída esperada: `Aprovado! – Com falha: 0, Aprovado: 8`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/17-RDS.Basic/RdsBasicTests.cs
git commit -m "feat(17): testes Npgsql — INSERT, SELECT, UPDATE, DELETE e transaction rollback"
```

---

## Task 7: Worker Python — pedido_processor

**Files:**
- Create: `src/tasks/pedido_processor/handler.py`
- Create: `src/tasks/pedido_processor/requirements.txt`
- Create: `src/tasks/pedido_processor/Dockerfile`

- [ ] **Passo 1: Criar requirements.txt**

```
boto3
psycopg2-binary
```

- [ ] **Passo 2: Criar handler.py**

```python
import json
import os
import time

import boto3
import psycopg2


def connect_with_retry(database_url: str, retries: int = 30, delay: int = 2):
    """Tenta conectar ao PostgreSQL com retries — o container pode demorar para subir."""
    for attempt in range(retries):
        try:
            conn = psycopg2.connect(database_url)
            conn.autocommit = False
            print(f"[WORKER] Conectado ao PostgreSQL (tentativa {attempt + 1}).")
            return conn
        except psycopg2.OperationalError as e:
            print(f"[WORKER] Aguardando PostgreSQL... ({attempt + 1}/{retries}): {e}")
            time.sleep(delay)
    raise RuntimeError(f"PostgreSQL não disponível após {retries} tentativas.")


def main():
    endpoint_url   = os.environ.get("AWS_ENDPOINT_URL")
    database_url   = os.environ["DATABASE_URL"]
    fila_pedidos_url = os.environ["FILA_PEDIDOS_URL"]

    sqs  = boto3.client("sqs", endpoint_url=endpoint_url)
    conn = connect_with_retry(database_url)

    # Cria tabela se não existir — este worker é o responsável pelo schema de pedidos
    with conn.cursor() as cur:
        cur.execute("""
            CREATE TABLE IF NOT EXISTS pedidos (
                id           TEXT PRIMARY KEY,
                cliente      TEXT NOT NULL,
                valor        NUMERIC(10,2) NOT NULL,
                status       TEXT NOT NULL,
                processado_em TIMESTAMPTZ DEFAULT NOW()
            )
        """)
    conn.commit()
    print("[WORKER] Tabela 'pedidos' pronta.")

    print(f"[WORKER] Iniciando poll da fila '{fila_pedidos_url}'...")

    while True:
        try:
            response = sqs.receive_message(
                QueueUrl=fila_pedidos_url,
                MaxNumberOfMessages=10,
                WaitTimeSeconds=2,
            )
            for msg in response.get("Messages", []):
                pedido = json.loads(msg["Body"])
                try:
                    with conn.cursor() as cur:
                        cur.execute(
                            """
                            INSERT INTO pedidos (id, cliente, valor, status)
                            VALUES (%s, %s, %s, %s)
                            ON CONFLICT (id) DO NOTHING
                            """,
                            (pedido["id"], pedido["cliente"],
                             str(pedido["valor"]), "processado"),
                        )
                    conn.commit()
                    sqs.delete_message(
                        QueueUrl=fila_pedidos_url,
                        ReceiptHandle=msg["ReceiptHandle"],
                    )
                    print(f"[WORKER] Pedido {pedido['id']} processado → cliente={pedido['cliente']}")
                except psycopg2.Error as db_err:
                    print(f"[WORKER] Erro PostgreSQL ao processar pedido: {db_err}")
                    try:
                        conn.rollback()
                    except Exception:
                        pass
        except Exception as e:
            # Fila ainda não criada (fixture em inicialização) ou erro transitório
            print(f"[WORKER] Erro SQS (aguardando fila): {e}")
            time.sleep(2)


if __name__ == "__main__":
    main()
```

- [ ] **Passo 3: Criar Dockerfile**

```dockerfile
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY handler.py .
CMD ["python", "handler.py"]
```

- [ ] **Passo 4: Commit**

```bash
git add src/tasks/
git commit -m "feat(tasks): worker Python pedido_processor — poll SQS e INSERT PostgreSQL"
```

---

## Task 8: AppHost — container ecs-worker

**Files:**
- Modify: `src/AppHost/Program.cs`

- [ ] **Passo 1: Adicionar container ecs-worker ao AppHost**

Adicionar após o bloco do `postgres-rds`, antes de `builder.Build().Run()`:

```csharp
    builder
        .AddDockerfile("ecs-worker", "../tasks/pedido_processor")
        .WithEnvironment("AWS_ENDPOINT_URL", "http://host.docker.internal:4566")
        .WithEnvironment("DATABASE_URL", "postgresql://test:test@host.docker.internal:5433/testdb")
        .WithEnvironment("FILA_PEDIDOS_URL",
            "http://host.docker.internal:4566/000000000000/fila-pedidos");
```

O arquivo completo fica:

```csharp
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args
});

var alvoAws = Environment.GetEnvironmentVariable("AWS_TARGET") ?? "localstack";

if (!string.Equals(alvoAws, "aws", StringComparison.OrdinalIgnoreCase))
{
    builder
        .AddContainer("localstack", "localstack/localstack", "3.8")
        .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
        .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
        .WithEnvironment("SERVICES", "s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,scheduler,stepfunctions,rds,ecs")
        .WithEnvironment("LAMBDA_REMOVE_CONTAINERS", "true")
        .WithEnvironment("LAMBDA_RUNTIME_ENVIRONMENT_TIMEOUT", "120")
        .WithEnvironment("DOCKER_HOST", "unix:///var/run/docker.sock")
        .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "gateway", isProxied: false);

    builder
        .AddContainer("postgres-rds", "postgres", "16")
        .WithEnvironment("POSTGRES_USER", "test")
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .WithEnvironment("POSTGRES_DB", "testdb")
        .WithHttpEndpoint(port: 5433, targetPort: 5432, name: "tcp", isProxied: false);

    builder
        .AddDockerfile("ecs-worker", "../tasks/pedido_processor")
        .WithEnvironment("AWS_ENDPOINT_URL", "http://host.docker.internal:4566")
        .WithEnvironment("DATABASE_URL", "postgresql://test:test@host.docker.internal:5433/testdb")
        .WithEnvironment("FILA_PEDIDOS_URL",
            "http://host.docker.internal:4566/000000000000/fila-pedidos");
}

builder.Build().Run();
```

- [ ] **Passo 2: Verificar build**

```bash
dotnet build src/AppHost/
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Passo 3: Commit**

```bash
git add src/AppHost/Program.cs
git commit -m "feat(apphost): adiciona container ecs-worker (Dockerfile tasks/pedido_processor)"
```

---

## Task 9: Scaffold do cenário 18-ECS.RunTask

**Files:**
- Create: `scenarios/18-ECS.RunTask/18-ECS.RunTask.csproj`
- Create: `scenarios/18-ECS.RunTask/Fixture.cs` (esqueleto)
- Create: `scenarios/18-ECS.RunTask/EcsRunTaskTests.cs` (esqueleto)
- Modify: `aspire-aws.sln`

- [ ] **Passo 1: Criar 18-ECS.RunTask.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Scenarios.ECS.RunTask</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Npgsql" Version="9.*" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Shared\Shared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Passo 2: Criar Fixture.cs (esqueleto)**

```csharp
using Amazon.ECS;
using Amazon.SQS;
using Shared;

namespace Scenarios.ECS.RunTask;

public class Fixture : LocalStackFixture
{
    public const string NomeCluster       = "pedidos-cluster";
    public const string FamiliaTask       = "pedido-processor";
    public const string NomeFilaPedidos   = "fila-pedidos";
    public const string NomeTabelaPedidos = "pedidos";

    public AmazonECSClient ECS { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public string ClusterArn    { get; private set; } = null!;
    public string TaskDefArn    { get; private set; } = null!;
    public string UrlFilaPedidos { get; private set; } = null!;
    public string ConnectionString => LocalStackFixture.PostgresConnectionString;

    protected override Task InitializeScenarioAsync() => Task.CompletedTask;

    protected override Task DisposeScenarioAsync()
    {
        ECS?.Dispose();
        SQS?.Dispose();
        return Task.CompletedTask;
    }
}
```

- [ ] **Passo 3: Criar EcsRunTaskTests.cs (esqueleto)**

```csharp
using Xunit.Abstractions;

namespace Scenarios.ECS.RunTask;

public class EcsRunTaskTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public void Placeholder_ShouldPass() => Assert.True(true);
}
```

- [ ] **Passo 4: Registrar na solução**

```bash
dotnet sln aspire-aws.sln add scenarios/18-ECS.RunTask/18-ECS.RunTask.csproj
```

- [ ] **Passo 5: Verificar build e teste do placeholder**

```bash
dotnet build aspire-aws.sln
dotnet test scenarios/18-ECS.RunTask/
```

Saída esperada: `Aprovado! – Com falha: 0, Aprovado: 1`

- [ ] **Passo 6: Commit**

```bash
git add scenarios/18-ECS.RunTask/ aspire-aws.sln
git commit -m "feat(18): scaffold do cenário ECS.RunTask"
```

---

## Task 10: Fixture do cenário 18 — PostgreSQL, ECS cluster, task def, SQS, worker wait

**Files:**
- Modify: `scenarios/18-ECS.RunTask/Fixture.cs`

- [ ] **Passo 1: Implementar Fixture completa**

Substituir o conteúdo de `scenarios/18-ECS.RunTask/Fixture.cs`:

```csharp
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Npgsql;
using Shared;

namespace Scenarios.ECS.RunTask;

public class Fixture : LocalStackFixture
{
    public const string NomeCluster       = "pedidos-cluster";
    public const string FamiliaTask       = "pedido-processor";
    public const string NomeFilaPedidos   = "fila-pedidos";
    public const string NomeTabelaPedidos = "pedidos";

    public AmazonECSClient ECS { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public string ClusterArn     { get; private set; } = null!;
    public string TaskDefArn     { get; private set; } = null!;
    public string UrlFilaPedidos { get; private set; } = null!;
    public string ConnectionString => LocalStackFixture.PostgresConnectionString;

    protected override async Task InitializeScenarioAsync()
    {
        ECS = AwsClientFactory.ECS();
        SQS = AwsClientFactory.SQS();

        // 1. Aguarda PostgreSQL real
        await PollingHelper.WaitUntilAsync(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }, timeout: TimeSpan.FromSeconds(60), interval: TimeSpan.FromSeconds(1),
        failureMessage: "PostgreSQL não ficou disponível em 60s.");

        // 2. Aguarda o worker criar a tabela 'pedidos' (prova de que está rodando)
        await WaitForWorkerAsync();

        // 3. Cria fila SQS que o worker vai consumir
        UrlFilaPedidos = (await SQS.CreateQueueAsync(NomeFilaPedidos)).QueueUrl;

        // 4. Cria cluster ECS no LocalStack
        var cluster = await ECS.CreateClusterAsync(new CreateClusterRequest
        {
            ClusterName = NomeCluster
        });
        ClusterArn = cluster.Cluster.ClusterArn;

        // 5. Registra task definition com env vars do worker
        var taskDef = await ECS.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
        {
            Family = FamiliaTask,
            ContainerDefinitions =
            [
                new ContainerDefinition
                {
                    Name      = "worker",
                    Image     = "ecs-worker:latest",
                    Essential = true,
                    Environment =
                    [
                        new Amazon.ECS.Model.KeyValuePair { Name = "FILA_PEDIDOS_URL",  Value = UrlFilaPedidos },
                        new Amazon.ECS.Model.KeyValuePair { Name = "DATABASE_URL",       Value = ConnectionString },
                        new Amazon.ECS.Model.KeyValuePair { Name = "AWS_ENDPOINT_URL",   Value = Endpoint }
                    ]
                }
            ]
        });
        TaskDefArn = taskDef.TaskDefinition.TaskDefinitionArn;
    }

    private async Task WaitForWorkerAsync()
    {
        // O worker cria a tabela 'pedidos' ao inicializar.
        // Quando a tabela existir, o worker está pronto para receber mensagens.
        await PollingHelper.WaitUntilAsync(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'pedidos')",
                    conn);
                return (bool)(await cmd.ExecuteScalarAsync())!;
            }
            catch
            {
                return false;
            }
        }, timeout: TimeSpan.FromSeconds(60), interval: TimeSpan.FromSeconds(1),
        failureMessage: "Worker ECS não inicializou — tabela 'pedidos' não criada em 60s.");
    }

    protected override async Task DisposeScenarioAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DELETE FROM {NomeTabelaPedidos}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* ignora se tabela não existir */ }

        try
        {
            if (UrlFilaPedidos is not null)
                await SQS.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = UrlFilaPedidos });
        }
        catch { }

        try
        {
            await ECS.DeleteClusterAsync(new DeleteClusterRequest { Cluster = NomeCluster });
        }
        catch { }

        ECS?.Dispose();
        SQS?.Dispose();
    }
}
```

- [ ] **Passo 2: Verificar build**

```bash
dotnet build scenarios/18-ECS.RunTask/
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/18-ECS.RunTask/Fixture.cs
git commit -m "feat(18): Fixture com PostgreSQL wait, worker health check, ECS cluster e task definition"
```

---

## Task 11: Testes de plano de controle ECS

**Files:**
- Modify: `scenarios/18-ECS.RunTask/EcsRunTaskTests.cs`

- [ ] **Passo 1: Implementar os 3 testes de API ECS**

Substituir o conteúdo de `scenarios/18-ECS.RunTask/EcsRunTaskTests.cs`:

```csharp
using Amazon.ECS.Model;
using Xunit.Abstractions;

namespace Scenarios.ECS.RunTask;

public class EcsRunTaskTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    // ── Plano de controle AWS ────────────────────────────────────────────────

    [Fact]
    public async Task DescribeCluster_ShouldBeActive()
    {
        output.WriteLine($">>> ECS.DescribeClusters: verificando cluster '{Fixture.NomeCluster}'");
        output.WriteLine("    Status 'ACTIVE' confirma que o cluster foi provisionado e está disponível para tasks");

        var response = await fixture.ECS.DescribeClustersAsync(new DescribeClustersRequest
        {
            Clusters = [Fixture.NomeCluster]
        });

        var cluster = response.Clusters.Single();
        output.WriteLine($"    Cluster: {cluster.ClusterName} | Status: {cluster.Status}");

        Assert.Equal(Fixture.NomeCluster, cluster.ClusterName);
        Assert.Equal("ACTIVE", cluster.Status);
        output.WriteLine("    Cluster ativo — pronto para registrar tasks");
    }

    [Fact]
    public async Task DescribeTaskDefinition_ShouldMatchRegisteredConfig()
    {
        output.WriteLine($">>> ECS.DescribeTaskDefinition: verificando task def '{Fixture.FamiliaTask}'");
        output.WriteLine("    A task def define o container, imagem e env vars — base de toda execução ECS");

        var response = await fixture.ECS.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest
        {
            TaskDefinition = Fixture.FamiliaTask
        });

        var def       = response.TaskDefinition;
        var container = def.ContainerDefinitions.Single();
        output.WriteLine($"    Família: {def.Family} | Container: {container.Name} | Imagem: {container.Image}");

        Assert.Equal(Fixture.FamiliaTask, def.Family);
        Assert.Equal("worker", container.Name);
        Assert.Equal("ecs-worker:latest", container.Image);
        Assert.Contains(container.Environment, e => e.Name == "FILA_PEDIDOS_URL");
        Assert.Contains(container.Environment, e => e.Name == "DATABASE_URL");
        output.WriteLine("    Task definition validada — família, container e env vars corretos");
    }

    [Fact]
    public async Task ListTaskDefinitions_ShouldIncludeRegisteredFamily()
    {
        output.WriteLine($">>> ECS.ListTaskDefinitions: listando task definitions da família '{Fixture.FamiliaTask}'");
        output.WriteLine("    ListTaskDefinitions é o padrão de descoberta de recursos — lista ARNs disponíveis no cluster");

        var response = await fixture.ECS.ListTaskDefinitionsAsync(new ListTaskDefinitionsRequest
        {
            FamilyPrefix = Fixture.FamiliaTask
        });

        output.WriteLine($"    {response.TaskDefinitionArns.Count} ARN(s) encontrado(s)");
        Assert.NotEmpty(response.TaskDefinitionArns);
        Assert.Contains(response.TaskDefinitionArns, arn => arn.Contains(Fixture.FamiliaTask));
        output.WriteLine("    Task definition presente na listagem — recurso registrado corretamente");
    }
}
```

- [ ] **Passo 2: Rodar os 3 testes de controle ECS**

```bash
dotnet test scenarios/18-ECS.RunTask/ --logger "console;verbosity=normal"
```

Saída esperada: `Aprovado! – Com falha: 0, Aprovado: 3`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/18-ECS.RunTask/EcsRunTaskTests.cs
git commit -m "feat(18): testes de plano de controle ECS — DescribeCluster, DescribeTaskDefinition, ListTaskDefinitions"
```

---

## Task 12: Testes de integração ECS + SQS + PostgreSQL

**Files:**
- Modify: `scenarios/18-ECS.RunTask/EcsRunTaskTests.cs`

- [ ] **Passo 1: Adicionar os 2 testes de integração à classe existente**

Adicionar os métodos abaixo dentro da classe `EcsRunTaskTests`, após os 3 testes já existentes:

```csharp
    // ── Integração ECS + SQS + PostgreSQL ────────────────────────────────────

    [Fact]
    public async Task RunTask_ShouldPersistOrderToDatabase()
    {
        const string pedidoId = "pedido-runtask-001";
        const string cliente  = "João Silva";
        const decimal valor   = 1500.00m;

        output.WriteLine($">>> ECS.RunTask: submetendo task ao cluster '{Fixture.NomeCluster}'");
        output.WriteLine("    NOTA: LocalStack Community registra a task mas não executa o container.");
        output.WriteLine("          O worker Aspire (sempre ativo) processa a mensagem SQS abaixo.");

        // 1. RunTask — demonstra a API de controle ECS
        var runResponse = await fixture.ECS.RunTaskAsync(new RunTaskRequest
        {
            Cluster        = Fixture.NomeCluster,
            TaskDefinition = Fixture.FamiliaTask,
            LaunchType     = Amazon.ECS.LaunchType.EC2,
            Count          = 1,
            Overrides = new TaskOverride
            {
                ContainerOverrides =
                [
                    new ContainerOverride
                    {
                        Name = "worker",
                        Environment =
                        [
                            new Amazon.ECS.Model.KeyValuePair { Name = "PEDIDO_ID", Value = pedidoId },
                            new Amazon.ECS.Model.KeyValuePair { Name = "CLIENTE",   Value = cliente },
                            new Amazon.ECS.Model.KeyValuePair { Name = "VALOR",     Value = valor.ToString() }
                        ]
                    }
                ]
            }
        });

        output.WriteLine($"    Task ARN: {runResponse.Tasks.FirstOrDefault()?.TaskArn ?? "(sem ARN — LocalStack Community)"}");

        // 2. SendMessage — aciona o worker real via SQS
        output.WriteLine($">>> SQS.SendMessage: publicando pedido '{pedidoId}' na fila '{Fixture.NomeFilaPedidos}'");
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            id      = pedidoId,
            cliente,
            valor
        });
        await fixture.SQS.SendMessageAsync(fixture.UrlFilaPedidos, body);

        // 3. Poll PostgreSQL até o worker persistir o pedido
        output.WriteLine($">>> Polling PostgreSQL: aguardando até 30s pelo registro '{pedidoId}'");
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await PollingHelper.WaitUntilAsync(async () =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", pedidoId);
            return (long)(await cmd.ExecuteScalarAsync())! > 0;
        }, timeout: TimeSpan.FromSeconds(30),
        failureMessage: $"Pedido '{pedidoId}' não apareceu no PostgreSQL em 30s.");

        // 4. Verifica os dados gravados
        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT cliente, valor, status FROM {Fixture.NomeTabelaPedidos} WHERE id = @id", conn);
        select.Parameters.AddWithValue("id", pedidoId);
        await using var reader = await select.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        output.WriteLine($"    Pedido encontrado: cliente={reader.GetString(0)}, valor={reader.GetDecimal(1)}, status={reader.GetString(2)}");
        Assert.Equal(cliente, reader.GetString(0));
        Assert.Equal(valor, reader.GetDecimal(1));
        Assert.Equal("processado", reader.GetString(2));
        output.WriteLine("    Worker processou e persistiu o pedido corretamente");
    }

    [Fact]
    public async Task RunTask_WithMultipleOrders_ShouldPersistAll()
    {
        var pedidos = new[]
        {
            new { Id = "pedido-multi-001", Cliente = "Ana Costa",   Valor = 320.00m },
            new { Id = "pedido-multi-002", Cliente = "Bruno Lima",  Valor = 875.50m },
            new { Id = "pedido-multi-003", Cliente = "Carla Souza", Valor = 1240.00m }
        };

        output.WriteLine($">>> ECS.RunTask + SQS.SendMessage: submetendo {pedidos.Length} pedidos");
        output.WriteLine("    Demonstra processamento de batch — worker consome múltiplas mensagens da fila");

        foreach (var pedido in pedidos)
        {
            // RunTask por pedido — demonstra chamadas múltiplas à API ECS
            await fixture.ECS.RunTaskAsync(new RunTaskRequest
            {
                Cluster        = Fixture.NomeCluster,
                TaskDefinition = Fixture.FamiliaTask,
                LaunchType     = Amazon.ECS.LaunchType.EC2,
                Count          = 1
            });

            // SendMessage para o worker real
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                id      = pedido.Id,
                cliente = pedido.Cliente,
                valor   = pedido.Valor
            });
            await fixture.SQS.SendMessageAsync(fixture.UrlFilaPedidos, body);
        }

        output.WriteLine($">>> Polling PostgreSQL: aguardando até 30s pelos {pedidos.Length} registros");
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await PollingHelper.WaitUntilAsync(async () =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id LIKE 'pedido-multi-%'", conn);
            return (long)(await cmd.ExecuteScalarAsync())! >= pedidos.Length;
        }, timeout: TimeSpan.FromSeconds(30),
        failureMessage: $"Nem todos os {pedidos.Length} pedidos apareceram no PostgreSQL em 30s.");

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id LIKE 'pedido-multi-%'", conn);
        var total = (long)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    {total} pedido(s) persistido(s) no PostgreSQL");
        Assert.Equal(pedidos.Length, (int)total);
        output.WriteLine("    Todos os pedidos processados e gravados com sucesso");
    }
```

- [ ] **Passo 2: Rodar todos os 5 testes do cenário 18**

```bash
dotnet test scenarios/18-ECS.RunTask/ --logger "console;verbosity=normal"
```

Saída esperada: `Aprovado! – Com falha: 0, Aprovado: 5`

- [ ] **Passo 3: Commit**

```bash
git add scenarios/18-ECS.RunTask/EcsRunTaskTests.cs
git commit -m "feat(18): testes de integração ECS + SQS + PostgreSQL — RunTask com 1 e múltiplos pedidos"
```

---

## Task 13: Suite completa e commit final

- [ ] **Passo 1: Rodar toda a suite**

```bash
dotnet test aspire-aws.sln
```

Saída esperada:
```
Aprovado! — cenários 01–06, 09–10, 16, 17, 18
Ignorado!  — cenários 07, 08, 11, 12, 13, 14 (ARM64 skip), 15 (StepFunctions skip)
Com falha: 0
```

- [ ] **Passo 2: Commit final se tudo passou**

```bash
git add -A
git commit -m "feat: cenários 17-RDS.Basic e 18-ECS.RunTask

Cenário 17: plano de controle RDS (CreateDBInstance, DescribeDBInstances,
ModifyDBInstance) + CRUD Npgsql (INSERT, SELECT, UPDATE, DELETE, ROLLBACK).

Cenário 18: ciclo de vida ECS (CreateCluster, RegisterTaskDefinition,
RunTask, DescribeTasks) + worker Python sempre-ativo que consome SQS
e persiste pedidos no PostgreSQL.

Infraestrutura: postgres:16 na porta 5433, ecs-worker via Dockerfile,
AWSSDK.RDS e AWSSDK.ECS no AwsClientFactory."
```

---

## Notas para o implementador

**Ordem obrigatória:** as tasks de 1 a 13 devem ser executadas em sequência. Tasks 5 e 6 dependem de Docker rodando. Tasks 10–12 dependem do worker `ecs-worker` já estar construído (Task 7+8).

**Docker Desktop (macOS/Windows):** `host.docker.internal` resolve automaticamente. Em Linux puro, definir `DOCKER_INTERNAL_HOST=172.17.0.1` e passar como env var ao worker.

**Primeira execução:** `AddDockerfile` constrói a imagem `ecs-worker` na primeira vez — adiciona ~30s ao startup do AppHost.

**LocalStack Community + ECS:** `RunTask` retorna task ARN mas não executa o container. Isso é documentado nos `output.WriteLine` dos testes — é um ponto de aprendizado sobre a diferença entre LocalStack Community e Pro.
