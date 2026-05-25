# Design: Cenários 17-RDS.Basic e 18-ECS.RunTask

**Data:** 2026-05-23  
**Projeto:** aspire-aws  
**Status:** Aprovado

---

## Contexto

O projeto aspire-aws é uma base de exemplos didáticos de serviços AWS com LocalStack + .NET Aspire. Os cenários 01–16 cobrem S3, SQS, SNS, DynamoDB, Lambda, SSM, SecretsManager, EventBridge, Scheduler e StepFunctions. Este design adiciona dois novos cenários:

- **17-RDS.Basic**: provisionar instância RDS via SDK + CRUD real com PostgreSQL
- **18-ECS.RunTask**: ciclo de vida ECS via SDK + worker real que processa pedidos via SQS e persiste no PostgreSQL

### Decisões de design

- **Abordagem híbrida (dual-plano)**: LocalStack para a camada de controle AWS (criar/descrever/modificar recursos), container real Aspire para a camada de dados. Escolhida por ser a mais rica didaticamente.
- **Progressão conectada**: cenário 17 ensina RDS isolado; cenário 18 usa o mesmo padrão PostgreSQL como destino do worker ECS, construindo sobre o vocabulário do 17.
- **ECS worker sempre-ativo**: worker Python é um container Aspire de vida longa que poll SQS. Didaticamente honesto — `RunTask` demonstra a API de controle, `SendMessage` aciona a execução real. A separação é explicada nos `output.WriteLine` dos testes.
- **LocalStack Community**: ECS API (CreateCluster, RegisterTaskDefinition, RunTask, DescribeTasks) funciona; execução real de containers ECS não é suportada. Documentado no código.

---

## Arquitetura

### Diagrama de componentes

```
AppHost (Aspire)
├── LocalStack 3.8 Community
│     SERVICES: ...,rds,ecs
│     Plano de controle AWS para ambos os cenários
├── postgres:16  →  host:5433
│     Banco de dados real para cenários 17 e 18
└── ecs-worker (Dockerfile)           [apenas cenário 18]
      Poll SQS → INSERT PostgreSQL
      Conecta via host.docker.internal:{4566,5433}
```

### Fluxo — Cenário 17

```
Test → RDS.CreateDBInstance (LocalStack) → DescribeDBInstances → status "available"
Test → Npgsql (localhost:5433) → CREATE TABLE produtos
Test → INSERT / SELECT / UPDATE / DELETE / ROLLBACK
```

### Fluxo — Cenário 18

```
AppHost init → worker container sobe → poll SQS fila-pedidos
Fixture → ECS.CreateCluster → RegisterTaskDefinition → SQS.CreateQueue

Test → ECS.RunTask com overrides (LocalStack registra task ARN)
     → SQS.SendMessage (aciona worker real)
     → PollingHelper → SELECT PostgreSQL até pedido aparecer
     → ECS.DescribeTasks (valida metadados do plano de controle)
```

---

## Infraestrutura compartilhada — mudanças

### `src/AppHost/Program.cs`

Acréscimos ao bloco LocalStack:
```csharp
// Adicionar rds,ecs ao final de SERVICES
.WithEnvironment("SERVICES", "...,rds,ecs")

// Novo container PostgreSQL (fixo na porta 5433 — mesmo padrão do LocalStack em 4566)
builder.AddContainer("postgres-rds", "postgres", "16")
    .WithEnvironment("POSTGRES_USER", "test")
    .WithEnvironment("POSTGRES_PASSWORD", "test")
    .WithEnvironment("POSTGRES_DB", "testdb")
    .WithHttpEndpoint(port: 5433, targetPort: 5432, name: "tcp", isProxied: false);

// Novo container ECS worker (só sobido quando o cenário 18 rodar — ver nota abaixo)
// Caminho relativo ao diretório do AppHost (src/AppHost/ → src/tasks/)
builder.AddDockerfile("ecs-worker", "../tasks/pedido_processor")
    .WithEnvironment("AWS_ENDPOINT_URL", "http://host.docker.internal:4566")
    .WithEnvironment("DATABASE_URL", "postgresql://test:test@host.docker.internal:5433/testdb")
    .WithEnvironment("FILA_PEDIDOS_URL",
        "http://host.docker.internal:4566/000000000000/fila-pedidos");
```

> **Nota sobre o worker**: o container `ecs-worker` sobe junto com o AppHost em todos os testes, mas só tem impacto no cenário 18 (onde a fila `fila-pedidos` é criada). Nos demais cenários o worker simplesmente não encontra mensagens e fica ocioso — custo zero de recursos.

### `src/Shared/AwsClientFactory.cs`

```csharp
public static AmazonRDSClient RDS(string endpoint = LocalStackFixture.Endpoint) =>
    ModoLocalStack
        ? new AmazonRDSClient(Credentials, Configure(new AmazonRDSConfig(), endpoint))
        : new AmazonRDSClient();

public static AmazonECSClient ECS(string endpoint = LocalStackFixture.Endpoint) =>
    ModoLocalStack
        ? new AmazonECSClient(Credentials, Configure(new AmazonECSConfig(), endpoint))
        : new AmazonECSClient();
```

