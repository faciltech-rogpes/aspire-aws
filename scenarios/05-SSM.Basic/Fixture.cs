using Amazon.SimpleSystemsManagement;
using Shared;

namespace Scenarios.SSM.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonSimpleSystemsManagementClient SSM { get; private set; } = null!;

    protected override Task InitializeScenarioAsync()
    {
        SSM = AwsClientFactory.SSM();
        return Task.CompletedTask;
    }

    protected override Task DisposeScenarioAsync()
    {
        SSM.Dispose();
        return Task.CompletedTask;
    }
}
