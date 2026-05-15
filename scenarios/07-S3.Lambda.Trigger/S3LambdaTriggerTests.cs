using Amazon.DynamoDBv2.Model;
using Amazon.S3.Model;
using Shared;

namespace Scenarios.S3.LambdaTrigger;

public class S3LambdaTriggerTests(Fixture fixture) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PutObject_ShouldTriggerLambda_AndPersistToDynamoDb()
    {
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Fixture.BucketName,
            Key = "report.pdf",
            ContentBody = "pdf-content"
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.DynamoDB.GetItemAsync(
                Fixture.TableName,
                new Dictionary<string, AttributeValue> { ["key"] = new() { S = "report.pdf" } });

            return response.Item.ContainsKey("key");
        }, timeout: TimeSpan.FromSeconds(90));

        var item = await fixture.DynamoDB.GetItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["key"] = new() { S = "report.pdf" } });

        Assert.Equal("uploads", item.Item["bucket"].S);
        Assert.Equal("processed", item.Item["status"].S);
    }
}
