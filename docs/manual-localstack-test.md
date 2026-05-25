# Teste Manual dos Serviços AWS via LocalStack Docker

Guia para verificar que cada serviço AWS está funcionando corretamente no LocalStack via Docker.
Todos os comandos devem ser executados no **Git Bash** (não no PowerShell). Docker Desktop deve estar rodando.

---

## 1. Iniciar LocalStack

```bash
docker run -d --name ls-test \
  -e AWS_ACCESS_KEY_ID=test \
  -e AWS_SECRET_ACCESS_KEY=test \
  -e AWS_DEFAULT_REGION=us-east-1 \
  -e SERVICES=s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,scheduler,stepfunctions \
  -e DOCKER_HOST=unix:///var/run/docker.sock \
  -e LAMBDA_RUNTIME_ENVIRONMENT_TIMEOUT=120 \
  -e LAMBDA_REMOVE_CONTAINERS=true \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -p 4566:4566 \
  localstack/localstack:3.8
```

Aguardar ~15s e verificar health:

```bash
curl http://localhost:4566/_localstack/health
```

Deve retornar JSON com os serviços listados como `"available"` ou `"running"`.

---

## 2. S3

```bash
# Criar bucket
docker exec ls-test awslocal s3 mb s3://test-bucket

# Upload de arquivo
docker exec ls-test bash -c "echo 'hello world' > /tmp/test.txt"
docker exec ls-test awslocal s3 cp /tmp/test.txt s3://test-bucket/test.txt

# Listar objetos
docker exec ls-test awslocal s3 ls s3://test-bucket/

# Download e verificar conteúdo
docker exec ls-test awslocal s3 cp s3://test-bucket/test.txt /tmp/downloaded.txt
docker exec ls-test cat /tmp/downloaded.txt
# Esperado: "hello world"

# Deletar
docker exec ls-test awslocal s3 rm s3://test-bucket/test.txt
docker exec ls-test awslocal s3 rb s3://test-bucket
```

---

## 3. SQS

```bash
# Criar fila
docker exec ls-test awslocal sqs create-queue --queue-name test-queue

# Enviar mensagem
docker exec ls-test awslocal sqs send-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/test-queue \
  --message-body '{"id":"msg-001","content":"teste"}'

# Receber mensagem
docker exec ls-test awslocal sqs receive-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/test-queue \
  --max-number-of-messages 1
# Esperado: JSON com a mensagem enviada

# Deletar fila
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/test-queue
```

---

## 4. SNS

```bash
# Criar tópico
docker exec ls-test awslocal sns create-topic --name test-topic
# Anotar o TopicArn retornado

# Criar fila SQS para subscrição
docker exec ls-test awslocal sqs create-queue --queue-name sns-subscriber

# Subscrever fila ao tópico
docker exec ls-test awslocal sns subscribe \
  --topic-arn arn:aws:sns:us-east-1:000000000000:test-topic \
  --protocol sqs \
  --notification-endpoint arn:aws:sqs:us-east-1:000000000000:sns-subscriber

# Publicar mensagem
docker exec ls-test awslocal sns publish \
  --topic-arn arn:aws:sns:us-east-1:000000000000:test-topic \
  --message '{"evento":"teste-sns"}'

# Verificar recebimento na fila
docker exec ls-test awslocal sqs receive-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/sns-subscriber
# Esperado: mensagem com wrapper SNS contendo {"evento":"teste-sns"}

# Cleanup
docker exec ls-test awslocal sns delete-topic --topic-arn arn:aws:sns:us-east-1:000000000000:test-topic
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/sns-subscriber
```

---

## 5. DynamoDB

```bash
# Criar tabela
docker exec ls-test awslocal dynamodb create-table \
  --table-name test-table \
  --attribute-definitions AttributeName=id,AttributeType=S \
  --key-schema AttributeName=id,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST

# Inserir item
docker exec ls-test awslocal dynamodb put-item \
  --table-name test-table \
  --item '{"id":{"S":"item-001"},"nome":{"S":"Teste"},"valor":{"N":"42"}}'

# Ler item
docker exec ls-test awslocal dynamodb get-item \
  --table-name test-table \
  --key '{"id":{"S":"item-001"}}'
# Esperado: item com id=item-001, nome=Teste, valor=42

# Scan
docker exec ls-test awslocal dynamodb scan --table-name test-table

# Deletar tabela
docker exec ls-test awslocal dynamodb delete-table --table-name test-table
```

