using Amazon.ECS.Model;
using Shared;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Scenarios.ECS.RunTask;

public class EcsRunTaskTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    // ── Plano de controle AWS ────────────────────────────────────────────────

    [Fact(Skip = EnvironmentLimitations.LocalStackEcsApiReason)]
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

    [Fact(Skip = EnvironmentLimitations.LocalStackEcsApiReason)]
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

    [Fact(Skip = EnvironmentLimitations.LocalStackEcsApiReason)]
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

        if (fixture.EcsApiDisponivel)
        {
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
            output.WriteLine($"    Task ARN: {runResponse.Tasks.FirstOrDefault()?.TaskArn ?? "(sem ARN)"}");
        }
        else
        {
            output.WriteLine("    ECS API não disponível no LocalStack Community — pulando RunTask, testando integração SQS/PG.");
        }

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
        output.WriteLine($">>> Polling PostgreSQL: aguardando até 120s pelo registro '{pedidoId}'");
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await PollingHelper.WaitUntilAsync(async () =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", pedidoId);
            return (long)(await cmd.ExecuteScalarAsync())! > 0;
        }, timeout: TimeSpan.FromSeconds(120),
        failureMessage: $"Pedido '{pedidoId}' não apareceu no PostgreSQL em 120s.");

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
            if (fixture.EcsApiDisponivel)
            {
                // RunTask por pedido — demonstra chamadas múltiplas à API ECS
                await fixture.ECS.RunTaskAsync(new RunTaskRequest
                {
                    Cluster        = Fixture.NomeCluster,
                    TaskDefinition = Fixture.FamiliaTask,
                    LaunchType     = Amazon.ECS.LaunchType.EC2,
                    Count          = 1
                });
            }

            // SendMessage para o worker real
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                id      = pedido.Id,
                cliente = pedido.Cliente,
                valor   = pedido.Valor
            });
            await fixture.SQS.SendMessageAsync(fixture.UrlFilaPedidos, body);
        }

        output.WriteLine($">>> Polling PostgreSQL: aguardando até 120s pelos {pedidos.Length} registros");
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await PollingHelper.WaitUntilAsync(async () =>
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id LIKE 'pedido-multi-%'", conn);
            return (long)(await cmd.ExecuteScalarAsync())! >= pedidos.Length;
        }, timeout: TimeSpan.FromSeconds(120),
        failureMessage: $"Nem todos os {pedidos.Length} pedidos apareceram no PostgreSQL em 120s.");

        await using var select = new Npgsql.NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Fixture.NomeTabelaPedidos} WHERE id LIKE 'pedido-multi-%'", conn);
        var total = (long)(await select.ExecuteScalarAsync())!;

        output.WriteLine($"    {total} pedido(s) persistido(s) no PostgreSQL");
        Assert.Equal(pedidos.Length, (int)total);
        output.WriteLine("    Todos os pedidos processados e gravados com sucesso");
    }
}
