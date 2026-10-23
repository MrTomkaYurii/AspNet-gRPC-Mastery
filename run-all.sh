#!/usr/bin/env bash
# ==============================================================================
# gRPC-Complete-Mastery (.NET 10) - Повний запуск для Linux / macOS
# ==============================================================================
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

echo "======================================================================"
echo " 🚀 gRPC Complete Mastery: Запуск мікросервісного комплексу (.NET 10) "
echo "======================================================================"

echo ""
echo "[1/4] Запуск InventoryService (http://localhost:5001)..."
dotnet run --project "$DIR/src/InventoryService" > /dev/null 2>&1 &
INV_PID=$!

echo "[2/4] Запуск PricingService (http://localhost:5002)..."
dotnet run --project "$DIR/src/PricingService" > /dev/null 2>&1 &
PRICE_PID=$!

echo "[3/4] Запуск GatewayService (REST:5000, gRPC:5005)..."
dotnet run --project "$DIR/src/GatewayService" > /dev/null 2>&1 &
GW_PID=$!

cleanup() {
    echo ""
    echo "🛑 Зупинка фонових мікросервісів..."
    kill $INV_PID $PRICE_PID $GW_PID 2>/dev/null || true
    echo "✅ Усі сервіси зупинено."
}
trap cleanup EXIT INT TERM

echo ""
echo "⏳ Очікування ініціалізації Kestrel та SQLite (3 сек)..."
sleep 3

echo "[4/4] Запуск інтерактивного консольного клієнта ClientApp..."
echo "----------------------------------------------------------------------"
dotnet run --project "$DIR/src/ClientApp"