---

## 6. Lambda

```bash
# Criar handler Python dentro do container
# Nota: use heredoc para evitar problemas com ! e aspas no bash
# Nota: ZipInfo com external_attr define permissões Unix no arquivo (evita Permission denied)
docker exec -i ls-test python3 << 'PYEOF'
import zipfile
info = zipfile.ZipInfo('handler.py')
info.external_attr = 0o644 << 16
code = '''def lambda_handler(event, context):
    name = event.get("name", "World")
    return {"statusCode": 200, "body": f"Hello, {name}!"}
'''
with zipfile.ZipFile('/tmp/lambda.zip', 'w') as z:
    z.writestr(info, code)
PYEOF

# Criar função
docker exec ls-test awslocal lambda create-function \
  --function-name test-hello \
  --runtime python3.12 \
  --handler handler.lambda_handler \
  --role arn:aws:iam::000000000000:role/test-role \
  --zip-file fileb:///tmp/lambda.zip

# Aguardar ficar Active (~5-10s)
docker exec ls-test awslocal lambda wait function-active-v2 --function-name test-hello

# Invocar com payload inline
# Nota: usar bash -c mantém o caminho /tmp dentro do container (evita conversão MSYS)
docker exec ls-test bash -c 'awslocal lambda invoke --function-name test-hello --payload "{\"name\":\"LocalStack\"}" /tmp/lambda_out.json && cat /tmp/lambda_out.json'
# Esperado: {"statusCode": 200, "body": "Hello, LocalStack!"}

# Deletar função
docker exec ls-test awslocal lambda delete-function --function-name test-hello
```

### 6.1 Lambda com boto3 (acesso a outros serviços)

```bash
# Criar fila destino
docker exec ls-test awslocal sqs create-queue --queue-name lambda-target

# Criar handler que envia para SQS
docker exec -i ls-test python3 << 'PYEOF'
import zipfile
info = zipfile.ZipInfo('handler.py')
info.external_attr = 0o644 << 16
code = '''import json, os, boto3

def lambda_handler(event, context):
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    sqs = boto3.client("sqs", endpoint_url=endpoint_url)
    queue_url = os.environ["QUEUE_URL"]
    sqs.send_message(QueueUrl=queue_url, MessageBody=json.dumps(event))
    return {"statusCode": 200, "sent": True}
'''
with zipfile.ZipFile('/tmp/sqs_lambda.zip', 'w') as z:
    z.writestr(info, code)
PYEOF

# Obter URL da fila
docker exec ls-test awslocal sqs get-queue-url --queue-name lambda-target

# Criar função com env var (substituir QUEUE_URL pelo valor real)
docker exec ls-test awslocal lambda create-function \
  --function-name test-sqs-sender \
  --runtime python3.12 \
  --handler handler.lambda_handler \
  --role arn:aws:iam::000000000000:role/test-role \
  --zip-file fileb:///tmp/sqs_lambda.zip \
  --environment 'Variables={QUEUE_URL=http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/lambda-target}'

# Aguardar
docker exec ls-test awslocal lambda wait function-active-v2 --function-name test-sqs-sender

# Invocar com payload inline
docker exec ls-test bash -c 'awslocal lambda invoke --function-name test-sqs-sender --payload "{\"id\":\"evt-001\",\"tipo\":\"teste\"}" /tmp/out2.json && cat /tmp/out2.json'
# Esperado: {"statusCode": 200, "sent": true}

# Verificar mensagem na fila
docker exec ls-test awslocal sqs receive-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/lambda-target
# Esperado: mensagem com body {"id":"evt-001","tipo":"teste"}

# Cleanup
docker exec ls-test awslocal lambda delete-function --function-name test-sqs-sender
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/lambda-target
```

---

## 7. SSM Parameter Store

> **Nota:** paths como `/app/...` são convertidos pelo MSYS do Git Bash para caminhos Windows.
> Use `bash -c "..."` em todos os comandos SSM que recebem paths.

