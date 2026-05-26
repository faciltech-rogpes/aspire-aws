using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shared;

namespace Scenarios.S3.SQS.Notification;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public const string BucketName = "notify-bucket";

    public AmazonS3Client S3 { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public string QueueUrl { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        S3 = AwsClientFactory.S3();
        SQS = AwsClientFactory.SQS();

        QueueUrl = (await SQS.CreateQueueAsync("s3-events-queue")).QueueUrl;
        var queueArn = (await SQS.GetQueueAttributesAsync(QueueUrl, ["QueueArn"])).Attributes["QueueArn"];

        await SQS.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = QueueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = $$"""
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    {
                      "Sid": "AllowS3Notifications",
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
                    Id = "notify-on-put",
                    Queue = queueArn,
                    Events = [EventType.ObjectCreatedPut]
                }
            ]
        });
    }

    protected override Task DisposeScenarioAsync()
    {
        S3.Dispose();
        SQS.Dispose();
        return Task.CompletedTask;
    }
}
