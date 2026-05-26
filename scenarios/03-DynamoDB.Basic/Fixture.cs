using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Shared;

namespace Scenarios.DynamoDB.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string TableName = "items";

    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
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
    }

    protected override Task DisposeScenarioAsync()
    {
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
