# ==============================================================================
# gRPC-Complete-Mastery (.NET 10) - Повний запуск навчального середовища
# ==============================================================================
# Цей скрипт запускає всі три мікросервіси у фонових процесах
# та відкриває інтерактивну консольну програму ClientApp для студента.
# ==============================================================================

$ErrorActionPreference = "Stop"
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " 🚀 gRPC Complete Mastery: Запуск навчального мікросервісного комплексу " -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan

$rootDir = $PSScriptRoot

Write-Host "`n[1/4] Запуск InventoryService (gRPC на http://localhost:5001)..." -ForegroundColor Green
$invProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$rootDir/src/InventoryService`"" -PassThru -WindowStyle Minimized

Write-Host "[2/4] Запуск PricingService (gRPC на http://localhost:5002)..." -ForegroundColor Green
$priceProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$rootDir/src/PricingService`"" -PassThru -WindowStyle Minimized

Write-Host "[3/4] Запуск GatewayService (Dual-Stack: REST:5000, gRPC:5005)..." -ForegroundColor Green
$gwProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$rootDir/src/GatewayService`"" -PassThru -WindowStyle Minimized

Write-Host "`n⏳ Очікування ініціалізації Kestrel та створення баз даних SQLite (3 сек)..." -ForegroundColor DarkGray
Start-Sleep -Seconds 3

Write-Host "[4/4] Запуск інтерактивного консольного клієнта ClientApp..." -ForegroundColor Cyan
Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkGray

try {
    # Запускаємо інтерактивну консоль у поточному вікні
    dotnet run --project "$rootDir/src/ClientApp"
}
finally {
    Write-Host "`n🛑 Зупинка фонових мікросервісів..." -ForegroundColor Yellow
    if ($invProcess -and -not $invProcess.HasExited) { Stop-Process -Id $invProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($priceProcess -and -not $priceProcess.HasExited) { Stop-Process -Id $priceProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($gwProcess -and -not $gwProcess.HasExited) { Stop-Process -Id $gwProcess.Id -Force -ErrorAction SilentlyContinue }
    Write-Host "✅ Усі сервіси коректно зупинено. Дякуємо!" -ForegroundColor Green
}
