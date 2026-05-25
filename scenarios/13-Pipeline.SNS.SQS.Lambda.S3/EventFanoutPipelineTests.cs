using Amazon.S3.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.Pipeline.SnsSqsLambdaS3;

public class EventFanoutPipelineTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task PublishToSns_ShouldFlowToLambdaAndWriteResultToS3()
    {
        var eventPayload = """{"type":"user-signup","userId":"u-42"}""";
        output.WriteLine($">>> SNS.Publish: publicando evento no tópico '{fixture.TopicArn}'");
        output.WriteLine($"    Payload: {eventPayload}");
        output.WriteLine("    Pipeline: SNS publica → SQS recebe (fanout) → Lambda consome → grava resultado no S3");
        await fixture.SNS.PublishAsync(fixture.TopicArn, eventPayload);

        output.WriteLine($">>> Polling S3: aguardando até 45s pela Lambda gravar resultado no bucket '{Fixture.BucketName}' sob o prefixo 'results/'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Fixture.BucketName,
                Prefix = "results/"
            });

            return response.S3Objects.Any();
        }, timeout: TimeSpan.FromSeconds(120));

        output.WriteLine(">>> S3.ListObjectsV2: listando objetos gravados pela Lambda em 'results/'");
        var objects = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = Fixture.BucketName,
            Prefix = "results/"
        });
        output.WriteLine($"    Objetos encontrados: {string.Join(", ", objects.S3Objects.Select(o => o.Key))}");

        Assert.NotEmpty(objects.S3Objects);
    }
}