### `src/Shared/Shared.csproj`

```xml
<PackageReference Include="AWSSDK.RDS" Version="3.*" />
<PackageReference Include="AWSSDK.ECS" Version="3.*" />
```

### `src/Shared/LocalStackFixture.cs`

```csharp
// Constante para os cenários que usam PostgreSQL real
public const string PostgresConnectionString =
    "Host=localhost;Port=5433;Database=testdb;Username=test;Password=test";
```

---

## Cenário 17 — RDS.Basic

### Arquivos

```
scenarios/17-RDS.Basic/
├── 17-RDS.Basic.csproj
├── Fixture.cs
└── RdsBasicTests.cs
```

### `Fixture.cs`

```
Constantes:
  NomeInstancia = "rds-test-db"
  NomeTabelaProdutos = "produtos"

Propriedades públicas:
  AmazonRDSClient RDS
  string ConnectionString   ← LocalStackFixture.PostgresConnectionString

InitializeScenarioAsync():
  RDS = AwsClientFactory.RDS()
  Aguarda PostgreSQL aceitar conexão Npgsql (PollingHelper, 60s, intervalo 1s)
  LocalStack: CreateDBInstanceAsync(identifier=rds-test-db, engine=postgres,
    class=db.t3.micro, storage=20, masterUser=test, masterPassword=test)
  LocalStack: DescribeDBInstancesAsync → poll status == "available" (30s)
  Npgsql: CREATE TABLE IF NOT EXISTS produtos (
    id TEXT PRIMARY KEY, nome TEXT NOT NULL,
    preco NUMERIC(10,2) NOT NULL, estoque INTEGER NOT NULL
  )

DisposeScenarioAsync():
  Npgsql: DROP TABLE IF EXISTS produtos
  RDS.Dispose()
```

### `RdsBasicTests.cs` — 8 testes

**Plano de controle AWS — 3 `[Fact]`**

| Método | Comportamento verificado |
|---|---|
| `CreateDBInstance_ShouldReturnInstanceIdentifier` | Identifier retornado == "rds-test-db", engine == "postgres" |
| `DescribeDBInstances_ShouldShowAvailableStatus` | Status da instância é "available" após provisionamento |
| `ModifyDBInstance_ShouldUpdateAllocatedStorage` | Modify aceito, PendingModifiedValues reflete a mudança |

**Plano de dados (Npgsql) — 5 `[Fact]`**

| Método | Comportamento verificado |
|---|---|
| `InsertProduct_ShouldPersistToDatabase` | INSERT + SELECT retorna o registro inserido |
| `QueryProducts_ShouldReturnAllInserted` | SELECT retorna N registros após N INSERTs |
| `UpdateProduct_ShouldModifyPrice` | UPDATE altera preço; SELECT retorna novo valor |
| `DeleteProduct_ShouldRemoveRecord` | DELETE remove registro; SELECT retorna vazio |
| `Transaction_WhenRolledBack_ShouldNotPersist` | INSERT dentro de transação revertida não aparece no SELECT |

---

## Cenário 18 — ECS.RunTask

### Arquivos

```
scenarios/18-ECS.RunTask/
├── 18-ECS.RunTask.csproj
├── Fixture.cs
└── EcsRunTaskTests.cs

src/tasks/pedido_processor/
├── handler.py
├── requirements.txt    ← boto3, psycopg2-binary
└── Dockerfile
```

### Worker Python (`handler.py`)

```python
Startup:
  Lê AWS_ENDPOINT_URL, DATABASE_URL, FILA_PEDIDOS_URL das env vars
  Conecta psycopg2 → CREATE TABLE IF NOT EXISTS pedidos (
    id TEXT PRIMARY KEY, cliente TEXT NOT NULL,
    valor NUMERIC(10,2) NOT NULL, status TEXT NOT NULL,
    processado_em TIMESTAMPTZ DEFAULT NOW()
  )

Loop (poll SQS a cada 2s com WaitTimeSeconds=2):
  try:
    ReceiveMessage → para cada mensagem:
      pedido = json.loads(body)
      INSERT INTO pedidos (id, cliente, valor, status)
      DeleteMessage
      print("[WORKER] Pedido {id} processado → cliente={cliente}")
  except QueueDoesNotExist:
    # Fila ainda não criada (fixture em inicialização) — tenta novamente em 2s
    time.sleep(2)
  except Exception as e:
    print(f"[WORKER] Erro: {e}") ; time.sleep(2)
```

> **Nota de race condition**: o container worker sobe antes de `InitializeScenarioAsync` criar a fila `fila-pedidos`. O try/except no loop garante que o worker simplesmente aguarda até a fila existir — sem crash.

### `Fixture.cs`

