using Amazon.DynamoDBv2.Model;
using Amazon.S3.Model;
using Shared;

namespace Scenarios.Pipeline.S3SqsLambdaDynamoDb;

public class FileProcessingPipelineTests(Fixture fixture) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task UploadToS3_ShouldFlowThroughSqsToLambdaAndIntoDynamoDb()
    {
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Fixture.BucketName,
            Key = "invoice-001.pdf",
            ContentBody = "invoice data"
        });

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var scan = await fixture.DynamoDB.ScanAsync(new ScanRequest
            {
                TableName = Fixture.TableName
            });

            return scan.Items.Any(item =>
                item.TryGetValue("body", out var body) &&
                body.S.Contains("invoice-001.pdf", StringComparison.Ordinal));
        }, timeout: TimeSpan.FromSeconds(120));
    }
}
