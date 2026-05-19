import json


def lambda_handler(event, context):
    for registro in event["Records"]:
        oferta = json.loads(registro["body"])
        print(
            f"[OFERTA] id={oferta['id']} | "
            f"segmento={oferta['segmento']} | "
            f"taxa={oferta['taxa']}% | "
            f"valor=R${oferta['valor']:.0f}"
        )
