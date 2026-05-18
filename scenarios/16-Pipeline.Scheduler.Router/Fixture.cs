using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shared;

namespace Scenarios.Pipeline.Scheduler.Router;

public class Fixture : LocalStackFixture
{
    public const string NomeLambdaProdutora = "produtor-de-ofertas";
    public const string NomeLambdaRoteadora = "roteador-de-ofertas";
    public const string NomeLambdaEco       = "eco-consignado";
    public const string NomeFilaOfertas     = "fila-ofertas-credito";
    public const string NomeFilaConsignado  = "fila-consignado";
    public const string NomeTabelaRegras    = "regras-de-roteamento";
    public const string NomeAgendador       = "agendador-ofertas";

    public AmazonSQSClient      SQS      { get; private set; } = null!;
    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;

    public string UrlFilaOfertas    { get; private set; } = null!;
    public string UrlFilaConsignado { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        SQS      = AwsClientFactory.SQS();
        DynamoDB = AwsClientFactory.DynamoDB();

        await DynamoDB.CreateTableAsync(new CreateTableRequest
        {
            TableName            = NomeTabelaRegras,
            AttributeDefinitions = [new AttributeDefinition("segmento", ScalarAttributeType.S)],
            KeySchema            = [new KeySchemaElement("segmento", KeyType.HASH)],
            BillingMode          = BillingMode.PAY_PER_REQUEST
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var tabela = await DynamoDB.DescribeTableAsync(NomeTabelaRegras);
            return tabela.Table.TableStatus == TableStatus.ACTIVE;
        });

        UrlFilaOfertas    = (await SQS.CreateQueueAsync(NomeFilaOfertas)).QueueUrl;
        UrlFilaConsignado = (await SQS.CreateQueueAsync(NomeFilaConsignado)).QueueUrl;

        var arnFilaOfertas    = (await SQS.GetQueueAttributesAsync(UrlFilaOfertas,    ["QueueArn"])).Attributes["QueueArn"];
        var arnFilaConsignado = (await SQS.GetQueueAttributesAsync(UrlFilaConsignado, ["QueueArn"])).Attributes["QueueArn"];

        await DynamoDB.PutItemAsync(new PutItemRequest
        {
            TableName = NomeTabelaRegras,
            Item = new Dictionary<string, AttributeValue>
            {
                ["segmento"]     = new AttributeValue { S = "consignado" },
                ["fila_destino"] = new AttributeValue { S = UrlFilaConsignado }
            }
        });

        using var lambda = AwsClientFactory.Lambda();
        var deployer = new LambdaDeployer(lambda);

        await deployer.DeployAsync(NomeLambdaProdutora, "produtor_de_ofertas",
            new Dictionary<string, string> { ["FILA_OFERTAS_URL"] = UrlFilaOfertas });

        await deployer.DeployAsync(NomeLambdaRoteadora, "roteador_de_ofertas",
            new Dictionary<string, string>
            {
                ["TABELA_REGRAS"]       = NomeTabelaRegras,
                ["FILA_CONSIGNADO_URL"] = UrlFilaConsignado
            });

        await deployer.DeployAsync(NomeLambdaEco, "eco_consignado",
            new Dictionary<string, string>());

        var mappingRoteador = await lambda.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
        {
            FunctionName   = NomeLambdaRoteadora,
            EventSourceArn = arnFilaOfertas,
            BatchSize      = 1,
            Enabled        = true
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var r = await lambda.GetEventSourceMappingAsync(new GetEventSourceMappingRequest { UUID = mappingRoteador.UUID });
            return string.Equals(r.State, "Enabled", StringComparison.OrdinalIgnoreCase);
        }, timeout: TimeSpan.FromSeconds(30));

        var mappingEco = await lambda.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
        {
            FunctionName   = NomeLambdaEco,
            EventSourceArn = arnFilaConsignado,
            BatchSize      = 1,
            Enabled        = true
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var r = await lambda.GetEventSourceMappingAsync(new GetEventSourceMappingRequest { UUID = mappingEco.UUID });
            return string.Equals(r.State, "Enabled", StringComparison.OrdinalIgnoreCase);
        }, timeout: TimeSpan.FromSeconds(30));

        var produtora = await lambda.GetFunctionAsync(new GetFunctionRequest { FunctionName = NomeLambdaProdutora });

        // Em modo AWS real, obtém o account ID via STS para construir o ARN correto da role.
        // Em modo LocalStack, usa o account ID fictício padrão (000000000000).
        string accountId;
        using (var sts = AwsClientFactory.STS())
        {
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest());
            accountId = identity.Account;
        }

        using var scheduler = AwsClientFactory.Scheduler();
        await scheduler.CreateScheduleAsync(new CreateScheduleRequest
        {
            Name               = NomeAgendador,
            ScheduleExpression = "rate(20 seconds)",
            FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
            Target = new Amazon.Scheduler.Model.Target
            {
                Arn     = produtora.Configuration.FunctionArn,
                RoleArn = $"arn:aws:iam::{accountId}:role/{NomeAgendador}-role"
            }
        });
    }

    public async Task<ConfiguracaoDoAgendador> ObterConfiguracaoDoAgendadorAsync()
    {
        using var scheduler = AwsClientFactory.Scheduler();
        var schedule = await scheduler.GetScheduleAsync(new GetScheduleRequest { Name = NomeAgendador });
        var funcaoAlvo = schedule.Target.Arn.Split(':')[^1];
        return new ConfiguracaoDoAgendador(funcaoAlvo, schedule.ScheduleExpression);
    }

    protected override Task DisposeScenarioAsync()
    {
        SQS.Dispose();
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
