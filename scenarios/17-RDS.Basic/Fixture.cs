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
    public bool RdsApiAvailable { get; private set; }

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

        // 2. Tenta criar instância RDS no LocalStack (plano de controle AWS).
        //    LocalStack Community 3.8 não suporta a API RDS — apenas a edição Pro.
        //    A tentativa é registrada mas não bloqueia a fixture.
        try
        {
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

            // 3. Aguarda status "available" (LocalStack Pro transiciona rapidamente)
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

            RdsApiAvailable = true;
        }
        catch (Amazon.RDS.AmazonRDSException)
        {
            // API RDS não disponível no LocalStack Community — testes de plano de controle serão pulados.
            RdsApiAvailable = false;
        }

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
