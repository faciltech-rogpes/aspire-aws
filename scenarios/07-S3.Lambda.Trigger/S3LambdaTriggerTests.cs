using Amazon.DynamoDBv2.Model;
using Amazon.S3.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.S3.LambdaTrigger;

public class S3LambdaTriggerTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutObject_ShouldTriggerLambda_AndPersistToDynamoDb()
    {
        output.WriteLine($">>> S3.PutObject: fazendo upload de 'report.pdf' no bucket '{Fixture.BucketName}'");
        output.WriteLine("    O bucket tem uma notificação de evento configurada que dispara a Lambda automaticamente");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Fixture.BucketName,
            Key = "report.pdf",
            ContentBody = "pdf-content"
        });

        output.WriteLine($">>> Polling DynamoDB: aguardando até 30s para a Lambda processar o evento e gravar na tabela '{Fixture.TableName}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.DynamoDB.GetItemAsync(
                Fixture.TableName,
                new Dictionary<string, AttributeValue> { ["key"] = new() { S = "report.pdf" } });

            return response.Item.ContainsKey("key");
        }, timeout: TimeSpan.FromSeconds(90));

        output.WriteLine(">>> DynamoDB.GetItem: lendo o registro gravado pela Lambda");
        var item = await fixture.DynamoDB.GetItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["key"] = new() { S = "report.pdf" } });
        output.WriteLine($"    key='{item.Item["key"].S}', bucket='{item.Item["bucket"].S}', status='{item.Item["status"].S}'");

        Assert.Equal("uploads", item.Item["bucket"].S);
        Assert.Equal("processed", item.Item["status"].S);
    }
}
