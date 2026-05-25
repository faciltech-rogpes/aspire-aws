# 18 - ECS RunTask

## Tecnologias deste cenario

- [Amazon ECS](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/Welcome.html): servico de orquestracao de containers na AWS. Permite executar containers Docker sem gerenciar servidores.
- [Amazon SQS](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html): fila de mensagens. O worker consome pedidos publicados aqui.
- [PostgreSQL](https://www.postgresql.org/docs/): banco relacional onde o worker persiste os pedidos processados.
- [Docker](https://docs.docker.com/): runtime de containers. O worker `pedido_processor` eh construido e executado como container Docker real.
- [LocalStack](https://docs.localstack.cloud/): emula a API de controle do ECS (requer edicao Pro). SQS e PostgreSQL sao reais.

## Conceitos base deste cenario

- `Cluster ECS`: agrupamento logico de recursos onde as tasks rodam.
- `Task Definition`: especificacao do container — imagem, variaveis de ambiente, recursos. Equivale a um `docker run` declarativo.
- `RunTask`: comando que dispara a execucao de uma task em um cluster.
- `Worker de longa duracao`: container que fica rodando continuamente, consumindo mensagens de uma fila.
- `Dual-plane`: plano de controle (API ECS — Pro feature) separado do plano de dados (SQS + PostgreSQL — funcionam no Community).

## O que este cenario ensina

Este roteiro mostra a integracao entre ECS, SQS e PostgreSQL usando um worker Docker real:

```
Plano de controle (AWS SDK — Pro feature):
  CreateCluster -> RegisterTaskDefinition -> RunTask

Plano de dados (worker real):
  SQS.SendMessage -> worker Docker consome -> INSERT no PostgreSQL
```

O ponto central: mesmo sem a API ECS disponivel no Community, o worker Docker real processa as mensagens SQS e persiste no PostgreSQL. Os dois planos sao independentes.

## Conceitos em portugues simples

- [Amazon ECS](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/Welcome.html): servico que roda containers sem que voce gerencie os servidores por baixo.
- `Task Definition`: a "receita" de como o container deve rodar — imagem, env vars, etc.
- `RunTask`: o comando que coloca um container pra rodar no cluster.
- `Worker`: container que fica de pe esperando mensagens chegar na fila para processar.

## Como o cenario esta montado

O `Fixture`:

1. Aguarda o PostgreSQL aceitar conexoes
2. Constroi a imagem Docker `ecs-worker:latest` a partir de `src/tasks/pedido_processor/`
3. Inicia o worker como container Docker via CLI (`docker run -d`)
4. Aguarda o worker criar a tabela `pedidos` (prova de que esta pronto)
5. Cria a fila SQS `fila-pedidos`
6. Tenta criar cluster ECS e registrar task definition via API — se indisponivel (Community), registra e continua

Arquivo: [Fixture.cs](../../scenarios/18-ECS.RunTask/Fixture.cs)

Trecho de inicializacao do worker Docker:

```csharp
// scenarios/18-ECS.RunTask/Fixture.cs (trecho)
await RunDockerAsync(
    "run", "-d",
    "--name", containerName,
    "-e", "AWS_ENDPOINT_URL=http://host.docker.internal:4566",
    "-e", "DATABASE_URL=postgresql://test:test@host.docker.internal:5433/testdb",
    "-e", "FILA_PEDIDOS_URL=http://host.docker.internal:4566/000000000000/fila-pedidos",
    "ecs-worker:latest"
);
```

## O que o worker faz

Arquivo: [handler.py](../../src/tasks/pedido_processor/handler.py)

O worker e um processo Python continuo que:

1. Conecta ao PostgreSQL e cria a tabela `pedidos` (inicializacao)
2. Fica em loop consumindo mensagens da fila SQS via long polling
3. Para cada mensagem, insere um registro na tabela `pedidos` e deleta a mensagem da fila

```python
# src/tasks/pedido_processor/handler.py (trecho)
while True:
    response = sqs.receive_message(
        QueueUrl=fila_pedidos_url,
        MaxNumberOfMessages=10,
        WaitTimeSeconds=2,
    )
    for msg in response.get("Messages", []):
        pedido = json.loads(msg["Body"])
        cur.execute(
            "INSERT INTO pedidos (id, cliente, valor, status) VALUES (%s, %s, %s, %s)",
            (pedido["id"], pedido["cliente"], str(pedido["valor"]), "processado"),
        )
        conn.commit()
        sqs.delete_message(QueueUrl=fila_pedidos_url, ReceiptHandle=msg["ReceiptHandle"])
```

## O que os testes validam

Arquivo: [EcsRunTaskTests.cs](../../scenarios/18-ECS.RunTask/EcsRunTaskTests.cs)

### Plano de controle — 3 testes (skipped no Community)

| Teste | O que verifica |
|-------|---------------|
| `DescribeCluster_ShouldBeActive` | Cluster criado com status `ACTIVE` |
| `DescribeTaskDefinition_ShouldMatchRegisteredConfig` | Task definition contem familia, container e env vars corretos |
| `ListTaskDefinitions_ShouldIncludeRegisteredFamily` | Task definition aparece na listagem |

### Integracao real — 2 testes (passam sempre)

| Teste | O que verifica |
|-------|---------------|
| `RunTask_ShouldPersistOrderToDatabase` | Pedido publicado no SQS chega no PostgreSQL apos o worker processar |
| `RunTask_WithMultipleOrders_ShouldPersistAll` | Tres pedidos publicados em sequencia sao todos persistidos |

Os testes de integracao usam `PollingHelper.WaitUntilAsync` com timeout de 120s para aguardar o worker processar.

## Observacao importante

Os 3 testes de plano de controle estao marcados com `Skip`:

```csharp
[Fact(Skip = EnvironmentLimitations.LocalStackEcsApiReason)]
```

A mensagem: *"ECS control plane API (CreateCluster, RegisterTaskDefinition, RunTask) is a Pro feature — not available in LocalStack Community 3.8."*

Os 2 testes de integracao passam no Community porque o worker Docker real processa as mensagens SQS independentemente da API ECS.

## Passo a passo para rodar

1. Abra o Docker (necessario para o worker e para o LocalStack).
2. Rode:

```bash
dotnet test scenarios/18-ECS.RunTask/
```

3. Resultado esperado:

```
Total tests: 5
     Passed: 2   ← integracao SQS + PostgreSQL (worker real)
    Skipped: 3   ← plano de controle ECS (Pro feature)
```

## O que observar no resultado

- Os 2 testes que passam provam que o worker Docker real sobe, consome mensagens SQS e persiste no PostgreSQL — tudo dentro do ciclo de vida do xUnit.
- O fixture constroi a imagem (`docker build`) a cada execucao se necessario, usando cache do Docker quando possivel.
- Os 3 skipped mostram onde a API ECS seria usada em producao (ou com LocalStack Pro).

## Arquivos principais

- [Fixture.cs](../../scenarios/18-ECS.RunTask/Fixture.cs)
- [EcsRunTaskTests.cs](../../scenarios/18-ECS.RunTask/EcsRunTaskTests.cs)
- [handler.py](../../src/tasks/pedido_processor/handler.py) — worker Python
- [Dockerfile](../../src/tasks/pedido_processor/Dockerfile) — imagem do worker
