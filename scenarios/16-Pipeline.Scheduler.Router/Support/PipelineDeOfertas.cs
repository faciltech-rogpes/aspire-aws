using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.SQS.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.Pipeline.Scheduler.Router;

public class PipelineDeOfertas(Fixture fixture, ITestOutputHelper output)
{
    public async Task SubmeterOfertaAsync(OfertaDeCredito oferta)
    {
        var config = new AmazonLambdaConfig
        {
            ServiceURL = LocalStackFixture.Endpoint,
            AuthenticationRegion = "us-east-1",
            UseHttp = true,
            Timeout = TimeSpan.FromSeconds(150)
        };
        using var lambda = new AmazonLambdaClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"), config);

        var payload = JsonSerializer.Serialize(new
        {
            id = oferta.Id,
            segmento = oferta.Segmento,
            taxa = oferta.Taxa,
            valor = oferta.Valor
        });

        var resposta = await lambda.InvokeAsync(new InvokeRequest
        {
            FunctionName = Fixture.NomeLambdaProdutora,
            Payload = payload
        });

        output.WriteLine($"    Lambda invocada: status={resposta.StatusCode} | oferta={oferta.Id}");

        if (!string.IsNullOrEmpty(resposta.FunctionError))
        {
            using var reader = new StreamReader(resposta.Payload);
            var errorPayload = await reader.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Lambda '{Fixture.NomeLambdaProdutora}' retornou erro: {resposta.FunctionError} — {errorPayload}");
        }
    }

    public async Task VerificarRoteamentoAsync(string ofertaId, string urlFila)
    {
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var mensagens = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = urlFila,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 1
            });

            return mensagens.Messages.Any(m =>
            {
                var oferta = JsonSerializer.Deserialize<JsonElement>(m.Body);
                return oferta.TryGetProperty("id", out var id) && id.GetString() == ofertaId;
            });
        }, timeout: TimeSpan.FromSeconds(120));
    }

    public async Task VerificarAusenciaDeRoteamentoAsync(string ofertaId, string urlFila)
    {
        await PollingHelper.AssertNeverAsync(async () =>
        {
            var mensagens = await fixture.SQS.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = urlFila,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 1
            });

            return mensagens.Messages.Any(m =>
            {
                var oferta = JsonSerializer.Deserialize<JsonElement>(m.Body);
                return oferta.TryGetProperty("id", out var id) && id.GetString() == ofertaId;
            });
        }, duration: TimeSpan.FromSeconds(5));
    }
}
