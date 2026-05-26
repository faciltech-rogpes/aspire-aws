using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shared;

namespace Scenarios.SNS.SQS.Fanout;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonSimpleNotificationServiceClient SNS { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;
    public string Queue1Url { get; private set; } = null!;
    public string Queue2Url { get; private set; } = null!;
    public string TopicArn { get; private set; } = null!;

    protected override async Task InitializeScenarioAsync()
    {
        SNS = AwsClientFactory.SNS();
        SQS = AwsClientFactory.SQS();

        TopicArn = (await SNS.CreateTopicAsync("fanout-topic")).TopicArn;
        Queue1Url = (await SQS.CreateQueueAsync("fanout-queue-1")).QueueUrl;
        Queue2Url = (await SQS.CreateQueueAsync("fanout-queue-2")).QueueUrl;

        foreach (var queueUrl in new[] { Queue1Url, Queue2Url })
        {
            var queueArn = (await SQS.GetQueueAttributesAsync(queueUrl, ["QueueArn"])).Attributes["QueueArn"];

            await SQS.SetQueueAttributesAsync(new SetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                Attributes = new Dictionary<string, string>
                {
                    ["Policy"] = BuildQueuePolicy(queueArn, TopicArn)
                }
            });

            await SNS.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = TopicArn,
                Protocol = "sqs",
                Endpoint = queueArn
            });
        }
    }

    protected override Task DisposeScenarioAsync()
    {
        SNS.Dispose();
        SQS.Dispose();
        return Task.CompletedTask;
    }

    private static string BuildQueuePolicy(string queueArn, string topicArn)
    {
        return $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Sid": "AllowSnsFanout",
              "Effect": "Allow",
              "Principal": { "Service": "sns.amazonaws.com" },
              "Action": "sqs:SendMessage",
              "Resource": "{{queueArn}}",
              "Condition": {
                "ArnEquals": { "aws:SourceArn": "{{topicArn}}" }
              }
            }
          ]
        }
        """;
    }
}
