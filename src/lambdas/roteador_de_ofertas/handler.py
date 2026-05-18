import json
import os

import boto3

endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
dynamodb = boto3.resource("dynamodb", endpoint_url=endpoint_url)
sqs = boto3.client("sqs", endpoint_url=endpoint_url)
tabela_regras = os.environ["TABELA_REGRAS"]

# inicialização a frio: carrega regras uma vez e cacheia em memória
_tabela = dynamodb.Table(tabela_regras)
_resposta = _tabela.scan()
_regras: dict = {item["segmento"]: item["fila_destino"] for item in _resposta["Items"]}


def handler(event, context):
    for registro in event["Records"]:
        oferta = json.loads(registro["body"])
        segmento = oferta.get("segmento", "")
        fila_destino = _regras.get(segmento)

        if not fila_destino:
            print(f"[ROTEADOR] AVISO: segmento '{segmento}' sem regra cadastrada — oferta descartada")
            continue

        sqs.send_message(QueueUrl=fila_destino, MessageBody=json.dumps(oferta))
        print(f"[ROTEADOR] Oferta {oferta['id']} roteada → segmento={segmento}")
