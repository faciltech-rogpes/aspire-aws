# lambdas

## Responsabilidade

Handlers Python invocados pelo LocalStack nos cenários que envolvem AWS Lambda. Cada subdiretório é uma função Lambda independente: contém um único `handler.py` com a função `lambda_handler(event, context)`. Os handlers usam `boto3` para interagir com outros serviços AWS a partir do ambiente de execução do LocalStack.

Não contém lógica de testes nem de orquestração — essa responsabilidade fica em `src/Shared/` e nos `Fixture.cs` dos cenários.

## Estrutura

| Pasta | Cenário(s) | O que faz |
|-------|-----------|-----------|
| `s3_processor/` | 07 | Processado por upload no S3; persiste metadados no DynamoDB |
| `sqs_consumer/` | 08 | Consome mensagens da fila SQS via event source mapping; persiste no DynamoDB |
| `dynamodb_writer/` | 11 | Invocado diretamente; escreve item no DynamoDB |
| `fanout_processor/` | 13 | Acionado por fila SQS (fan-out SNS); salva resultado no S3 |
| `eventbridge_handler/` | 14 | Acionado por regra EventBridge com filtro de `source`; persiste no DynamoDB |
| `stepfunctions_task/` | 15 | Task de state machine Step Functions; retorna resultado para o orquestrador |
| `produtor_de_ofertas/` | 16 | Acionado pelo EventBridge Scheduler; gera oferta de crédito e publica em fila SQS |
| `roteador_de_ofertas/` | 16 | Acionado por fila SQS; lê regras do DynamoDB (cache em memória) e roteia para fila do segmento |
| `eco_consignado/` | 16 | Acionado por fila SQS; imprime oferta no stdout |

## Como usar

Os handlers **não são invocados diretamente** — o `LambdaDeployer` em `src/Shared/` empacota o diretório em ZIP e faz deploy no LocalStack durante `InitializeScenarioAsync`:

```csharp
// Dentro de Fixture.cs do cenário
Lambda = AwsClientFactory.Lambda();
await LambdaDeployer.DeployAsync(Lambda, "s3_processor");
```

Todos os handlers seguem o mesmo padrão Python:

```python
# src/lambdas/sqs_consumer/handler.py (padrão)
import os
import boto3

def lambda_handler(event, context):
    endpoint = os.environ.get("AWS_ENDPOINT_URL")  # LocalStack injeta automaticamente
    dynamodb = boto3.resource("dynamodb", endpoint_url=endpoint)
    table = dynamodb.Table(os.environ["DYNAMODB_TABLE"])

    for record in event["Records"]:
        body = record["body"]
        table.put_item(Item={"id": body, "processed": True})
```

O `AWS_ENDPOINT_URL` é injetado automaticamente pelo LocalStack no ambiente de execução da Lambda — o handler não precisa distinguir LocalStack de AWS real.

## Decisões relevantes

- [ADR-001](../../docs/architecture/adrs/ADR-001-stack-tecnologica.md) — por que Python 3.12 para os handlers e não C# ou Node.js
- [ADR-003](../../docs/architecture/adrs/ADR-003-simulacao-servicos-aws.md) — limitações de Lambda no LocalStack Community em macOS ARM64
