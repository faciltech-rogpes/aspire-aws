# aspire-aws

Catálogo de 15 cenários progressivos para testar integrações AWS localmente, usando **.NET Aspire** como orquestrador, **LocalStack** como simulador e **xUnit** como framework de testes.

Nenhuma conta AWS necessária. Tudo roda em Docker.

## Índice

- [Pré-requisitos](#pré-requisitos)
- [Quick start](#quick-start)
- [Catálogo de cenários](#catálogo-de-cenários)
  - [Básicos (01–06)](#básicos-0106--serviço-único-sem-lambda)
  - [Integração entre serviços (09–10)](#integração-entre-serviços-0910--sem-lambda)
  - [Lambda triggers (07–08, 11, 14)](#lambda-triggers-0708-11-14)
  - [Pipelines multi-serviço (12–13)](#pipelines-multi-serviço-1213)
  - [Orquestração (15)](#orquestração-15)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Como funciona](#como-funciona)
- [Limitações conhecidas](#limitações-conhecidas)
- [Documentação adicional](#documentação-adicional)

## Pré-requisitos


| Requisito | Versão mínima                             |
| --------- | ----------------------------------------- |
| .NET SDK  | 10.0                                      |
| Docker    | Em execução                               |
| Python    | 3.12 (apenas para editar handlers Lambda) |


## Quick start

```bash
git clone <repo-url> && cd aspire-aws

# Rodar um cenário específico
dotnet test scenarios/01-S3.Basic/

#Rodar de forma verbosa

dotnet test scenarios/<N>/ --logger "console;verbosity=detailed" 2>&1 | grep -E "(\[xUnit| >>>|^     [^ ]|  Aprovado|  Com falha|localstack is now in state Ready|Aprovado!|Com falha!)"

dotnet test scenarios/01-S3.Basic/ --logger "console;verbosity=detailed" 2>&1 | grep -E "(\[xUnit| >>>|^     [^ ]|  Aprovado|  Com falha|localstack is now in state Ready|Aprovado!|Com falha!)"


# Rodar a suíte completa (~3 min)
dotnet test aspire-aws.sln
```

O LocalStack sobe e desce automaticamente via Aspire — não é necessário `docker-compose` nem scripts externos.

## Catálogo de cenários

### Básicos (01–06) — serviço único, sem Lambda


| #   | Cenário              | Serviços AWS   | Testes | O que demonstra                                |
| --- | -------------------- | -------------- | ------ | ---------------------------------------------- |
| 01  | S3.Basic             | S3             | 5      | CRUD de buckets/objetos, presigned URLs        |
| 02  | SQS.Basic            | SQS            | 4      | Send/receive, delete, dead-letter queue        |
| 03  | DynamoDB.Basic       | DynamoDB       | 4      | Put/get, scan, query, delete                   |
| 04  | SNS.Basic            | SNS, SQS       | 3      | Criar tópico, publicar com subscriber SQS      |
| 05  | SSM.Basic            | SSM            | 3      | Parâmetros String/SecureString, busca por path |
| 06  | SecretsManager.Basic | SecretsManager | 3      | Criar, atualizar, deletar segredos             |


### Integração entre serviços (09–10) — sem Lambda


| #   | Cenário             | Serviços AWS | Testes | O que demonstra                              |
| --- | ------------------- | ------------ | ------ | -------------------------------------------- |
| 09  | SNS.SQS.Fanout      | SNS, SQS     | 1      | Fan-out: uma mensagem SNS entregue a N filas |
| 10  | S3.SQS.Notification | S3, SQS      | 1      | Upload no S3 gera notificação na fila SQS    |


### Lambda triggers (07–08, 11, 14)


| #   | Cenário             | Fluxo                           | Testes | O que demonstra                                          |
| --- | ------------------- | ------------------------------- | ------ | -------------------------------------------------------- |
| 07  | S3.Lambda.Trigger   | S3 → Lambda → DynamoDB          | 1      | Upload dispara Lambda que persiste no DynamoDB           |
| 08  | SQS.Lambda.Consumer | SQS → Lambda → DynamoDB         | 1      | Mensagem na fila dispara Lambda via event source mapping |
| 11  | DynamoDB.Lambda     | invoke → Lambda → DynamoDB      | 1      | Invocação direta de Lambda com persistência              |
| 14  | EventBridge.Lambda  | EventBridge → Lambda → DynamoDB | 2      | Regra com filtro de source + teste negativo              |


### Pipelines multi-serviço (12–13)


| #   | Cenário                         | Fluxo                        | Testes | O que demonstra                                |
| --- | ------------------------------- | ---------------------------- | ------ | ---------------------------------------------- |
| 12  | Pipeline.S3.SQS.Lambda.DynamoDB | S3 → SQS → Lambda → DynamoDB | 1      | Pipeline completo de processamento de arquivos |
| 13  | Pipeline.SNS.SQS.Lambda.S3      | SNS → SQS → Lambda → S3      | 1      | Pipeline de fan-out com resultado salvo no S3  |


### Orquestração (15)


| #   | Cenário                     | Serviços AWS                    | Testes | O que demonstra                           |
| --- | --------------------------- | ------------------------------- | ------ | ----------------------------------------- |
| 15  | StepFunctions.Orchestration | StepFunctions, Lambda, DynamoDB | 2      | State machine com Task + Choice branching |


### Pipeline com agendamento e roteamento (16)


| #   | Cenário                       | Serviços AWS                                    | Testes | O que demonstra                                                        |
| --- | ----------------------------- | ----------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| 16  | Pipeline.Scheduler.Router     | EventBridge Scheduler, SQS, Lambda, DynamoDB    | 4      | Agendador → produção de ofertas → roteamento por regras no DynamoDB → fila dedicada por segmento. Suporte dual-modo LocalStack/AWS via `AWS_TARGET` |


> Cenários Lambda (07–08, 11–14, 16) são skipped automaticamente no macOS ARM64.  
> Cenário 15 requer LocalStack Pro.

## Estrutura do projeto

```
aspire-aws/
├── src/
│   ├── AppHost/           Aspire AppHost — configura container LocalStack
│   ├── Shared/            Fixtures e helpers compartilhados
│   │   ├── LocalStackFixture.cs    Ciclo de vida Aspire + lock de porta
│   │   ├── AwsClientFactory.cs     Factory de clientes AWS → localhost:4566
│   │   ├── LambdaDeployer.cs       Zip + deploy de Python Lambdas
│   │   └── PollingHelper.cs        WaitUntilAsync / AssertNeverAsync
│   └── lambdas/           Handlers Python (um diretório por função)
│       ├── s3_processor/
│       ├── sqs_consumer/
│       ├── dynamodb_writer/
│       ├── fanout_processor/
│       ├── eventbridge_handler/
│       └── stepfunctions_task/
├── scenarios/             Um projeto xUnit independente por cenário
│   ├── 01-S3.Basic/
│   │   ├── Fixture.cs     Setup de recursos AWS para este cenário
│   │   └── S3BasicTests.cs
│   ├── 02-SQS.Basic/
│   │   └── ...
│   └── 15-StepFunctions.Orchestration/
└── docs/
    └── architecture.md    Documento detalhado de arquitetura e conceitos
```

## Como funciona

```
dotnet test scenarios/XX-Foo/
        │
        ▼
  Fixture : LocalStackFixture (IAsyncLifetime)
        │
        ├─ Adquire file lock exclusivo (porta 4566)
        ├─ Inicia Aspire AppHost → sobe container LocalStack
        ├─ Polling no healthcheck até LocalStack responder
        ├─ InitializeScenarioAsync() → cria recursos AWS do cenário
        │
        ▼
  Testes xUnit rodam contra LocalStack via AWS SDK for .NET
        │
        ▼
  DisposeAsync() → destrói container, libera lock
```

**Aspire** substitui `docker-compose`: o `AppHost/Program.cs` declara o container LocalStack com serviços, portas e bind mounts. O `DistributedApplicationTestingBuilder` inicia tudo programaticamente dentro do xUnit.

**Cada cenário é isolado.** Seu próprio projeto, fixture, recursos AWS e ciclo de vida. Cenários rodam sequencialmente (um assembly por vez) para compartilhar a porta 4566 sem conflito.

**Assertions assíncronas usam polling, nunca `Task.Delay`.** O `PollingHelper.WaitUntilAsync` repete uma condição com intervalo configurável até timeout. Para testes negativos (verificar que algo *não* acontece), há `AssertNeverAsync`.

## Limitações conhecidas


| Limitação                              | Cenários afetados           | Alternativa                         |
| -------------------------------------- | --------------------------- | ----------------------------------- |
| Lambda no LocalStack 3.8 + macOS ARM64 | 07, 08, 11, 12, 13, 14      | Rodar em Linux/CI ou LocalStack Pro |
| Step Functions na edição Community     | 15                          | LocalStack Pro                      |
| Porta fixa 4566                        | Todos (execução sequencial) | —                                   |


## Documentação adicional

- [docs/architecture.md](docs/architecture.md) — Arquitetura detalhada, conceitos AWS, padrões de implementação