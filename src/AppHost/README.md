# AppHost

## Responsabilidade

Declara e configura os containers de infraestrutura para o ambiente de testes: o container LocalStack (simulador AWS) e o container PostgreSQL auxiliar. É o ponto de entrada do .NET Aspire — o `DistributedApplicationTestingBuilder` dos cenários referencia este projeto para iniciar a infraestrutura programaticamente dentro do xUnit.

Não contém lógica de testes nem de cenários. Sua única responsabilidade é descrever **o que sobe** e **com quais configurações**.

## Estrutura

| Arquivo | Descrição |
|---------|-----------|
| `Program.cs` | Entry point Aspire: declara containers LocalStack e PostgreSQL com variáveis de ambiente, portas e bind mounts |
| `AppHost.csproj` | Projeto SDK `Aspire.AppHost`; referenciado pelos cenários via `Projects.AppHost` |

## Como usar

O AppHost **não é invocado diretamente** pelo desenvolvedor. É iniciado programaticamente pela `LocalStackFixture` base:

```csharp
// src/Shared/LocalStackFixture.cs
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.AppHost>()
    .ConfigureAwait(false);

_app = await appHost.BuildAsync().ConfigureAwait(false);
await _app.StartAsync().ConfigureAwait(false);
```

O container LocalStack expõe todos os serviços AWS na porta `4566`:

```csharp
// src/AppHost/Program.cs
builder
    .AddContainer("localstack", "localstack/localstack", "3.8")
    .WithEnvironment("SERVICES",
        "s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,scheduler,stepfunctions,rds,ecs")
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "gateway", isProxied: false);
```

O modo `AWS_TARGET=aws` desativa o AppHost — a `LocalStackFixture` detecta a variável e pula a inicialização do Aspire, conectando diretamente à AWS real.

## Decisões relevantes

- [ADR-002](../../docs/architecture/adrs/ADR-002-orquestracao-ambiente-testes.md) — por que Aspire em vez de docker-compose
- [ADR-003](../../docs/architecture/adrs/ADR-003-simulacao-servicos-aws.md) — escolha do LocalStack Community 3.8 e dual-mode
