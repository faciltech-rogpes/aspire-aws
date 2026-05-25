import json
import os

import boto3


def lambda_handler(event, context):
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    dynamodb = boto3.resource("dynamodb", endpoint_url=endpoint_url)
    sqs = boto3.client("sqs", endpoint_url=endpoint_url)
    tabela_regras = os.environ["TABELA_REGRAS"]

    tabela = dynamodb.Table(tabela_regras)
    resposta = tabela.scan()
    regras = {item["segmento"]: item["fila_destino"] for item in resposta["Items"]}

    for registro in event["Records"]:
        oferta = json.loads(registro["body"])
        segmento = oferta.get("segmento", "")
        fila_destino = regras.get(segmento)

        if not fila_destino:
            print(f"[ROTEADOR] AVISO: segmento '{segmento}' sem regra cadastrada — oferta descartada")
            continue

        sqs.send_message(QueueUrl=fila_destino, MessageBody=json.dumps(oferta))
        print(f"[ROTEADOR] Oferta {oferta['id']} roteada → segmento={segmento}")