```bash
# Criar parâmetro
docker exec ls-test bash -c "awslocal ssm put-parameter --name /app/config/database-url --value 'postgres://user:pass@host:5432/db' --type String"

# Criar parâmetro seguro
docker exec ls-test bash -c "awslocal ssm put-parameter --name /app/secrets/api-key --value 'sk-abc123secret' --type SecureString"

# Ler parâmetro
docker exec ls-test bash -c "awslocal ssm get-parameter --name /app/config/database-url"
# Esperado: Value = "postgres://user:pass@host:5432/db"

# Ler com decriptação
docker exec ls-test bash -c "awslocal ssm get-parameter --name /app/secrets/api-key --with-decryption"

# Listar por path
docker exec ls-test bash -c "awslocal ssm get-parameters-by-path --path /app/ --recursive"

# Deletar
docker exec ls-test bash -c "awslocal ssm delete-parameter --name /app/config/database-url"
docker exec ls-test bash -c "awslocal ssm delete-parameter --name /app/secrets/api-key"
```

---

## 8. Secrets Manager

```bash
# Criar secret
docker exec ls-test awslocal secretsmanager create-secret \
  --name test/database-credentials \
  --secret-string '{"username":"admin","password":"SuperSecret123"}'

# Ler secret
docker exec ls-test awslocal secretsmanager get-secret-value --secret-id test/database-credentials
# Esperado: SecretString com as credenciais JSON

# Atualizar
docker exec ls-test awslocal secretsmanager update-secret \
  --secret-id test/database-credentials \
  --secret-string '{"username":"admin","password":"NewPassword456"}'

# Verificar atualização
docker exec ls-test awslocal secretsmanager get-secret-value --secret-id test/database-credentials

# Deletar
docker exec ls-test awslocal secretsmanager delete-secret \
  --secret-id test/database-credentials --force-delete-without-recovery
```

---

## 9. EventBridge

```bash
# Criar event bus customizado
docker exec ls-test awslocal events create-event-bus --name test-bus

# Criar regra
docker exec ls-test awslocal events put-rule \
  --name test-rule \
  --event-bus-name test-bus \
  --event-pattern '{"source":["app.test"],"detail-type":["OrderCreated"]}'

# Criar fila SQS como target
docker exec ls-test awslocal sqs create-queue --queue-name events-target

# Adicionar target
docker exec ls-test awslocal events put-targets \
  --rule test-rule \
  --event-bus-name test-bus \
  --targets 'Id=sqs-target,Arn=arn:aws:sqs:us-east-1:000000000000:events-target'

# Enviar evento
docker exec ls-test awslocal events put-events \
  --entries '[{"Source":"app.test","DetailType":"OrderCreated","Detail":"{\"orderId\":\"order-123\",\"amount\":99.90}","EventBusName":"test-bus"}]'

# Verificar recebimento (aguardar 2-3s)
docker exec ls-test awslocal sqs receive-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/events-target \
  --wait-time-seconds 5
# Esperado: mensagem com o evento enviado

# Cleanup
docker exec ls-test awslocal events remove-targets --rule test-rule --event-bus-name test-bus --ids sqs-target
docker exec ls-test awslocal events delete-rule --name test-rule --event-bus-name test-bus
docker exec ls-test awslocal events delete-event-bus --name test-bus
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/events-target
```

---

## 10. EventBridge Scheduler

```bash
# Criar fila destino
docker exec ls-test awslocal sqs create-queue --queue-name scheduler-target

# Criar schedule (one-time no futuro próximo)
docker exec ls-test awslocal scheduler create-schedule \
  --name test-schedule \
  --schedule-expression 'rate(1 minutes)' \
  --flexible-time-window 'Mode=OFF' \
  --target '{"Arn":"arn:aws:sqs:us-east-1:000000000000:scheduler-target","RoleArn":"arn:aws:iam::000000000000:role/scheduler-role","Input":"{\"scheduled\":true}"}'

# Verificar criação
docker exec ls-test awslocal scheduler get-schedule --name test-schedule
# Esperado: Schedule com target SQS e expressão rate

# Cleanup
docker exec ls-test awslocal scheduler delete-schedule --name test-schedule
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/scheduler-target
```

