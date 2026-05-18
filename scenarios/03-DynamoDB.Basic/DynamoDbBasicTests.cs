using Amazon.DynamoDBv2.Model;
using Xunit.Abstractions;

namespace Scenarios.DynamoDB.Basic;

public class DynamoDbBasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task PutAndGetItem_ShouldRoundTrip()
    {
        output.WriteLine($">>> DynamoDB.PutItem: gravando item {{id='item-1', name='Alice'}} na tabela '{Fixture.TableName}'");
        await fixture.DynamoDB.PutItemAsync(Fixture.TableName, new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = "item-1" },
            ["name"] = new AttributeValue { S = "Alice" }
        });

        output.WriteLine(">>> DynamoDB.GetItem: buscando item pela chave primária id='item-1'");
        var response = await fixture.DynamoDB.GetItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "item-1" } });
        output.WriteLine($"    name: '{response.Item["name"].S}'");

        Assert.Equal("Alice", response.Item["name"].S);
    }

    [Fact]
    public async Task Scan_ShouldReturnAllItems()
    {
        output.WriteLine($">>> DynamoDB.PutItem: gravando item id='scan-1' na tabela '{Fixture.TableName}'");
        await fixture.DynamoDB.PutItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "scan-1" } });

        output.WriteLine($">>> DynamoDB.PutItem: gravando item id='scan-2' na tabela '{Fixture.TableName}'");
        await fixture.DynamoDB.PutItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "scan-2" } });

        output.WriteLine($">>> DynamoDB.Scan: varrendo todos os itens da tabela '{Fixture.TableName}' (sem filtro)");
        var response = await fixture.DynamoDB.ScanAsync(new ScanRequest
        {
            TableName = Fixture.TableName
        });
        output.WriteLine($"    Itens encontrados: {response.Count}");

        Assert.True(response.Count >= 2);
    }

    [Fact]
    public async Task DeleteItem_ShouldRemoveRecord()
    {
        output.WriteLine($">>> DynamoDB.PutItem: gravando item id='to-delete' na tabela '{Fixture.TableName}'");
        await fixture.DynamoDB.PutItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "to-delete" } });

        output.WriteLine(">>> DynamoDB.DeleteItem: removendo item pela chave id='to-delete'");
        await fixture.DynamoDB.DeleteItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "to-delete" } });

        output.WriteLine(">>> DynamoDB.GetItem: confirmando que o item foi removido (resposta deve ser vazia)");
        var response = await fixture.DynamoDB.GetItemAsync(
            Fixture.TableName,
            new Dictionary<string, AttributeValue> { ["id"] = new() { S = "to-delete" } });
        output.WriteLine($"    Campos retornados: {response.Item.Count}");

        Assert.Empty(response.Item);
    }

    [Fact]
    public async Task Query_ShouldReturnMatchingItems()
    {
        output.WriteLine($">>> DynamoDB.PutItem: gravando item {{id='query-target', status='active'}} na tabela '{Fixture.TableName}'");
        await fixture.DynamoDB.PutItemAsync(Fixture.TableName, new Dictionary<string, AttributeValue>
        {
            ["id"] = new AttributeValue { S = "query-target" },
            ["status"] = new AttributeValue { S = "active" }
        });

        output.WriteLine(">>> DynamoDB.Query: buscando itens com KeyConditionExpression 'id = :id' (usa índice primário, mais eficiente que Scan)");
        var response = await fixture.DynamoDB.QueryAsync(new QueryRequest
        {
            TableName = Fixture.TableName,
            KeyConditionExpression = "id = :id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":id"] = new() { S = "query-target" }
            }
        });
        output.WriteLine($"    Itens retornados: {response.Items.Count}, status: '{response.Items[0]["status"].S}'");

        Assert.Single(response.Items);
        Assert.Equal("active", response.Items[0]["status"].S);
    }
}
