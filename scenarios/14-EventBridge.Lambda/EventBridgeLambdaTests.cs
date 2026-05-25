using Amazon.DynamoDBv2.Model;
using Amazon.EventBridge.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.EventBridge.Lambda;

public class EventBridgeLambdaTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutEvent_ShouldTriggerLambda_AndPersistToDynamoDb()
    {
        output.WriteLine($">>> EventBridge.PutEvents: publicando evento no barramento '{Fixture.BusName}'");
        output.WriteLine("    Source='myapp', DetailType='OrderPlaced' — a regra do EventBridge filtra por source e dispara a Lambda");
        var response = await fixture.EventBridge.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = Fixture.BusName,
                    Source = "myapp",
                    DetailType = "OrderPlaced",
                    Detail = """{"orderId":"ord-99","amount":150}"""
                }
            ]
        });
        output.WriteLine($"    FailedEntryCount: {response.FailedEntryCount}");

        Assert.Equal(0, response.FailedEntryCount);

        output.WriteLine($">>> Polling DynamoDB: aguardando até 30s para a Lambda processar o evento e gravar na tabela '{Fixture.TableName}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var scan = await fixture.DynamoDB.ScanAsync(new ScanRequest
            {
                TableName = Fixture.TableName
            });

            return scan.Items.Any(item =>
                item.TryGetValue("detail_type", out var detailType) &&
                detailType.S == "OrderPlaced");
        }, timeout: TimeSpan.FromSeconds(90));

        output.WriteLine("    Registro 'OrderPlaced' encontrado na tabela — Lambda processou o evento com sucesso");
    }

    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutEvent_WithNonMatchingSource_ShouldNotTriggerLambda()
    {
        output.WriteLine($">>> EventBridge.PutEvents: publicando evento com Source='other-app' no barramento '{Fixture.BusName}'");
        output.WriteLine("    A regra do EventBridge filtra apenas source='myapp' — este evento NÃO deve acionar a Lambda");
        var response = await fixture.EventBridge.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = Fixture.BusName,
                    Source = "other-app",
                    DetailType = "SomeEvent",
                    Detail = """{"ignored":true}"""
                }
            ]
        });
        output.WriteLine($"    FailedEntryCount: {response.FailedEntryCount}");

        Assert.Equal(0, response.FailedEntryCount);

        output.WriteLine($">>> AssertNever DynamoDB: verificando durante 5s que nenhum registro de 'other-app' aparece na tabela '{Fixture.TableName}'");
        await PollingHelper.AssertNeverAsync(async () =>
        {
            var scan = await fixture.DynamoDB.ScanAsync(new ScanRequest
            {
                TableName = Fixture.TableName
            });

            return scan.Items.Any(item =>
                item.TryGetValue("source", out var source) &&
                source.S == "other-app");
        }, duration: TimeSpan.FromSeconds(5));

        output.WriteLine("    Nenhum registro encontrado — filtro da regra EventBridge funcionou corretamente");
    }
}
