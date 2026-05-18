using Amazon.DynamoDBv2.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.SQS.LambdaConsumer;

public class SqsLambdaConsumerTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task SendMessage_ShouldTriggerLambda_AndPersistToDynamoDb()
    {
        var messageBody = """{"event":"order-placed","orderId":"123"}""";
        output.WriteLine($">>> SQS.SendMessage: enviando mensagem para a fila '{fixture.QueueUrl}'");
        output.WriteLine($"    Payload: {messageBody}");
        output.WriteLine("    A fila tem um Event Source Mapping configurado que aciona a Lambda automaticamente");
        await fixture.SQS.SendMessageAsync(fixture.QueueUrl, messageBody);

        output.WriteLine($">>> Polling DynamoDB: aguardando até 30s para a Lambda consumir a mensagem e gravar na tabela '{Fixture.TableName}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var scan = await fixture.DynamoDB.ScanAsync(new ScanRequest
            {
                TableName = Fixture.TableName
            });

            return scan.Items.Any(item =>
                item.TryGetValue("body", out var body) &&
                body.S.Contains("order-placed", StringComparison.Ordinal));
        }, timeout: TimeSpan.FromSeconds(30));

        output.WriteLine("    Registro encontrado na tabela — Lambda processou a mensagem com sucesso");
    }
}
