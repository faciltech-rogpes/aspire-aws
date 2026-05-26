using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Lambda.Model;
using Shared;

namespace Scenarios.EventBridge.Lambda;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string BusName = "custom-bus";
    public const string FunctionName = "eventbridge-handler";
    public const string RuleName = "order-events-rule";
    public const string TableName = "eb-events";

    public AmazonEventBridgeClient EventBridge { get; private set; } = null!;
    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        EventBridge = AwsClientFactory.EventBridge();
        DynamoDB = AwsClientFactory.DynamoDB();

        await DynamoDB.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            AttributeDefinitions =
            [
                new AttributeDefinition("id", ScalarAttributeType.S)
            ],
            KeySchema =
            [
                new KeySchemaElement("id", KeyType.HASH)
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var table = await DynamoDB.DescribeTableAsync(TableName);
            return table.Table.TableStatus == TableStatus.ACTIVE;
        });

        using var lambda = AwsClientFactory.Lambda();
        await new LambdaDeployer(lambda).DeployAsync(
            FunctionName,
            "eventbridge_handler",
            new Dictionary<string, string> { ["DYNAMODB_TABLE"] = TableName });

        var function = await lambda.GetFunctionAsync(new GetFunctionRequest
        {
            FunctionName = FunctionName
        });

        await EventBridge.CreateEventBusAsync(new CreateEventBusRequest
        {
            Name = BusName
        });

        var rule = await EventBridge.PutRuleAsync(new PutRuleRequest
        {
            Name = RuleName,
            EventBusName = BusName,
            EventPattern = """{"source":["myapp"]}""",
            State = RuleState.ENABLED
        });

        await lambda.AddPermissionAsync(new AddPermissionRequest
        {
            Action = "lambda:InvokeFunction",
            FunctionName = FunctionName,
            Principal = "events.amazonaws.com",
            StatementId = "allow-eventbridge-invoke",
            SourceArn = rule.RuleArn
        });

        await EventBridge.PutTargetsAsync(new PutTargetsRequest
        {
            Rule = RuleName,
            EventBusName = BusName,
            Targets =
            [
                new Target
                {
                    Id = "lambda-target",
                    Arn = function.Configuration.FunctionArn
                }
            ]
        });
    }

    protected override Task DisposeScenarioAsync()
    {
        EventBridge.Dispose();
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
