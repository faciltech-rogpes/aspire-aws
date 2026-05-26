using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shared;

namespace Scenarios.Pipeline.S3SqsLambdaDynamoDb;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string BucketName = "pipeline-uploads";
    public const string FunctionName = "sqs-consumer-pipeline";
    public const string QueueName = "pipeline-queue";
    public const string TableName = "pipeline-results";

    public AmazonS3Client S3 { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        S3 = AwsClientFactory.S3();
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

        var queueUrl = (await SQS.CreateQueueAsync(QueueName)).QueueUrl;
        var queueArn = (await SQS.GetQueueAttributesAsync(queueUrl, ["QueueArn"])).Attributes["QueueArn"];

        await SQS.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = $$"""
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    {
                      "Sid": "AllowS3ToQueue",
                      "Effect": "Allow",
                      "Principal": { "Service": "s3.amazonaws.com" },
                      "Action": "sqs:SendMessage",
                      "Resource": "{{queueArn}}",
                      "Condition": {
                        "ArnLike": { "aws:SourceArn": "arn:aws:s3:::{{BucketName}}" }
                      }
                    }
                  ]
                }
                """
            }
        });

        await S3.PutBucketAsync(BucketName);

        await S3.PutBucketNotificationAsync(new PutBucketNotificationRequest
        {
            BucketName = BucketName,
            QueueConfigurations =
            [
                new QueueConfiguration
                {
                    Id = "pipeline-trigger",
                    Queue = queueArn,
                    Events = [EventType.ObjectCreatedPut]
                }
            ]
        });

        var mapping = await lambda.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
        {
            FunctionName = FunctionName,
            EventSourceArn = queueArn,
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
        S3.Dispose();
        SQS.Dispose();
        DynamoDB.Dispose();
        return Task.CompletedTask;
    }
}
