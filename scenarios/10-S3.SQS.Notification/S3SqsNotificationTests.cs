using Amazon.S3.Model;
using Amazon.SQS.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.S3.SQS.Notification;

public class S3SqsNotificationTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task PutObject_ShouldSendNotificationToSqs()
    {
        output.WriteLine($">>> S3.PutObject: fazendo upload de 'upload.csv' no bucket '{Fixture.BucketName}'");
        output.WriteLine("    O bucket tem uma NotificationConfiguration que envia eventos s3:ObjectCreated para a fila SQS");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Fixture.BucketName,
            Key = "upload.csv",
            ContentBody = "col1,col2"
        });

        output.WriteLine($">>> Polling SQS: aguardando até 20s pela notificação de evento S3 na fila '{fixture.QueueUrl}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = fixture.QueueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 2
            });

            return response.Messages.Any(message =>
                message.Body.Contains("upload.csv", StringComparison.Ordinal));
        }, timeout: TimeSpan.FromSeconds(20));

        output.WriteLine("    Notificação recebida — o corpo da mensagem contém o evento S3 em formato JSON com a chave do objeto");
    }
}
