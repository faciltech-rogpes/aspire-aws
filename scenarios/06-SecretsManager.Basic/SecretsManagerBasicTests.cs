using Amazon.SecretsManager.Model;
using Xunit.Abstractions;

namespace Scenarios.SecretsManager.Basic;

public class SecretsManagerBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task CreateAndGetSecret_ShouldRoundTrip()
    {
        output.WriteLine(">>> SecretsManager.CreateSecret: criando segredo 'myapp/db-password'");
        await fixture.SecretsManager.CreateSecretAsync(new CreateSecretRequest
        {
            Name = "myapp/db-password",
            SecretString = "p@ssw0rd"
        });

        output.WriteLine(">>> SecretsManager.GetSecretValue: recuperando valor do segredo pelo SecretId");
        var response = await fixture.SecretsManager.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = "myapp/db-password"
        });
        output.WriteLine($"    SecretString: '{response.SecretString}'");

        Assert.Equal("p@ssw0rd", response.SecretString);
    }

    [Fact]
    public async Task UpdateSecret_ShouldOverwriteValue()
    {
        output.WriteLine(">>> SecretsManager.CreateSecret: criando segredo 'myapp/api-key' com valor inicial 'old-key'");
        await fixture.SecretsManager.CreateSecretAsync(new CreateSecretRequest
        {
            Name = "myapp/api-key",
            SecretString = "old-key"
        });

        output.WriteLine(">>> SecretsManager.PutSecretValue: atualizando valor do segredo para 'new-key' (cria nova versão)");
        await fixture.SecretsManager.PutSecretValueAsync(new PutSecretValueRequest
        {
            SecretId = "myapp/api-key",
            SecretString = "new-key"
        });

        output.WriteLine(">>> SecretsManager.GetSecretValue: lendo valor atual (deve retornar a versão mais recente)");
        var response = await fixture.SecretsManager.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = "myapp/api-key"
        });
        output.WriteLine($"    SecretString: '{response.SecretString}'");

        Assert.Equal("new-key", response.SecretString);
    }

    [Fact]
    public async Task DeleteSecret_ShouldMakeItInaccessible()
    {
        output.WriteLine(">>> SecretsManager.CreateSecret: criando segredo 'myapp/to-delete'");
        await fixture.SecretsManager.CreateSecretAsync(new CreateSecretRequest
        {
            Name = "myapp/to-delete",
            SecretString = "value"
        });

        output.WriteLine(">>> SecretsManager.DeleteSecret: removendo segredo com ForceDeleteWithoutRecovery=true (sem período de recuperação de 7 dias)");
        await fixture.SecretsManager.DeleteSecretAsync(new DeleteSecretRequest
        {
            SecretId = "myapp/to-delete",
            ForceDeleteWithoutRecovery = true
        });

        output.WriteLine(">>> SecretsManager.GetSecretValue: confirmando que ResourceNotFoundException é lançada ao tentar acessar o segredo deletado");
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.SecretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = "myapp/to-delete"
            }));
        output.WriteLine("    ResourceNotFoundException recebida conforme esperado");
    }
}
