using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Amazon.SQS;
using Shared;

namespace Scenarios.SQS.LambdaConsumer;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string FunctionName = "sqs-consumer";
    public const string TableName = "consumed-messages";

    public AmazonSQSClient SQS { get; private set; } = null!;
    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;
    public string QueueArn { get; private set; } = null!;
    public string QueueUrl { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        SQS = AwsClientFactory.SQS();
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
            "sqs_consumer",
            new Dictionary<string, string> { ["DYNAMODB_TABLE"] = TableName });

        QueueUrl = (await SQS.CreateQueueAsync("consumer-queue")).QueueUrl;
        QueueArn = (await SQS.GetQueueAttributesAsync(QueueUrl, ["QueueArn"])).Attributes["QueueArn"];

        var mapping = await lambda.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
        {
            FunctionName = FunctionName,
            EventSourceArn = QueueArn,
            BatchSize = 1,
            Enabled = true
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await lambda.GetEventSourceMappingAsync(new GetEventSourceMappingRequest
            {
                UUID = mapping.UUID
            });

            return string.Equals(response.State, "Enabled", StringComparison.OrdinalIgnoreCase);
        }, timeout: TimeSpan.FromSeconds(30));
    }

    protected override Task DisposeScenarioAsync()
    {
        SQS.Dispose();
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
