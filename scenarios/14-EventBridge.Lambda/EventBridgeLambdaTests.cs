using Amazon.DynamoDBv2.Model;
using Amazon.EventBridge.Model;
using Shared;

namespace Scenarios.EventBridge.Lambda;

public class EventBridgeLambdaTests(Fixture fixture) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutEvent_ShouldTriggerLambda_AndPersistToDynamoDb()
    {
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

        Assert.Equal(0, response.FailedEntryCount);

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
    }

    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutEvent_WithNonMatchingSource_ShouldNotTriggerLambda()
    {
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

        Assert.Equal(0, response.FailedEntryCount);

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
    }
}
