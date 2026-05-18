using Amazon.SimpleSystemsManagement.Model;
using Xunit.Abstractions;

namespace Scenarios.SSM.Basic;

public class SsmBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task PutAndGetParameter_ShouldRoundTrip()
    {
        output.WriteLine(">>> SSM.PutParameter: gravando parâmetro String '/app/config/db-host' com valor 'localhost'");
        await fixture.SSM.PutParameterAsync(new PutParameterRequest
        {
            Name = "/app/config/db-host",
            Value = "localhost",
            Type = "String"
        });

        output.WriteLine(">>> SSM.GetParameter: lendo parâmetro pelo nome '/app/config/db-host'");
        var response = await fixture.SSM.GetParameterAsync(new GetParameterRequest
        {
            Name = "/app/config/db-host"
        });
        output.WriteLine($"    Valor: '{response.Parameter.Value}', tipo: {response.Parameter.Type}");

        Assert.Equal("localhost", response.Parameter.Value);
    }

    [Fact]
    public async Task PutSecureStringParameter_ShouldBeRetrievable()
    {
        output.WriteLine(">>> SSM.PutParameter: gravando parâmetro SecureString '/app/secrets/api-key' (valor cifrado no armazenamento)");
        await fixture.SSM.PutParameterAsync(new PutParameterRequest
        {
            Name = "/app/secrets/api-key",
            Value = "super-secret",
            Type = "SecureString"
        });

        output.WriteLine(">>> SSM.GetParameter: lendo parâmetro com WithDecryption=true para obter o valor em plaintext");
        var response = await fixture.SSM.GetParameterAsync(new GetParameterRequest
        {
            Name = "/app/secrets/api-key",
            WithDecryption = true
        });
        output.WriteLine($"    Valor decifrado: '{response.Parameter.Value}'");

        Assert.Equal("super-secret", response.Parameter.Value);
    }

    [Fact]
    public async Task GetParametersByPath_ShouldReturnAllUnderPrefix()
    {
        output.WriteLine(">>> SSM.PutParameter: gravando '/myapp/env/host'");
        await fixture.SSM.PutParameterAsync(new PutParameterRequest
        {
            Name = "/myapp/env/host",
            Value = "host-val",
            Type = "String"
        });

        output.WriteLine(">>> SSM.PutParameter: gravando '/myapp/env/port'");
        await fixture.SSM.PutParameterAsync(new PutParameterRequest
        {
            Name = "/myapp/env/port",
            Value = "5432",
            Type = "String"
        });

        output.WriteLine(">>> SSM.GetParametersByPath: buscando todos os parâmetros sob o prefixo '/myapp/env' (Recursive=true)");
        var response = await fixture.SSM.GetParametersByPathAsync(new GetParametersByPathRequest
        {
            Path = "/myapp/env",
            Recursive = true
        });
        output.WriteLine($"    Parâmetros encontrados: {string.Join(", ", response.Parameters.Select(p => p.Name))}");

        Assert.Equal(2, response.Parameters.Count);
    }
}
