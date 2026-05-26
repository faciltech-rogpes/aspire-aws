using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda;
using Shared;

namespace Scenarios.DynamoDB.Lambda;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string FunctionName = "dynamodb-writer";
    public const string ResultTable = "processed-events";
    public const string SourceTable = "events";

    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;
    public AmazonLambdaClient Lambda { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        DynamoDB = AwsClientFactory.DynamoDB();
        Lambda = AwsClientFactory.Lambda();

        await new LambdaDeployer(Lambda).DeployAsync(
            FunctionName,
            "dynamodb_writer",
            new Dictionary<string, string> { ["DYNAMODB_TABLE"] = ResultTable });

        await DynamoDB.CreateTableAsync(new CreateTableRequest
        {
            TableName = SourceTable,
            AttributeDefinitions =
            [
                new AttributeDefinition("id", ScalarAttributeType.S)
            ],
            KeySchema =
            [
                new KeySchemaElement("id", KeyType.HASH)
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST,
            StreamSpecification = new StreamSpecification
            {
                StreamEnabled = true,
                StreamViewType = StreamViewType.NEW_IMAGE
            }
        });

        await DynamoDB.CreateTableAsync(new CreateTableRequest
        {
            TableName = ResultTable,
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
            var source = await DynamoDB.DescribeTableAsync(SourceTable);
            var result = await DynamoDB.DescribeTableAsync(ResultTable);
            return source.Table.TableStatus == TableStatus.ACTIVE &&
                   result.Table.TableStatus == TableStatus.ACTIVE;
        });
    }

    protected override Task DisposeScenarioAsync()
    {
        DynamoDB.Dispose();
        Lambda.Dispose();
        return Task.CompletedTask;
    }
}
