# aspire-aws -- Arquitetura e Conceitos

Ambiente de testes local para integrações AWS usando **.NET Aspire** como orquestrador e **LocalStack** como simulador de serviços AWS. Os testes são escritos em **C# (xUnit)** e os Lambda handlers em **Python 3.12**.

## Stack

| Componente | Versao | Papel |
|---|---|---|
| .NET SDK | 10.0 | Runtime e build |
| .NET Aspire | 9.5 | Orquestracao do container LocalStack |
| xUnit | 2.9 / runner 3.1 | Framework de testes |
| LocalStack | 3.8 (Community) | Simulador AWS local (S3, SQS, SNS, DynamoDB, Lambda, SSM, SecretsManager, EventBridge, StepFunctions) |
| Python | 3.12 + boto3 | Lambda handlers |
| Docker | -- | Container runtime para LocalStack (e para Lambda internamente) |

---

## Estrutura de diretorios

```
aspire-aws/
  aspire-aws.sln
  Directory.Build.props          # Build sequencial, copia xunit.runner.json
  Directory.Build.targets        # RunSettingsFilePath (avaliado apos pacotes NuGet)
  test.runsettings               # MaxCpuCount=1 (1 assembly por vez)
  xunit.runner.json              # parallelizeTestCollections=false
  global.json                    # .NET SDK 10.0

  src/
    AppHost/
      Program.cs                 # Aspire AppHost: configura container LocalStack
    Shared/
      LocalStackFixture.cs       # IAsyncLifetime: ciclo de vida do Aspire + lock de porta
      AwsClientFactory.cs        # Factory de clientes AWS apontando para localhost:4566
      LambdaDeployer.cs          # Empacota e deploya Python Lambdas no LocalStack
      PollingHelper.cs           # WaitUntilAsync / AssertNeverAsync (polling com timeout)
      EnvironmentLimitations.cs  # Skip condicional para macOS ARM64
    lambdas/
      s3_processor/handler.py
      sqs_consumer/handler.py
      dynamodb_writer/handler.py
      fanout_processor/handler.py
      eventbridge_handler/handler.py
      stepfunctions_task/handler.py
      produtor_de_ofertas/handler.py
      roteador_de_ofertas/handler.py
      eco_consignado/handler.py

  scenarios/
    01-S3.Basic/                 # Um projeto xUnit por cenario
    02-SQS.Basic/
    ...
    15-StepFunctions.Orchestration/
    16-Pipeline.Scheduler.Router/
```

---

## Como funciona

### Fluxo de execucao de um teste

```
dotnet test aspire-aws.sln
  |
  v
[test runner: 1 assembly por vez (MaxCpuCount=1)]
  |
  v
Fixture.InitializeAsync()
  |-- Adquire file lock exclusivo (aspire-aws-localstack-4566.lock)
  |-- Cria DistributedApplication via Aspire Testing Builder
  |-- Inicia AppHost -> sobe container LocalStack na porta 4566
  |-- Aguarda /healthcheck do LocalStack (polling, timeout 120s)
  |-- Executa InitializeScenarioAsync() do cenario
  |     |-- Cria recursos AWS (buckets, filas, tabelas, lambdas...)
  |     |-- Configura event sources, notificacoes, policies
  v
[Testes xUnit rodam contra LocalStack via AWS SDK]
  |
  v
Fixture.DisposeAsync()
  |-- DisposeScenarioAsync() (cleanup de clients)
  |-- Para o AppHost (destroi container)
  |-- Libera file lock
```

### Execucao sequencial: por que e como

Todos os cenarios compartilham a porta 4566. Para evitar conflitos, a execucao dos assemblies e serializada por dois mecanismos complementares:

1. **`test.runsettings`** com `MaxCpuCount=1` -- o test runner do .NET executa um assembly por vez
2. **File lock** em `LocalStackFixture` -- garantia adicional via lock exclusivo no filesystem

O `RunSettingsFilePath` e definido em `Directory.Build.targets` (nao `.props`) porque a propriedade `IsTestProject` so e definida pelos pacotes NuGet, que sao avaliados apos o `.props`.

### Aspire como orquestrador

O `AppHost/Program.cs` configura um unico container LocalStack com todos os servicos habilitados:

```csharp
builder
    .AddContainer("localstack", "localstack/localstack", "3.8")
    .WithEnvironment("SERVICES", "s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,stepfunctions")
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, ...);
```

O `DistributedApplicationTestingBuilder` do Aspire permite iniciar o AppHost programaticamente dentro de testes, sem precisar de `docker-compose` ou scripts externos.

### AwsClientFactory

Todos os clientes AWS sao criados via factory estatica que aponta para `http://localhost:4566` com credenciais dummy (`test/test`):

```csharp
public static AmazonS3Client S3() => new(Credentials, Configure(new AmazonS3Config
{
    ForcePathStyle = true  // necessario para LocalStack
}, "http://localhost:4566"));
```

