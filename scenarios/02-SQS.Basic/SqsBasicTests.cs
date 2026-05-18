using Amazon.SQS.Model;
using Xunit.Abstractions;

namespace Scenarios.SQS.Basic;

public class SqsBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task CreateQueue_ShouldSucceed()
    {
        output.WriteLine(">>> SQS.CreateQueue: criando fila 'test-queue-create'");
        var response = await fixture.SQS.CreateQueueAsync("test-queue-create");
        output.WriteLine($"    QueueUrl: {response.QueueUrl}");

        Assert.NotEmpty(response.QueueUrl);
    }

    [Fact]
    public async Task SendAndReceiveMessage_ShouldRoundTrip()
    {
        output.WriteLine(">>> SQS.CreateQueue: criando fila 'test-queue-rw'");
        var queueUrl = (await fixture.SQS.CreateQueueAsync("test-queue-rw")).QueueUrl;

        output.WriteLine(">>> SQS.SendMessage: enviando mensagem 'hello sqs'");
        await fixture.SQS.SendMessageAsync(queueUrl, "hello sqs");

        output.WriteLine(">>> SQS.ReceiveMessage: aguardando até 5s por uma mensagem (long polling)");
        var response = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5
        });
        output.WriteLine($"    Mensagens recebidas: {response.Messages.Count}, corpo: '{response.Messages[0].Body}'");

        Assert.Single(response.Messages);
        Assert.Equal("hello sqs", response.Messages[0].Body);
    }

    [Fact]
    public async Task DeleteMessage_ShouldRemoveFromQueue()
    {
        output.WriteLine(">>> SQS.CreateQueue: criando fila 'test-queue-delete'");
        var queueUrl = (await fixture.SQS.CreateQueueAsync("test-queue-delete")).QueueUrl;

        output.WriteLine(">>> SQS.SendMessage: enviando mensagem 'to-delete'");
        await fixture.SQS.SendMessageAsync(queueUrl, "to-delete");

        output.WriteLine(">>> SQS.ReceiveMessage: recebendo mensagem para obter o ReceiptHandle");
        var receive = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1
        });
        output.WriteLine($"    ReceiptHandle: {receive.Messages[0].ReceiptHandle[..20]}...");

        output.WriteLine(">>> SQS.DeleteMessage: removendo mensagem da fila usando o ReceiptHandle");
        await fixture.SQS.DeleteMessageAsync(queueUrl, receive.Messages[0].ReceiptHandle);

        output.WriteLine(">>> SQS.ReceiveMessage: confirmando que a fila está vazia");
        var afterDelete = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 1
        });
        output.WriteLine($"    Mensagens restantes: {afterDelete.Messages.Count}");

        Assert.Empty(afterDelete.Messages);
    }

    [Fact]
    public async Task DeadLetterQueue_ShouldBeConfigurable()
    {
        output.WriteLine(">>> SQS.CreateQueue: criando DLQ 'test-dlq'");
        var dlqUrl = (await fixture.SQS.CreateQueueAsync("test-dlq")).QueueUrl;

        output.WriteLine(">>> SQS.GetQueueAttributes: obtendo ARN da DLQ para usar na política de redrive");
        var dlqAttributes = await fixture.SQS.GetQueueAttributesAsync(dlqUrl, ["QueueArn"]);
        var dlqArn = dlqAttributes.Attributes["QueueArn"];
        output.WriteLine($"    DLQ ARN: {dlqArn}");

        output.WriteLine(">>> SQS.CreateQueue: criando fila 'test-queue-dlq' com RedrivePolicy apontando para a DLQ (maxReceiveCount=1)");
        var queueUrl = (await fixture.SQS.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = "test-queue-dlq",
            Attributes = new Dictionary<string, string>
            {
                ["RedrivePolicy"] = $$"""{"deadLetterTargetArn":"{{dlqArn}}","maxReceiveCount":"1"}"""
            }
        })).QueueUrl;

        output.WriteLine(">>> SQS.GetQueueAttributes: verificando se o RedrivePolicy foi aplicado");
        var attributes = await fixture.SQS.GetQueueAttributesAsync(queueUrl, ["RedrivePolicy"]);
        output.WriteLine($"    RedrivePolicy: {attributes.Attributes["RedrivePolicy"]}");

        Assert.Contains("deadLetterTargetArn", attributes.Attributes["RedrivePolicy"]);
    }
}
