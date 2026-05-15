using Amazon.S3.Model;
using Shared;

namespace Scenarios.Pipeline.SnsSqsLambdaS3;

public class EventFanoutPipelineTests(Fixture fixture) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PublishToSns_ShouldFlowToLambdaAndWriteResultToS3()
    {
        await fixture.SNS.PublishAsync(fixture.TopicArn, """{"type":"user-signup","userId":"u-42"}""");

        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Fixture.BucketName,
                Prefix = "results/"
            });

            return response.S3Objects.Any();
        }, timeout: TimeSpan.FromSeconds(120));

        var objects = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = Fixture.BucketName,
            Prefix = "results/"
        });

        Assert.NotEmpty(objects.S3Objects);
    }
}