---

## 11. Lambda com Event Source Mapping (SQS → Lambda)

Este teste verifica o pipeline completo: mensagem em SQS aciona Lambda automaticamente.

```bash
# Criar fila de entrada e tabela de saída
docker exec ls-test awslocal sqs create-queue --queue-name esm-input
docker exec ls-test awslocal dynamodb create-table \
  --table-name esm-results \
  --attribute-definitions AttributeName=id,AttributeType=S \
  --key-schema AttributeName=id,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST

# Criar Lambda que processa SQS e grava em DynamoDB
docker exec -i ls-test python3 << 'PYEOF'
import zipfile
info = zipfile.ZipInfo('handler.py')
info.external_attr = 0o644 << 16
code = '''import json, os, boto3

def lambda_handler(event, context):
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    dynamodb = boto3.resource("dynamodb", endpoint_url=endpoint_url)
    table = dynamodb.Table(os.environ["TABLE_NAME"])
    for record in event["Records"]:
        body = json.loads(record["body"])
        table.put_item(Item={"id": body["id"], "status": "processed", "data": json.dumps(body)})
    return {"statusCode": 200}
'''
with zipfile.ZipFile('/tmp/esm_lambda.zip', 'w') as z:
    z.writestr(info, code)
PYEOF

docker exec ls-test awslocal lambda create-function \
  --function-name esm-processor \
  --runtime python3.12 \
  --handler handler.lambda_handler \
  --role arn:aws:iam::000000000000:role/test-role \
  --zip-file fileb:///tmp/esm_lambda.zip \
  --timeout 30 \
  --environment 'Variables={TABLE_NAME=esm-results}'

# Aguardar Active
docker exec ls-test awslocal lambda wait function-active-v2 --function-name esm-processor

# Criar event source mapping
docker exec ls-test awslocal lambda create-event-source-mapping \
  --function-name esm-processor \
  --event-source-arn arn:aws:sqs:us-east-1:000000000000:esm-input \
  --batch-size 1 \
  --enabled

# Enviar mensagem para a fila (aciona Lambda automaticamente)
docker exec ls-test awslocal sqs send-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/esm-input \
  --message-body '{"id":"esm-001","tipo":"auto-trigger"}'

# Aguardar processamento (30-90s no Windows por causa do cold start)
# Verificar resultado no DynamoDB
docker exec ls-test bash -c "sleep 60 && awslocal dynamodb get-item --table-name esm-results --key '{\"id\":{\"S\":\"esm-001\"}}'"
# Esperado: item com id=esm-001, status=processed

# Cleanup
docker exec ls-test awslocal lambda delete-function --function-name esm-processor
docker exec ls-test awslocal dynamodb delete-table --table-name esm-results
docker exec ls-test awslocal sqs delete-queue \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/esm-input
```

---

## 12. Verificação de Rede Lambda ↔ LocalStack

Este teste confirma que Lambda containers conseguem se comunicar com o LocalStack (crucial no Windows Docker Desktop).

```bash
# Criar Lambda que testa conectividade
docker exec -i ls-test python3 << 'PYEOF'
import zipfile
info = zipfile.ZipInfo('handler.py')
info.external_attr = 0o644 << 16
code = '''import os, json, boto3

def lambda_handler(event, context):
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    # Tenta criar SQS client e listar filas (prova conectividade)
    sqs = boto3.client("sqs", endpoint_url=endpoint_url)
    queues = sqs.list_queues()
    return {
        "endpoint": endpoint_url,
        "queue_count": len(queues.get("QueueUrls", [])),
        "connectivity": "OK"
    }
'''
with zipfile.ZipFile('/tmp/net_test.zip', 'w') as z:
    z.writestr(info, code)
PYEOF

docker exec ls-test awslocal lambda create-function \
  --function-name test-network \
  --runtime python3.12 \
  --handler handler.lambda_handler \
  --role arn:aws:iam::000000000000:role/test-role \
  --zip-file fileb:///tmp/net_test.zip \
  --timeout 30

docker exec ls-test awslocal lambda wait function-active-v2 --function-name test-network

docker exec ls-test bash -c 'awslocal lambda invoke --function-name test-network --payload "{}" /tmp/net_out.json && cat /tmp/net_out.json'
# Esperado: {"endpoint": "http://172.17.0.x:4566", "queue_count": N, "connectivity": "OK"}
# Se falhar com timeout → problema de rede entre Lambda container e LocalStack

# Cleanup
docker exec ls-test awslocal lambda delete-function --function-name test-network
```

