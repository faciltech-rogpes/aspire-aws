using Amazon.DynamoDBv2.Model;
using Xunit.Abstractions;

namespace Scenarios.Pipeline.Scheduler.Router;

public class SemeadorDeRegras(Fixture fixture, ITestOutputHelper output)
{
    public async Task SemeiarAsync(RegraDeRoteamento regra)
    {
        await fixture.DynamoDB.PutItemAsync(new PutItemRequest
        {
            TableName = Fixture.NomeTabelaRegras,
            Item = new Dictionary<string, AttributeValue>
            {
                ["segmento"]     = new AttributeValue { S = regra.Segmento },
                ["fila_destino"] = new AttributeValue { S = regra.FilaDestino }
            }
        });
        output.WriteLine($"    Regra semeada: segmento={regra.Segmento} → {regra.FilaDestino}");
    }

    public async Task<List<RegraDeRoteamento>> ObterRegrasAsync()
    {
        var resultado = await fixture.DynamoDB.ScanAsync(new ScanRequest
        {
            TableName = Fixture.NomeTabelaRegras
        });

        return resultado.Items
            .Select(item => new RegraDeRoteamento(
                item["segmento"].S,
                item["fila_destino"].S))
            .ToList();
    }
}
