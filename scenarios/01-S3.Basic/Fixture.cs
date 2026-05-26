using Amazon.S3;
using Shared;

namespace Scenarios.S3.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonS3Client S3 { get; private set; } = null!;

    protected override Task InitializeScenarioAsync()
    {
        S3 = AwsClientFactory.S3();
        return Task.CompletedTask;
    }

    protected override Task DisposeScenarioAsync()
    {
        S3.Dispose();
        return Task.CompletedTask;
    }
}
