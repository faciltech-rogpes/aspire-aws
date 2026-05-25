import json
import os
import time

import boto3
import psycopg2


def connect_with_retry(database_url: str, retries: int = 30, delay: int = 2):
    """Tenta conectar ao PostgreSQL com retries — o container pode demorar para subir."""
    for attempt in range(retries):
        try:
            conn = psycopg2.connect(database_url)
            conn.autocommit = False
            print(f"[WORKER] Conectado ao PostgreSQL (tentativa {attempt + 1}).")
            return conn
        except psycopg2.OperationalError as e:
            print(f"[WORKER] Aguardando PostgreSQL... ({attempt + 1}/{retries}): {e}")
            time.sleep(delay)
    raise RuntimeError(f"PostgreSQL não disponível após {retries} tentativas.")


def main():
    endpoint_url     = os.environ.get("AWS_ENDPOINT_URL")
    database_url     = os.environ["DATABASE_URL"]
    fila_pedidos_url = os.environ["FILA_PEDIDOS_URL"]

    sqs  = boto3.client("sqs", endpoint_url=endpoint_url)
    conn = connect_with_retry(database_url)

    # Cria tabela se não existir — este worker é o responsável pelo schema de pedidos
    with conn.cursor() as cur:
        cur.execute("""
            CREATE TABLE IF NOT EXISTS pedidos (
                id           TEXT PRIMARY KEY,
                cliente      TEXT NOT NULL,
                valor        NUMERIC(10,2) NOT NULL,
                status       TEXT NOT NULL,
                processado_em TIMESTAMPTZ DEFAULT NOW()
            )
        """)
    conn.commit()
    print("[WORKER] Tabela 'pedidos' pronta.")

    print(f"[WORKER] Iniciando poll da fila '{fila_pedidos_url}'...")

    while True:
        try:
            response = sqs.receive_message(
                QueueUrl=fila_pedidos_url,
                MaxNumberOfMessages=10,
                WaitTimeSeconds=2,
            )
            for msg in response.get("Messages", []):
                pedido = json.loads(msg["Body"])
                try:
                    # Reconecta se a conexão PostgreSQL estiver morta
                    if conn.closed:
                        print("[WORKER] Conexão PostgreSQL fechada — reconectando...")
                        conn = connect_with_retry(database_url)
                    with conn.cursor() as cur:
                        cur.execute(
                            """
                            INSERT INTO pedidos (id, cliente, valor, status)
                            VALUES (%s, %s, %s, %s)
                            ON CONFLICT (id) DO NOTHING
                            """,
                            (pedido["id"], pedido["cliente"],
                             str(pedido["valor"]), "processado"),
                        )
                    conn.commit()
                    sqs.delete_message(
                        QueueUrl=fila_pedidos_url,
                        ReceiptHandle=msg["ReceiptHandle"],
                    )
                    print(f"[WORKER] Pedido {pedido['id']} processado → cliente={pedido['cliente']}")
                except psycopg2.OperationalError as db_err:
                    print(f"[WORKER] Erro de conexão PostgreSQL — reconectando: {db_err}")
                    try:
                        conn.rollback()
                    except Exception:
                        pass
                    try:
                        conn = connect_with_retry(database_url, retries=5, delay=1)
                    except Exception:
                        pass
                except psycopg2.Error as db_err:
                    print(f"[WORKER] Erro PostgreSQL ao processar pedido: {db_err}")
                    try:
                        conn.rollback()
                    except Exception:
                        pass
        except Exception as e:
            # Fila ainda não criada (fixture em inicialização) ou erro transitório
            print(f"[WORKER] Erro SQS (aguardando fila): {e}")
            time.sleep(2)


if __name__ == "__main__":
    main()
