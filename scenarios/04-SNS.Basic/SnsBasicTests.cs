using Amazon.SimpleNotificationService.Model;
using Amazon.SQS.Model;
using Xunit.Abstractions;

namespace Scenarios.SNS.Basic;

public class SnsBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task CreateTopic_ShouldReturnArn()
    {
        output.WriteLine(">>> SNS.CreateTopic: criando tópico 'test-topic'");
        var response = await fixture.SNS.CreateTopicAsync("test-topic");
        output.WriteLine($"    TopicArn: {response.TopicArn}");

        Assert.Contains("test-topic", response.TopicArn);
    }

    [Fact]
    public async Task Publish_WithSqsSubscription_ShouldDeliverMessage()
    {
        output.WriteLine(">>> SNS.CreateTopic: criando tópico 'notify-topic'");
        var topicArn = (await fixture.SNS.CreateTopicAsync("notify-topic")).TopicArn;
        output.WriteLine($"    TopicArn: {topicArn}");

        output.WriteLine(">>> SQS.CreateQueue: criando fila 'notify-queue' que receberá as mensagens do tópico");
        var queueUrl = (await fixture.SQS.CreateQueueAsync("notify-queue")).QueueUrl;

        output.WriteLine(">>> SQS.GetQueueAttributes: obtendo ARN da fila para usar na subscription e na policy");
        var queueArn = (await fixture.SQS.GetQueueAttributesAsync(queueUrl, ["QueueArn"]))
            .Attributes["QueueArn"];
        output.WriteLine($"    QueueArn: {queueArn}");

        output.WriteLine(">>> SQS.SetQueueAttributes: aplicando policy IAM que permite ao SNS publicar na fila");
        await fixture.SQS.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = $$"""
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    {
                      "Sid": "AllowSnsPublish",
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
                """
            }
        });

        output.WriteLine(">>> SNS.Subscribe: inscrevendo a fila SQS no tópico via protocolo 'sqs'");
        await fixture.SNS.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn
        });

        output.WriteLine(">>> SNS.Publish: publicando mensagem 'hello from SNS' no tópico");
        await fixture.SNS.PublishAsync(topicArn, "hello from SNS");

        output.WriteLine(">>> SQS.ReceiveMessage: aguardando entrega da mensagem via SNS→SQS (até 5s)");
        var messages = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5
        });
        output.WriteLine($"    Mensagens recebidas: {messages.Messages.Count}");
        output.WriteLine($"    Corpo (envelope SNS): {messages.Messages[0].Body[..80]}...");

        Assert.Single(messages.Messages);
        Assert.Contains("hello from SNS", messages.Messages[0].Body);
    }

    [Fact]
    public async Task ListTopics_ShouldIncludeCreatedTopic()
    {
        output.WriteLine(">>> SNS.CreateTopic: criando tópico 'list-topic'");
        var topicArn = (await fixture.SNS.CreateTopicAsync("list-topic")).TopicArn;

        output.WriteLine(">>> SNS.ListTopics: listando todos os tópicos da conta");
        var response = await fixture.SNS.ListTopicsAsync();
        output.WriteLine($"    Tópicos encontrados: {response.Topics.Count}");

        Assert.Contains(response.Topics, topic => topic.TopicArn == topicArn);
    }
}
