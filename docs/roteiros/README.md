# Roteiros dos Cenarios

Esta pasta existe para ajudar quem esta comecando em [AWS](https://aws.amazon.com/what-is-aws/?nc2=h_dsc_aa_wtaws), [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) e [LocalStack](https://docs.localstack.cloud/).
Cada arquivo explica um cenario do projeto em linguagem simples, mostra trechos
de codigo reais e traz um passo a passo curto para executar o teste.

## Tecnologias citadas pela primeira vez

- [AWS](https://aws.amazon.com/what-is-aws/?nc2=h_dsc_aa_wtaws): plataforma de nuvem da Amazon. Neste projeto, os servicos AWS sao emulados localmente.
- [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview): conjunto de ferramentas para orquestrar aplicacoes distribuidas no ambiente de desenvolvimento.
- [AppHost do Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview#the-apphost): projeto onde a topologia local da aplicacao e dos containers eh definida.
- [LocalStack](https://docs.localstack.cloud/): emulador local de servicos de nuvem. Aqui ele imita varios servicos da AWS na sua maquina.
- [xUnit](https://xunit.net/docs/getting-started/v2/getting-started): framework de testes usado pelos cenarios em C#.
- [AWS SDK for .NET](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/welcome.html): biblioteca que permite ao codigo C# conversar com servicos AWS ou, neste caso, com o LocalStack.

## Conceitos base deste conjunto de roteiros

- `Cenario`: um projeto de teste isolado dentro de `scenarios/`. Cada cenario ensina uma combinacao diferente de servicos.
- `Fixture`: classe que prepara o ambiente antes do teste rodar. No projeto, o fixture cria clientes AWS e, quando necessario, cria buckets, filas, tabelas e Lambdas.
- `LocalStackFixture`: fixture base compartilhado do projeto. Ele sobe o AppHost, espera o LocalStack ficar saudavel e depois chama a inicializacao especifica do cenario.
- `Fabrica compartilhada`: apelido para a classe [AwsClientFactory.cs](../../src/Shared/AwsClientFactory.cs). Ela centraliza a criacao dos clientes AWS para todos os testes, evitando repeticao e configuracoes inconsistentes.

## Ideia geral do projeto

O repositorio usa quatro pecas principais:

1. [xUnit](https://xunit.net/docs/getting-started/v2/getting-started): framework de teste em C#.
2. [Aspire AppHost](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview#the-apphost): sobe a infraestrutura local necessaria para os testes.
3. [LocalStack](https://docs.localstack.cloud/): emula servicos AWS localmente, na porta `4566`.
4. [AWS SDK for .NET](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/welcome.html): conversa com o LocalStack como se estivesse falando com a AWS.

## Fluxo comum de quase todos os cenarios

1. O teste chama um `Fixture`.
2. O `Fixture` herda de [LocalStackFixture](../../src/Shared/LocalStackFixture.cs).
3. O `LocalStackFixture` sobe o [AppHost](../../src/AppHost/Program.cs).
4. O AppHost sobe um container do [LocalStack](https://docs.localstack.cloud/).
5. O `Fixture` do cenario cria os recursos necessarios: bucket do [Amazon S3](https://docs.aws.amazon.com/AmazonS3/latest/userguide/GetStartedWithS3.html), fila do [Amazon SQS](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html), tabela do [Amazon DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Introduction.html), [Lambda](https://docs.aws.amazon.com/lambda/latest/dg/welcome.html) e assim por diante.
6. O teste faz uma acao.
7. O codigo valida o resultado.

Trecho central do bootstrap:

```csharp
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.AppHost>();

_app = await appHost.BuildAsync();
await _app.StartAsync();
await WaitForLocalStackAsync();
await InitializeScenarioAsync();
```

## Antes de rodar qualquer roteiro

1. Abra o Docker Desktop.
2. Entre na raiz do repositorio:

```bash
cd /Volumes/Marco-Dev/dev/aspire-aws
```

3. Se for sua primeira execucao, restaure e compile:

```bash
dotnet restore aspire-aws.sln
dotnet build aspire-aws.sln --no-restore -m:1 -v minimal
```

## Comandos uteis

Rodar um cenario:

```bash
dotnet test scenarios/01-S3.Basic/
```

Rodar a solucao inteira:

```bash
dotnet test aspire-aws.sln --no-build -v minimal
```

## Observacoes importantes

- O projeto usa a porta fixa `4566` para o LocalStack.
- A execucao agregada da solucao foi serializada para evitar disputa por essa porta.
- Em `macOS arm64`, os cenarios com Lambda assincrona podem aparecer como `SKIP`.
- O cenario `15` de [AWS Step Functions](https://docs.aws.amazon.com/step-functions/latest/dg/welcome.html) ja nasce como `SKIP`, porque o suporte nem sempre existe no LocalStack Community.

## Ordem sugerida de estudo

1. [01-s3-basic.md](./01-s3-basic.md)
2. [02-sqs-basic.md](./02-sqs-basic.md)
3. [03-dynamodb-basic.md](./03-dynamodb-basic.md)
4. [04-sns-basic.md](./04-sns-basic.md)
5. [05-ssm-basic.md](./05-ssm-basic.md)
6. [06-secrets-manager-basic.md](./06-secrets-manager-basic.md)
7. [07-s3-lambda-trigger.md](./07-s3-lambda-trigger.md)
8. [08-sqs-lambda-consumer.md](./08-sqs-lambda-consumer.md)
9. [09-sns-sqs-fanout.md](./09-sns-sqs-fanout.md)
10. [10-s3-sqs-notification.md](./10-s3-sqs-notification.md)
11. [11-dynamodb-lambda.md](./11-dynamodb-lambda.md)
12. [12-pipeline-s3-sqs-lambda-dynamodb.md](./12-pipeline-s3-sqs-lambda-dynamodb.md)
13. [13-pipeline-sns-sqs-lambda-s3.md](./13-pipeline-sns-sqs-lambda-s3.md)
14. [14-eventbridge-lambda.md](./14-eventbridge-lambda.md)
15. [15-stepfunctions-orchestration.md](./15-stepfunctions-orchestration.md)
16. [17-rds-basic.md](./17-rds-basic.md)
17. [18-ecs-runtask.md](./18-ecs-runtask.md)
