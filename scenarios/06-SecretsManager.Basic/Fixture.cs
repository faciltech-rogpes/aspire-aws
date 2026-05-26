using Amazon.SecretsManager;
using Shared;

namespace Scenarios.SecretsManager.Basic;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
    public AmazonSecretsManagerClient SecretsManager { get; private set; } = null!;

    protected override Task InitializeScenarioAsync()
    {
        SecretsManager = AwsClientFactory.SecretsManager();
        return Task.CompletedTask;
    }

    protected override Task DisposeScenarioAsync()
    {
        SecretsManager.Dispose();
        return Task.CompletedTask;
    }
}
