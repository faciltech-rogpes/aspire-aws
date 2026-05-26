using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Shared;

namespace Scenarios.SNS.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonSimpleNotificationServiceClient SNS { get; private set; } = null!;
    public AmazonSQSClient SQS { get; private set; } = null!;

    protected override Task InitializeScenarioAsync()
    {
        SNS = AwsClientFactory.SNS();
        SQS = AwsClientFactory.SQS();
        return Task.CompletedTask;
    }

    protected override Task DisposeScenarioAsync()
    {
        SNS.Dispose();
        SQS.Dispose();
        return Task.CompletedTask;
    }
}
