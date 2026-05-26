using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shared;

namespace Scenarios.Pipeline.SnsSqsLambdaS3;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string BucketName = "fanout-results";
    public const string FunctionName = "fanout-processor";

    public AmazonSimpleNotificationServiceClient SNS { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public AmazonS3Client S3 { get; private set; } = null!;
    public string TopicArn { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        SNS = AwsClientFactory.SNS();
        SQS = AwsClientFactory.SQS();
        S3 = AwsClientFactory.S3();

        await S3.PutBucketAsync(BucketName);

        using var lambda = AwsClientFactory.Lambda();
        await new LambdaDeployer(lambda).DeployAsync(
            FunctionName,
            "fanout_processor",
            new Dictionary<string, string> { ["S3_BUCKET"] = BucketName });

        TopicArn = (await SNS.CreateTopicAsync("fanout-topic-pipeline")).TopicArn;

        var queueUrl = (await SQS.CreateQueueAsync("fanout-queue")).QueueUrl;
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
                      "Sid": "AllowSnsPipeline",
                      "Effect": "Allow",
                      "Principal": { "Service": "sns.amazonaws.com" },
                      "Action": "sqs:SendMessage",
                      "Resource": "{{queueArn}}",
                      "Condition": {
                        "ArnEquals": { "aws:SourceArn": "{{TopicArn}}" }
                      }
                    }
                  ]
                }
                """
            }
        });

        await SNS.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = TopicArn,
            Protocol = "sqs",
            Endpoint = queueArn
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
        SNS.Dispose();
        SQS.Dispose();
        S3.Dispose();
        return Task.CompletedTask;
    }
}