Nunca construa `AmazonXxxClient` diretamente nos testes -- sempre use a factory.

### Lambda handlers (Python)

Cada handler segue o mesmo padrao:

```python
def lambda_handler(event, context):
    endpoint = os.environ.get("AWS_ENDPOINT_URL")  # LocalStack injeta automaticamente
    dynamodb = boto3.resource("dynamodb", endpoint_url=endpoint)
    table = dynamodb.Table(os.environ["DYNAMODB_TABLE"])
    # ... processa evento e persiste resultado
```

O `LambdaDeployer` no C# empacota o diretorio Python em ZIP, faz deploy via `CreateFunctionAsync`, e aguarda o estado `Active`.

### Polling vs delays fixos

Testes nunca usam `Task.Delay` para aguardar efeitos assincronos. Em vez disso, usam `PollingHelper`:

```csharp
// Espera ate a condicao ser verdadeira (com timeout)
await PollingHelper.WaitUntilAsync(async () =>
{
    var item = await dynamodb.GetItemAsync(...);
    return item.Item.ContainsKey("key");
}, timeout: TimeSpan.FromSeconds(30));

// Verifica que algo NUNCA acontece (teste negativo)
await PollingHelper.AssertNeverAsync(async () =>
{
    var items = await dynamodb.ScanAsync(...);
    return items.Items.Any();
}, duration: TimeSpan.FromSeconds(5));
```

---

## Catalogo de cenarios

### Basicos (01-06) -- servico unico, sem Lambda

| # | Cenario | Servicos | Testes | O que demonstra |
|---|---------|----------|--------|-----------------|
| 01 | S3.Basic | S3 | 5 | CRUD de buckets e objetos, presigned URLs |
| 02 | SQS.Basic | SQS | 4 | Send/receive, delete, dead-letter queue |
| 03 | DynamoDB.Basic | DynamoDB | 4 | Put/get, scan, query, delete |
| 04 | SNS.Basic | SNS, SQS | 3 | Criar topico, publicar com subscriber SQS, listar |
| 05 | SSM.Basic | SSM | 3 | Parametros String e SecureString, busca por path |
| 06 | SecretsManager.Basic | SecretsManager | 3 | Criar, atualizar, deletar segredos |

### Integracao entre servicos (09-10) -- sem Lambda

| # | Cenario | Servicos | Testes | O que demonstra |
|---|---------|----------|--------|-----------------|
| 09 | SNS.SQS.Fanout | SNS, SQS | 1 | Uma mensagem SNS entregue a N filas SQS (fan-out) |
| 10 | S3.SQS.Notification | S3, SQS | 1 | Upload no S3 gera notificacao na fila SQS |

### Lambda triggers (07-08, 11, 14) -- skipped no macOS ARM64

| # | Cenario | Fluxo | Testes | O que demonstra |
|---|---------|-------|--------|-----------------|
| 07 | S3.Lambda.Trigger | S3 -> Lambda -> DynamoDB | 1 | Upload no S3 dispara Lambda que persiste no DynamoDB |
| 08 | SQS.Lambda.Consumer | SQS -> Lambda -> DynamoDB | 1 | Mensagem na fila dispara Lambda via event source mapping |
| 11 | DynamoDB.Lambda | invocacao direta -> DynamoDB | 1 | Lambda invocada diretamente escreve no DynamoDB |
| 14 | EventBridge.Lambda | EventBridge -> Lambda -> DynamoDB | 2 | Regra EventBridge com filtro de source dispara Lambda; inclui teste negativo |

### Pipelines multi-servico (12-13) -- skipped no macOS ARM64

| # | Cenario | Fluxo | Testes | O que demonstra |
|---|---------|-------|--------|-----------------|
| 12 | Pipeline.S3.SQS.Lambda.DynamoDB | S3 -> SQS -> Lambda -> DynamoDB | 1 | Pipeline completo: upload dispara notificacao, fila alimenta Lambda, resultado no DynamoDB |
| 13 | Pipeline.SNS.SQS.Lambda.S3 | SNS -> SQS -> Lambda -> S3 | 1 | Publicacao SNS -> fila -> Lambda -> resultado salvo no S3 |

### Orquestracao (15) -- skipped (requer LocalStack Pro)

| # | Cenario | Servicos | Testes | O que demonstra |
|---|---------|----------|--------|-----------------|
| 15 | StepFunctions.Orchestration | StepFunctions, Lambda, DynamoDB | 2 | State machine com Task + Choice; start execution e list executions |

---

## Conceitos AWS demonstrados

### S3 (Simple Storage Service)

Armazenamento de objetos. Cada objeto vive dentro de um **bucket** e e identificado por uma **key** (caminho). O cenario 01 demonstra operacoes CRUD basicas. O `ForcePathStyle = true` na config do client e necessario para LocalStack, que nao suporta virtual-hosted-style (`bucket.s3.amazonaws.com`).

