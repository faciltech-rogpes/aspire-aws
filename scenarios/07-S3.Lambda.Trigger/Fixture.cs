using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Shared;

namespace Scenarios.S3.LambdaTrigger;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string BucketName = "uploads";
    public const string FunctionName = "s3-processor";
    public const string TableName = "processed-files";

    public AmazonS3Client S3 { get; private set; } = null!;
    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        S3 = AwsClientFactory.S3();
        DynamoDB = AwsClientFactory.DynamoDB();

        await DynamoDB.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            AttributeDefinitions =
            [
                new AttributeDefinition("key", ScalarAttributeType.S)
            ],
            KeySchema =
            [
                new KeySchemaElement("key", KeyType.HASH)
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
            "s3_processor",
            new Dictionary<string, string> { ["DYNAMODB_TABLE"] = TableName });

        var function = await lambda.GetFunctionAsync(new GetFunctionRequest
        {
            FunctionName = FunctionName
        });

        await S3.PutBucketAsync(BucketName);

        await lambda.AddPermissionAsync(new AddPermissionRequest
        {
            Action = "lambda:InvokeFunction",
            FunctionName = FunctionName,
            Principal = "s3.amazonaws.com",
            StatementId = "allow-s3-invoke",
            SourceArn = $"arn:aws:s3:::{BucketName}"
        });

        await S3.PutBucketNotificationAsync(new PutBucketNotificationRequest
        {
            BucketName = BucketName,
            LambdaFunctionConfigurations =
            [
                new LambdaFunctionConfiguration
                {
                    Id = "trigger-on-upload",
                    FunctionArn = function.Configuration.FunctionArn,
                    Events = [EventType.ObjectCreatedPut]
                }
            ]
        });
    }

    protected override Task DisposeScenarioAsync()
    {
        S3.Dispose();
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
