using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Shared;
using System.Text.Json;
using Xunit.Abstractions;

namespace Scenarios.DynamoDB.Lambda;

public class DynamoDbLambdaTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task InvokeLambda_ShouldWriteToDynamoDb()
    {
        var payload = JsonSerializer.Serialize(new { id = "evt-001", type = "click" });
        output.WriteLine($">>> Lambda.Invoke: invocando função '{Fixture.FunctionName}' diretamente via SDK");
        output.WriteLine($"    Payload: {payload}");
        await fixture.Lambda.InvokeAsync(new InvokeRequest
        {
            FunctionName = Fixture.FunctionName,
            Payload = payload
        });

        output.WriteLine($">>> Polling DynamoDB: aguardando a Lambda gravar na tabela '{Fixture.ResultTable}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.DynamoDB.GetItemAsync(
                Fixture.ResultTable,
                new Dictionary<string, AttributeValue> { ["id"] = new() { S = "evt-001" } });

            return response.Item.ContainsKey("id");
        });

        output.WriteLine(">>> DynamoDB.GetItem: lendo o registro gravado pela Lambda");
        var item = await fixture.DynamoDB.GetItemAsync(
            Fixture.ResultTable,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "evt-001" } });
        output.WriteLine($"    id='{item.Item["id"].S}'");

        Assert.Equal("evt-001", item.Item["id"].S);
    }
}
