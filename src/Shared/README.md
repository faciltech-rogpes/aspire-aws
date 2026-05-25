# Shared

## Responsabilidade

Núcleo de infraestrutura de testes: gerencia o ciclo de vida do LocalStack, fornece factory centralizada de clientes AWS, implementa helpers de polling para asserções assíncronas, e faz deploy de Lambda handlers Python. Não contém lógica de negócio nem asserções de cenários.

## Estrutura

| Arquivo | Descrição |
|---------|-----------|
| `LocalStackFixture.cs` | Classe base `IAsyncLifetime`: adquire file lock, inicia o Aspire AppHost, aguarda healthcheck do LocalStack, delega `InitializeScenarioAsync()` ao cenário |
| `AwsClientFactory.cs` | Factory estática: cria clientes AWS SDK configurados para LocalStack ou AWS real (via `AWS_TARGET`) |
| `LambdaDeployer.cs` | Empacota handlers Python em ZIP e faz deploy via `CreateFunctionAsync`; aguarda estado `Active` |
| `PollingHelper.cs` | `WaitUntilAsync` — aguarda condição com timeout; `AssertNeverAsync` — verifica que condição nunca se torna verdadeira |
| `EnvironmentLimitations.cs` | `LambdaSkip` — detecta macOS ARM64 + LocalStack Community para skip automático dos cenários Lambda |

## Como usar

Cada cenário herda de `LocalStackFixture` e sobrescreve `InitializeScenarioAsync`:

```csharp
// scenarios/XX-Foo/Fixture.cs
public class Fixture : LocalStackFixture
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
```

Para cenários Lambda, usar `LambdaDeployer` dentro de `InitializeScenarioAsync`:

```csharp
Lambda = AwsClientFactory.Lambda();
await LambdaDeployer.DeployAsync(Lambda, "s3_processor");
```

Para asserções assíncronas:

```csharp
// Aguardar efeito
await PollingHelper.WaitUntilAsync(async () =>
{
    var item = await DynamoDB.GetItemAsync(tableName, key);
    return item.Item.ContainsKey("result");
}, timeout: TimeSpan.FromSeconds(30));

// Verificar que algo nunca acontece
await PollingHelper.AssertNeverAsync(async () =>
{
    var items = await SQS.ReceiveMessageAsync(queueUrl);
    return items.Messages.Any();
}, duration: TimeSpan.FromSeconds(5));
```

## Decisões relevantes

- [ADR-002](../../docs/architecture/adrs/ADR-002-orquestracao-ambiente-testes.md) — por que Aspire em vez de docker-compose para o ciclo de vida
- [ADR-004](../../docs/architecture/adrs/ADR-004-isolamento-cenarios-teste.md) — file lock e execução sequencial implementados aqui
- [ADR-005](../../docs/architecture/adrs/ADR-005-assercoes-assincronas.md) — `PollingHelper` como substituto de `Task.Delay`
- [ADR-006](../../docs/architecture/adrs/ADR-006-abstracao-clientes-aws.md) — `AwsClientFactory` centraliza configuração dual-mode
