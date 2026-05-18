using Amazon.SQS.Model;
using Xunit.Abstractions;

namespace Scenarios.SNS.SQS.Fanout;

public class SnsSqsFanoutTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task Publish_ShouldDeliverMessageToBothQueues()
    {
        output.WriteLine($">>> SNS.Publish: publicando 'broadcast event' no tópico '{fixture.TopicArn}'");
        output.WriteLine("    O tópico tem duas filas SQS inscritas — cada publicação entrega uma cópia para cada fila (fanout)");
        await fixture.SNS.PublishAsync(fixture.TopicArn, "broadcast event");

        foreach (var queueUrl in new[] { fixture.Queue1Url, fixture.Queue2Url })
        {
            output.WriteLine($">>> SQS.ReceiveMessage: verificando entrega na fila '{queueUrl}'");
            var messages = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 5
            });
            output.WriteLine($"    Mensagens recebidas: {messages.Messages.Count}");

            Assert.Single(messages.Messages);
            Assert.Contains("broadcast event", messages.Messages[0].Body);
        }
    }
}
