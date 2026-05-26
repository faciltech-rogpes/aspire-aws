using Amazon.SQS;
using Shared;

namespace Scenarios.SQS.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonSQSClient SQS { get; private set; } = null!;

    protected override Task InitializeScenarioAsync()
    {
        SQS = AwsClientFactory.SQS();
        return Task.CompletedTask;
    }

    protected override Task DisposeScenarioAsync()
    {
        SQS.Dispose();
        return Task.CompletedTask;
    }
}
