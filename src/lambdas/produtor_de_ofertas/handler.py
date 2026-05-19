import json
import os
import uuid
from datetime import datetime, timezone

import boto3


def lambda_handler(event, context):
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    sqs = boto3.client("sqs", endpoint_url=endpoint_url)
    fila_ofertas_url = os.environ["FILA_OFERTAS_URL"]

    # usa dados do evento se fornecidos (invocação via testes); caso contrário gera oferta aleatória (scheduler)
    oferta = {
        "id": event.get("id", f"oferta-{uuid.uuid4().hex[:8]}"),
        "segmento": event.get("segmento", "consignado"),
        "taxa": event.get("taxa", 1.2),
        "valor": event.get("valor", 10000.0),
        "criado_em": datetime.now(timezone.utc).isoformat(),
    }
    sqs.send_message(
        QueueUrl=fila_ofertas_url,
        MessageBody=json.dumps(oferta),
    )
    print(f"[PRODUTOR] Oferta publicada: {oferta['id']} | segmento={oferta['segmento']}")
    return {"statusCode": 200, "oferta_id": oferta["id"]}
