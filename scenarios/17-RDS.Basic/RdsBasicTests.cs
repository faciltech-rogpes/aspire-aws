using Amazon.RDS.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.RDS.Basic;

public class RdsBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    // ── Plano de controle AWS ────────────────────────────────────────────────

    [Fact(Skip = EnvironmentLimitations.LocalStackRdsApiReason)]
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

    [Fact(Skip = EnvironmentLimitations.LocalStackRdsApiReason)]
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

    [Fact(Skip = EnvironmentLimitations.LocalStackRdsApiReason)]
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
}
