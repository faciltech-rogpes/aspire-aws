using System.Diagnostics;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Npgsql;
using Shared;
using Task = System.Threading.Tasks.Task;

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

    // ECS control plane (CreateCluster, RegisterTaskDefinition) é Pro feature.
    // Quando não disponível, testes de plano de controle são pulados mas integração SQS/PG funciona.
    public bool EcsApiDisponivel { get; private set; }

    private string? _workerContainerId;

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

        // 2. Constrói imagem e inicia o worker via Docker CLI (isolado do AppHost)
        await BuildAndStartWorkerAsync();

        // 3. Aguarda o worker criar a tabela 'pedidos' (prova de que está rodando)
        await WaitForWorkerAsync();

        // 4. Cria fila SQS que o worker vai consumir
        UrlFilaPedidos = (await SQS.CreateQueueAsync(NomeFilaPedidos)).QueueUrl;

        // 5. Tenta criar cluster ECS — pode falhar no LocalStack Community (Pro feature)
        try
        {
            var cluster = await ECS.CreateClusterAsync(new CreateClusterRequest
            {
                ClusterName = NomeCluster
            });
            ClusterArn = cluster.Cluster.ClusterArn;

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
            EcsApiDisponivel = true;
        }
        catch (AmazonECSException ex) when (ex.Message.Contains("not yet implemented") || ex.Message.Contains("pro feature"))
        {
            // LocalStack Community não suporta ECS control plane — integração SQS/PG ainda funciona
            EcsApiDisponivel = false;
        }
    }

    // ── Worker Docker ────────────────────────────────────────────────────────

    private async Task BuildAndStartWorkerAsync()
    {
        var workerPath = Path.Combine(ResolveSolutionRoot(), "src", "tasks", "pedido_processor");

        // Para containers antigos para evitar consumers duplicados na fila
        await StopExistingWorkerContainersAsync();

        // Dropa a tabela para que WaitForWorkerAsync aguarde o NOVO container criar.
        // Sem isso, uma tabela deixada de runs anteriores faz WaitForWorkerAsync retornar
        // antes do novo container estar pronto para consumir SQS.
        await DropPedidosTableAsync();

        // Build da imagem (idempotente — usa cache Docker se já existir)
        await RunDockerAsync("build", workerPath, "-t", "ecs-worker:latest");

        // Inicia o container com nome único para evitar conflitos entre runs.
        // Nota: no Docker Desktop (macOS/Windows) host.docker.internal está disponível nativamente.
        // Em Linux puro, adicionar --add-host host.docker.internal:host-gateway manualmente.
        var containerName = $"ecs-worker-{Guid.NewGuid():N}";
        _workerContainerId = await RunDockerAsync(
            "run", "-d",
            "--name", containerName,
            "-e", "AWS_ENDPOINT_URL=http://host.docker.internal:4566",
            "-e", "AWS_DEFAULT_REGION=us-east-1",
            "-e", "AWS_ACCESS_KEY_ID=test",
            "-e", "AWS_SECRET_ACCESS_KEY=test",
            "-e", "DATABASE_URL=postgresql://test:test@host.docker.internal:5433/testdb",
            "-e", "FILA_PEDIDOS_URL=http://host.docker.internal:4566/000000000000/fila-pedidos",
            "ecs-worker:latest"
        );
    }

    private async Task DropPedidosTableAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("DROP TABLE IF EXISTS pedidos", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* ignora — tabela pode não existir */ }
    }

    private static async Task StopExistingWorkerContainersAsync()
    {
        try
        {
            var ids = await RunDockerAsync("ps", "-q", "--filter", "ancestor=ecs-worker:latest");
            foreach (var id in ids.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try { await RunDockerAsync("rm", "-f", id); }
                catch { /* ignora */ }
            }
        }
        catch { /* ignora se não houver containers */ }
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
        }, timeout: TimeSpan.FromSeconds(120), interval: TimeSpan.FromSeconds(1),
        failureMessage: "Worker ECS não inicializou — tabela 'pedidos' não criada em 120s.");
    }

    private static string ResolveSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "aspire-aws.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Raiz da solução não encontrada.");
    }

    private static async Task<string> RunDockerAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error  = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"docker {string.Join(" ", args)} falhou (exit {process.ExitCode}): {error}");

        return output.Trim();
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    protected override async Task DisposeScenarioAsync()
    {
        // Para e remove o worker container
        if (_workerContainerId is not null)
        {
            try { await RunDockerAsync("rm", "-f", _workerContainerId); }
            catch { /* ignora falhas de limpeza */ }
        }

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

        if (EcsApiDisponivel)
        {
            try
            {
                await ECS.DeleteClusterAsync(new DeleteClusterRequest { Cluster = NomeCluster });
            }
            catch { }
        }

        ECS?.Dispose();
        SQS?.Dispose();
    }
}