```
Constantes:
  NomeCluster     = "pedidos-cluster"
  FamiliaTask     = "pedido-processor"
  NomeFilaPedidos = "fila-pedidos"
  NomeTabelaPedidos = "pedidos"

Propriedades públicas:
  AmazonECSClient ECS
  AmazonSQSClient SQS
  string ClusterArn
  string TaskDefArn
  string UrlFilaPedidos

InitializeScenarioAsync():
  ECS = AwsClientFactory.ECS()
  SQS = AwsClientFactory.SQS()
  Aguarda PostgreSQL (mesmo padrão do cenário 17, 60s)
  Aguarda worker inicializar — poll: tenta SELECT 1 FROM pedidos até sucesso (30s)
  UrlFilaPedidos = SQS.CreateQueueAsync("fila-pedidos").QueueUrl
  ClusterArn = ECS.CreateClusterAsync("pedidos-cluster").Cluster.ClusterArn
  TaskDefArn = ECS.RegisterTaskDefinitionAsync(
    family="pedido-processor",
    containerDefinitions=[{
      name="worker", image="ecs-worker:latest",
      environment=[FILA_PEDIDOS_URL, DATABASE_URL, AWS_ENDPOINT_URL]
    }]
  ).TaskDefinition.TaskDefinitionArn

DisposeScenarioAsync():
  Npgsql: DELETE FROM pedidos (limpa dados entre runs, mantém schema)
  SQS.DeleteQueueAsync(UrlFilaPedidos)
  ECS.DeleteClusterAsync(NomeCluster)
  ECS.Dispose() | SQS.Dispose()
```

### `EcsRunTaskTests.cs` — 5 testes

**Plano de controle AWS — 3 `[Fact]`**

| Método | Comportamento verificado |
|---|---|
| `DescribeCluster_ShouldBeActive` | Cluster existe, status == "ACTIVE", registeredContainerInstancesCount >= 0 |
| `DescribeTaskDefinition_ShouldMatchRegisteredConfig` | Família, containerDefinitions e env vars batem com o registrado |
| `ListTaskDefinitions_ShouldIncludeRegisteredFamily` | ARN da task def retorna na listagem |

**Integração ECS + SQS + PostgreSQL — 2 `[Fact]`**

| Método | Comportamento verificado |
|---|---|
| `RunTask_ShouldPersistOrderToDatabase` | RunTask (API) + SendMessage (worker) → poll PostgreSQL confirma pedido gravado |
| `RunTask_WithMultipleOrders_ShouldPersistAll` | 3 pedidos enviados → PostgreSQL contém os 3 registros |

> **Isolamento entre testes**: como todos os testes compartilham a mesma instância de `Fixture` (`IClassFixture`), cada teste usa IDs únicos (ex: `pedido-runTask-001`, `pedido-multi-001`) para não colidir na tabela `pedidos`. O `DisposeScenarioAsync` faz `DELETE FROM pedidos` ao final de toda a classe.

### Fluxo narrativo do teste principal

```
>>> ECS.RunTask: submetendo task ao cluster 'pedidos-cluster'
    Overrides: PEDIDO_ID=pedido-001, CLIENTE=João Silva, VALOR=1500.00
    NOTA: LocalStack Community registra a task mas não executa o container.
          O worker Aspire (sempre ativo) processa a mensagem SQS abaixo.

>>> SQS.SendMessage: publicando pedido 'pedido-001' na fila 'fila-pedidos'
    Worker container receberá e processará a mensagem

>>> Polling PostgreSQL: aguardando até 30s pelo registro de 'pedido-001'
    Pedido encontrado: cliente=João Silva, valor=1500.00

>>> ECS.DescribeTasks: verificando estado final da task no plano de controle
    Task registrada com sucesso — ARN e cluster validados
```

---

## Convenções e contratos herdados

- Namespace: `Scenarios.RDS.Basic` e `Scenarios.ECS.RunTask`
- Todos os clientes via `AwsClientFactory` — nunca instanciados diretamente nos testes
- `PollingHelper.WaitUntilAsync` para toda espera assíncrona — nenhum `Task.Delay` fixo
- Testes descrevem comportamento de negócio, não detalhes de implementação
- Cada cenário é rodável independentemente: `dotnet test scenarios/17-RDS.Basic/`
- `aspire-aws.sln` registra os dois novos projetos

---

## Limitações conhecidas

| Limitação | Impacto | Mitigação |
|---|---|---|
| LocalStack Community não executa containers ECS | RunTask não aciona worker real | Worker é iniciado pelo Aspire; SQS é o trigger real. Documentado nos testes. |
| `host.docker.internal` requer Docker Desktop | Worker container não conecta em Linux puro | Usuários Linux definem `DOCKER_INTERNAL_HOST=172.17.0.1` (env var com fallback) |
| ECS worker sobe em todos os cenários | Overhead mínimo de memória | Worker fica idle nos cenários 01–17; sem impacto funcional |