**Presigned URLs** (cenario 01) permitem gerar URLs temporarias com acesso a objetos privados, sem expor credenciais.

**Bucket Notifications** (cenarios 10, 12) permitem que o S3 dispare eventos em outros servicos (SQS, Lambda) quando objetos sao criados ou deletados.

### SQS (Simple Queue Service)

Fila de mensagens totalmente gerenciada. Suporta **dead-letter queues** (DLQ) para mensagens que falham repetidamente. O cenario 02 demonstra o fluxo send -> receive -> delete e configuracao de DLQ via `RedrivePolicy`.

**Event source mappings** (cenarios 08, 12, 13) conectam filas SQS a funcoes Lambda -- o runtime AWS faz o polling automaticamente e invoca a Lambda com batches de mensagens.

### SNS (Simple Notification Service)

Servico pub/sub. Um **topico** pode ter multiplos **subscribers** (SQS, Lambda, HTTP, email). O cenario 04 demonstra a integracao SNS -> SQS, e o cenario 09 demonstra **fan-out**: uma unica publicacao entregue a multiplas filas.

A comunicacao SNS -> SQS requer uma **IAM policy** na fila permitindo que o SNS envie mensagens:

```json
{
  "Effect": "Allow",
  "Principal": { "Service": "sns.amazonaws.com" },
  "Action": "sqs:SendMessage",
  "Condition": { "ArnEquals": { "aws:SourceArn": "<topic-arn>" } }
}
```

### DynamoDB

Banco NoSQL com modelo de **partition key** (e opcionalmente **sort key**). O cenario 03 demonstra operacoes basicas. Tabelas precisam ser aguardadas ate o status `ACTIVE` antes de usar -- os fixtures usam `PollingHelper` para isso.

### Lambda

Funcoes serverless executadas em resposta a eventos. Neste projeto, os handlers sao Python e usam `boto3` para interagir com outros servicos AWS. O `LambdaDeployer` resolve o caminho do codigo Python, cria um ZIP, e faz deploy via API do Lambda.

No LocalStack, Lambda cria containers Docker internos para executar o codigo. No macOS ARM64, esse mecanismo e instavel no LocalStack 3.8, por isso os cenarios Lambda sao skipped nesse ambiente.

### SSM Parameter Store e Secrets Manager

**SSM** (cenario 05) armazena configuracao como pares chave-valor, com suporte a **SecureString** (encriptado). **Secrets Manager** (cenario 06) e voltado para segredos com ciclo de vida (rotacao, versionamento, delete com recovery).

### EventBridge

Barramento de eventos com regras de filtragem. O cenario 14 cria um **custom bus**, uma **rule** com pattern `{"source":["myapp"]}`, e demonstra que apenas eventos com source correto disparam a Lambda (teste negativo com `AssertNeverAsync`).

### Step Functions

Orquestracao de workflows como maquinas de estado. O cenario 15 define uma state machine com estados **Task** (invoca Lambda), **Choice** (branch condicional), **Succeed** e **Fail**. Requer LocalStack Pro para execucao confiavel.

---

## Padrao de fixture por cenario

Cada cenario segue o mesmo padrao:

```
Fixture : LocalStackFixture
  |-- InitializeScenarioAsync()    // cria recursos AWS especificos
  |-- Properties publicas          // expoe clients e ARNs para os testes
  |-- DisposeScenarioAsync()       // dispoe clients (recursos morrem com o container)
```

Os testes recebem a fixture via `IClassFixture<Fixture>` do xUnit:

```csharp
public class S3BasicTests(Fixture fixture) : IClassFixture<Fixture>
{
    [Fact]
    public async Task PutAndGetObject_ShouldRoundTrip()
    {
        // usa fixture.S3 para interagir com LocalStack
    }
}
```

O `LocalStackFixture` base cuida de:
- Adquirir lock exclusivo na porta 4566
- Iniciar o Aspire AppHost
- Aguardar o healthcheck do LocalStack
- Chamar `InitializeScenarioAsync()` do cenario
- Cleanup reverso no `DisposeAsync()`

---

## Limitacoes conhecidas

| Limitacao | Impacto | Workaround |
|-----------|---------|------------|
| Lambda no LocalStack 3.8 + macOS ARM64 | Cenarios 07, 08, 11-14 skipped | Rodar em Linux/CI ou usar LocalStack Pro |
| Step Functions na edicao Community | Cenario 15 skipped | LocalStack Pro |
| Porta fixa 4566 | Um unico LocalStack por vez | File lock + MaxCpuCount=1 |
| Docker obrigatorio | Sem Docker, nada roda | -- |

---

## Como rodar

```bash
# Suite completa (sequencial, ~3min)
dotnet test aspire-aws.sln

# Cenario especifico
dotnet test scenarios/01-S3.Basic/

# Com output detalhado
dotnet test aspire-aws.sln --logger "console;verbosity=detailed"
```

Docker precisa estar rodando. O LocalStack sobe e desce automaticamente via Aspire.