---

## 13. Invocação Lambda a partir do Host (fora do container)

Testa a invocação via HTTP direto do host, simulando o que o C# SDK faz:

```bash
# Criar Lambda simples
docker exec -i ls-test python3 << 'PYEOF'
import zipfile
info = zipfile.ZipInfo('handler.py')
info.external_attr = 0o644 << 16
code = '''def lambda_handler(event, context):
    return {"echo": event, "source": "host-invocation"}
'''
with zipfile.ZipFile('/tmp/host_test.zip', 'w') as z:
    z.writestr(info, code)
PYEOF
docker exec ls-test awslocal lambda create-function \
  --function-name test-host-invoke \
  --runtime python3.12 \
  --handler handler.lambda_handler \
  --role arn:aws:iam::000000000000:role/test-role \
  --zip-file fileb:///tmp/host_test.zip

docker exec ls-test awslocal lambda wait function-active-v2 --function-name test-host-invoke

# Invocar do HOST via HTTP (como o C# SDK faz)
curl -X POST \
  -H 'Content-Type: application/json' \
  -d '{"test":"from-host"}' \
  http://localhost:4566/2015-03-31/functions/test-host-invoke/invocations
# Esperado: {"echo": {"test": "from-host"}, "source": "host-invocation"}

# Cleanup
docker exec ls-test awslocal lambda delete-function --function-name test-host-invoke
```

---

## 14. Remoção Completa

```bash
# Parar e remover o container LocalStack
docker rm -f ls-test

# Remover containers Lambda órfãos (criados pelo LocalStack)
docker ps -a --filter "name=ls-test-lambda" --format "{{.ID}}" | xargs -r docker rm -f

# Remover redes Docker órfãs
docker network prune -f

# Remover volumes órfãos
docker volume prune -f

# Verificar que tudo foi limpo
docker ps -a
docker network ls
# Deve mostrar apenas: bridge, host, none
```

### Remoção Profunda (opcional — remove IMAGENS também)

```bash
# Remove a imagem Lambda runtime (~516MB)
docker rmi public.ecr.aws/lambda/python:3.12

# Remove a imagem LocalStack (~1.26GB)
docker rmi localstack/localstack:3.8

# Verificar
docker images
```

---

## Notas Importantes

| Ponto                             | Detalhe                                                                                                                                           |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| **LAMBDA_DOCKER_NETWORK**         | NÃO definir. Deixar LocalStack auto-detectar a rede. Definir `bridge` quebra comunicação quando LocalStack roda em rede customizada (ex: Aspire). |
| **Cold start no Windows**         | Primeira invocação Lambda leva 30-90s (pull do container + inicialização runtime). Subsequentes são rápidas se o container persistir.             |
| **LAMBDA_REMOVE_CONTAINERS=true** | Remove containers Lambda após execução. Bom para cleanup, mas cada invocação faz cold start. Remover para reutilizar containers.                  |
| **AWS_ENDPOINT_URL**              | Injetado automaticamente pelo LocalStack nos containers Lambda. Valor típico: `http://172.17.0.x:4566`.                                           |
| **ZIP sem permissões**            | Criar o ZIP sem `ZipInfo.external_attr` causa `Permission denied` no runtime. Sempre use `info.external_attr = 0o644 << 16`.                      |
| **Paths /tmp no Git Bash**        | Git Bash converte `/tmp/file` para caminho Windows (MSYS). Use `bash -c '...'` para manter caminhos dentro do container.                          |
| **`!` em strings no bash**        | O `!` dispara history expansion no bash. Use heredoc `<< 'PYEOF'` para scripts Python com f-strings ou outros caracteres especiais.               |
| **Event Source Mapping**          | Polling pode levar 5-60s para acionar a primeira vez. No Windows Docker, cold start soma mais 30-60s. Total: até 120s.                            |
