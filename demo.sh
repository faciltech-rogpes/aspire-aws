#!/usr/bin/env bash
# Sobe LocalStack + Aspire Dashboard e fica no ar até Ctrl+C.
# Em outro terminal, rode os testes para ver atividade no dashboard.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DASHBOARD_PORT="${DASHBOARD_PORT:-18888}"
OTLP_PORT="${OTLP_PORT:-18889}"

# Aspire 13 requer estas variáveis para iniciar o dashboard em modo standalone
export ASPNETCORE_URLS="http://localhost:${DASHBOARD_PORT}"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:${OTLP_PORT}"
export ASPIRE_ALLOW_UNSECURED_TRANSPORT="true"
export DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL="http://localhost:${OTLP_PORT}"

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║           aspire-aws  —  modo demonstração               ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "  Dashboard  →  http://localhost:${DASHBOARD_PORT}"
echo "  OTLP       →  http://localhost:${OTLP_PORT}"
echo ""
echo "  Em outro terminal, rode os testes:"
echo "    dotnet test scenarios/03-DynamoDB.Basic/"
echo "    dotnet test scenarios/16-Pipeline.Scheduler.Router/"
echo ""
echo "  Pressione Ctrl+C para parar tudo."
echo ""

cleanup() {
    echo ""
    echo "  Encerrando Aspire e removendo container LocalStack..."
    docker rm -f aspire-aws-localstack 2>/dev/null || true
    echo "  Encerrado."
}
trap cleanup EXIT INT TERM

dotnet run --project "$ROOT/src/AppHost/" "$@"
