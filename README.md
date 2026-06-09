# aspire-aws

Catálogo de 16 cenários progressivos para testar integrações AWS localmente, usando **.NET Aspire** como orquestrador, **LocalStack** como simulador e **xUnit** como framework de testes.

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
  - [Pipeline com agendamento e roteamento (16)](#pipeline-com-agendamento-e-roteamento-16)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Como funciona](#como-funciona)
- [Limitações conhecidas](#limitações-conhecidas)
- [Documentação adicional](#documentação-adicional)


## Arquitetura de Referência

<img width="1536" height="1024" alt="image" src="https://github.com/user-attachments/assets/22416206-eef9-4ddd-9cf4-8faf6e9fc400" />


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
| 01  | [S3.Basic](docs/roteiros/01-s3-basic.md)             | S3             | 5      | CRUD de buckets/objetos, presigned URLs        |
| 02  | [SQS.Basic](docs/roteiros/02-sqs-basic.md)           | SQS            | 4      | Send/receive, delete, dead-letter queue        |
| 03  | [DynamoDB.Basic](docs/roteiros/03-dynamodb-basic.md) | DynamoDB       | 4      | Put/get, scan, query, delete                   |
| 04  | [SNS.Basic](docs/roteiros/04-sns-basic.md)           | SNS, SQS       | 3      | Criar tópico, publicar com subscriber SQS      |
| 05  | [SSM.Basic](docs/roteiros/05-ssm-basic.md)           | SSM            | 3      | Parâmetros String/SecureString, busca por path |
| 06  | [SecretsManager.Basic](docs/roteiros/06-secrets-manager-basic.md) | SecretsManager | 3 | Criar, atualizar, deletar segredos        |


### Integração entre serviços (09–10) — sem Lambda


| #   | Cenário             | Serviços AWS | Testes | O que demonstra                              |
| --- | ------------------- | ------------ | ------ | -------------------------------------------- |
| 09  | [SNS.SQS.Fanout](docs/roteiros/09-sns-sqs-fanout.md)           | SNS, SQS | 1 | Fan-out: uma mensagem SNS entregue a N filas |
| 10  | [S3.SQS.Notification](docs/roteiros/10-s3-sqs-notification.md) | S3, SQS  | 1 | Upload no S3 gera notificação na fila SQS    |


### Lambda triggers (07–08, 11, 14)


| #   | Cenário             | Fluxo                           | Testes | O que demonstra                                          |
| --- | ------------------- | ------------------------------- | ------ | -------------------------------------------------------- |
| 07  | [S3.Lambda.Trigger](docs/roteiros/07-s3-lambda-trigger.md)     | S3 → Lambda → DynamoDB          | 1 | Upload dispara Lambda que persiste no DynamoDB           |
| 08  | [SQS.Lambda.Consumer](docs/roteiros/08-sqs-lambda-consumer.md) | SQS → Lambda → DynamoDB         | 1 | Mensagem na fila dispara Lambda via event source mapping |
| 11  | [DynamoDB.Lambda](docs/roteiros/11-dynamodb-lambda.md)         | invoke → Lambda → DynamoDB      | 1 | Invocação direta de Lambda com persistência              |
| 14  | [EventBridge.Lambda](docs/roteiros/14-eventbridge-lambda.md)   | EventBridge → Lambda → DynamoDB | 2 | Regra com filtro de source + teste negativo              |


### Pipelines multi-serviço (12–13)


| #   | Cenário                         | Fluxo                        | Testes | O que demonstra                                |
| --- | ------------------------------- | ---------------------------- | ------ | ---------------------------------------------- |
| 12  | [Pipeline.S3.SQS.Lambda.DynamoDB](docs/roteiros/12-pipeline-s3-sqs-lambda-dynamodb.md) | S3 → SQS → Lambda → DynamoDB | 1 | Pipeline completo de processamento de arquivos |
| 13  | [Pipeline.SNS.SQS.Lambda.S3](docs/roteiros/13-pipeline-sns-sqs-lambda-s3.md)           | SNS → SQS → Lambda → S3      | 1 | Pipeline de fan-out com resultado salvo no S3  |


### Orquestração (15)


| #   | Cenário                     | Serviços AWS                    | Testes | O que demonstra                           |
| --- | --------------------------- | ------------------------------- | ------ | ----------------------------------------- |
| 15  | [StepFunctions.Orchestration](docs/roteiros/15-stepfunctions-orchestration.md) | StepFunctions, Lambda, DynamoDB | 2 | State machine com Task + Choice branching |


### Pipeline com agendamento e roteamento (16)


| #   | Cenário                       | Serviços AWS                                    | Testes | O que demonstra                                                        |
| --- | ----------------------------- | ----------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| 16  | [Pipeline.Scheduler.Router](scenarios/16-Pipeline.Scheduler.Router/README.md) | EventBridge Scheduler, SQS, Lambda, DynamoDB | 4 | Agendador → produção de ofertas → roteamento por regras no DynamoDB → fila dedicada por segmento. Suporte dual-modo LocalStack/AWS via `AWS_TARGET` |


### RDS e PostgreSQL (17)


| #   | Cenário   | Serviços AWS       | Testes | O que demonstra                                                                               |
| --- | --------- | ------------------ | ------ | --------------------------------------------------------------------------------------------- |
| 17  | [RDS.Basic](docs/roteiros/17-rds-basic.md) | RDS, PostgreSQL | 8 | API de controle RDS (Pro): criar/descrever/modificar instância. Plano de dados: CRUD + transação via Npgsql direto no PostgreSQL |


> 3 testes de plano de controle RDS são skipped na edição Community (requerem LocalStack Pro). 5 testes de plano de dados passam sempre.

### ECS + Worker Docker (18)


| #   | Cenário        | Serviços AWS              | Testes | O que demonstra                                                                                                      |
| --- | -------------- | ------------------------- | ------ | -------------------------------------------------------------------------------------------------------------------- |
| 18  | [ECS.RunTask](docs/roteiros/18-ecs-runtask.md) | ECS, SQS, PostgreSQL | 5 | API de controle ECS (Pro): criar cluster, registrar task definition, RunTask. Integração: worker Docker real consome SQS e persiste pedidos no PostgreSQL |


> 3 testes de plano de controle ECS são skipped na edição Community (requerem LocalStack Pro). 2 testes de integração (worker real) passam sempre.

> Cenários Lambda (07–08, 11–14, 16) são skipped automaticamente no macOS ARM64.  
> Cenários 15, e testes de plano de controle de 17 e 18, requerem LocalStack Pro.

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
│   ├── lambdas/           Handlers Python invocados via Lambda (cenários 07–16)
│   │   ├── s3_processor/
│   │   ├── sqs_consumer/
│   │   ├── dynamodb_writer/
│   │   ├── fanout_processor/
│   │   ├── eventbridge_handler/
│   │   ├── stepfunctions_task/
│   │   ├── produtor_de_ofertas/
│   │   ├── roteador_de_ofertas/
│   │   └── eco_consignado/
│   └── tasks/             Workers de longa duração em container Docker (cenário 18)
│       └── pedido_processor/   Worker Python: consome SQS e persiste no PostgreSQL
├── scenarios/             Um projeto xUnit independente por cenário
│   ├── 01-S3.Basic/
│   │   ├── Fixture.cs     Setup de recursos AWS para este cenário
│   │   └── S3BasicTests.cs
│   ├── 02-SQS.Basic/
│   │   └── ...
│   ├── 15-StepFunctions.Orchestration/
│   └── 16-Pipeline.Scheduler.Router/
└── docs/
    ├── architecture/      Índice arquitetural, diagramas C4 e ADRs
    ├── architecture.md    Documento detalhado de arquitetura e conceitos
    └── roteiros/          Roteiros didáticos por cenário
```

| Módulo | Documentação |
|--------|--------------|
| `src/AppHost/` | [README](src/AppHost/README.md) — configura e inicia os containers LocalStack e PostgreSQL via Aspire |
| `src/Shared/` | [README](src/Shared/README.md) — fixtures, factory de clientes AWS, polling e deploy de Lambdas |
| `src/lambdas/` | [README](src/lambdas/README.md) — handlers Python por cenário Lambda (cenários 07–16) |
| `src/tasks/` | Worker de longa duração em container Docker; usado pelo cenário 18 (ECS.RunTask) |

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


| Limitação                                        | Cenários afetados               | Alternativa                         |
| ------------------------------------------------ | ------------------------------- | ----------------------------------- |
| Lambda no LocalStack 3.8 + macOS ARM64           | 07, 08, 11, 12, 13, 14, 16      | Rodar em Linux/CI ou LocalStack Pro |
| Step Functions na edição Community               | 15                              | LocalStack Pro                      |
| RDS control plane API na edição Community        | 17 (3 testes de 8)              | LocalStack Pro                      |
| ECS control plane API na edição Community        | 18 (3 testes de 5)              | LocalStack Pro                      |
| Porta fixa 4566                                  | Todos (execução sequencial)     | —                                   |


## Documentação adicional

- [docs/architecture/README.md](docs/architecture/README.md) — Índice arquitetural: diagramas C4, mapa de módulos e ADRs
- [docs/architecture/adrs/](docs/architecture/adrs/) — Decisões arquiteturais (ADR-001 a ADR-006)
- [docs/architecture.md](docs/architecture.md) — Arquitetura detalhada, conceitos AWS, padrões de implementação
- [docs/docker-engine-wsl2-windows.md](docs/docker-engine-wsl2-windows.md) — Setup e troubleshooting de Docker Engine no Windows via WSL2
