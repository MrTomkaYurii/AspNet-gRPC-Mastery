# 🚀 gRPC-Complete-Mastery (.NET 10)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![gRPC](https://img.shields.io/badge/gRPC-v2.84-244F5D?logo=grpc)](https://grpc.io/)
[![Protocol Buffers](https://img.shields.io/badge/Protobuf-proto3-blue)](https://protobuf.dev/)
[![EF Core](https://img.shields.io/badge/EF%20Core-SQLite-512BD4?logo=sqlite)](https://learn.microsoft.com/ef/core/)
[![HTTP/2](https://img.shields.io/badge/Protocol-HTTP%2F2%20(h2c)-blue)](https://http2.github.io/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> **Фундаментальний навчальний курс та інженерний полігон з розробки мікросервісних систем на gRPC в екосистемі ASP.NET Core 10.**  
> Охоплює повний шлях: від контрактного проєктування (Contract-First на proto3) і збереження даних в Entity Framework Core до всіх 3-х потокових режимів, гібридної архітектури REST-over-gRPC, патерну Aggregator (Scatter-Gather) та виробничої стійкості (Interceptors, Deadlines, Graceful Fallback).

---

## 🏛️ Архітектура навчального комплексу

Система моделює високошвидкісну платформу електронної комерції та логістики:

```mermaid
flowchart TB
    ClientCLI["💻 ClientApp (Консольний стенд студента)
Інтерактивне тестування 5-ти сценаріїв"]
    WebClient["🌐 Web Browser / Frontend (cURL / Fetch)
HTTP/1.1 REST Client"]

    subgraph HybridGateway ["🚪 GatewayService (:5000) - ASP.NET Core 10 Dual-Stack"]
        RestApi["REST Minimal APIs
GET /api/catalog/products/{sku}"]
        Aggregator["gRPC OrderAggregatorService
Scatter-Gather Composite DTO"]
        Interceptors["Interceptors
Logging & Tracing (x-correlation-id)"]
    end

    subgraph Mesh ["⚡ Внутрішня gRPC-мережа (HTTP/2 Cleartext)"]
        Inventory["📦 InventoryService (:5001)
gRPC Unary + Server Streaming
Складські залишки та резерв"]
        Pricing["💰 PricingService (:5002)
gRPC Client + Bi-di Streaming
Динамічні ціни та знижки"]
    end

    subgraph Persistence ["🗄️ Бази даних (EF Core SQLite)"]
        DbInv[("inventory.db
SQLite WAL")]
        DbPrice[("pricing.db
SQLite WAL")]
    end

    WebClient -->|"HTTP/1.1 JSON"| RestApi
    ClientCLI -->|"gRPC HTTP/2"| Aggregator
    ClientCLI -.->|"Direct gRPC"| Inventory
    ClientCLI -.->|"Direct gRPC"| Pricing

    RestApi -->|"GrpcClientFactory"| Inventory
    RestApi -->|"GrpcClientFactory"| Pricing
    Aggregator -->|"Parallel Task.WhenAll"| Inventory
    Aggregator -->|"Parallel Task.WhenAll"| Pricing

    Inventory --> DbInv
    Pricing --> DbPrice
```

---

## 📚 Навчальні модулі серії «Mastery»

Усі методичні матеріали розроблені у вигляді покрокових практичних посібників у папці [`docs/`](./docs/):

| Посібник | Академічна теорія та інженерні практики | Відповідні файли в коді |
| :--- | :--- | :--- |
| **[Mastery 01: Protobuf & Contracts](./docs/Mastery-01-Protobuf-Contracts.md)** | • Ретроспектива: від RPC/CORBA/SOAP до Protobuf<br>• Двійковий шар: Varints, ZigZag для від'ємних чисел, Tag-Length-Value (TLV)<br>• Побайтний розбір структури пакетів (Hex)<br>• Правила еволюції API, `reserved` та збереження невідомих полів (`UnknownFieldSet`) | [`Shared.Contracts`](./src/Shared.Contracts/)<br>• [`inventory.proto`](./src/Shared.Contracts/Protos/inventory.proto)<br>• [`pricing.proto`](./src/Shared.Contracts/Protos/pricing.proto)<br>• [`aggregator.proto`](./src/Shared.Contracts/Protos/aggregator.proto) |
| **[Mastery 02: Streaming In-Depth](./docs/Mastery-02-Streaming-In-Depth.md)** | • Мережева анатомія HTTP/2: 9-байтний заголовок фрейму, потоки, мультиплексування<br>• Алгоритм компресії заголовків HPACK (Static/Dynamic tables)<br>• Кредитний контроль потоку (Flow Control) та Backpressure (`WINDOW_UPDATE`)<br>• Усі 4 патерни взаємодії з діаграмами фреймів<br>• Deadlines та каскадне скасування через фрейм `RST_STREAM` | [`InventoryGrpcService.cs`](./src/InventoryService/Services/InventoryGrpcService.cs)<br>[`PricingGrpcService.cs`](./src/PricingService/Services/PricingGrpcService.cs)<br>[`02_StreamingScenario.cs`](./src/ClientApp/Scenarios/02_StreamingScenario.cs) |
| **[Mastery 03: EF Core Integration](./docs/Mastery-03-EFCore-Integration.md)** | • Потокобезпека та ізоляція `IServiceScope` при мультиплексуванні HTTP/2<br>• Чому `DbContext` зобов'язаний бути Scoped<br>• Оптимізація пам'яті через `.AsNoTracking()` та нульові алокації (Zero Allocations)<br>• Каскад скасування від клієнта до драйвера SQLite/Postgres через `CancellationToken`<br>• Транзакційне резервування, стан гонитви (Race Conditions) та матриця мапінгу винятків у `RpcException` | [`InventoryDbContext.cs`](./src/InventoryService/Data/InventoryDbContext.cs)<br>[`PricingDbContext.cs`](./src/PricingService/Data/PricingDbContext.cs)<br>[`InventoryGrpcService.cs`](./src/InventoryService/Services/InventoryGrpcService.cs) |
| **[Mastery 04: REST Meets gRPC](./docs/Mastery-04-REST-vs-gRPC-Hybrid.md)** | • Інженерне порівняння REST проти gRPC (бенчмарки CPU/пам'яті)<br>• Архітектура North-South (зовнішній інтернет) проти East-West (датацентр)<br>• Мережева причина помилки `HTTP_1_1_REQUIRED (0xd)` у Cleartext HTTP/2 без ALPN<br>• Налаштування Dual-Stack Kestrel (порт 5000 REST + порт 5005 gRPC)<br>• Пулінг каналів через `GrpcClientFactory` (захист від Socket Exhaustion)<br>• Трансляція `RpcException` у HTTP ProblemDetails | [`GatewayService/Program.cs`](./src/GatewayService/Program.cs)<br>[`CatalogEndpoints.cs`](./src/GatewayService/Endpoints/CatalogEndpoints.cs)<br>[`03_RestGatewayScenario.cs`](./src/ClientApp/Scenarios/03_RestGatewayScenario.cs) |
| **[Mastery 05: The Aggregator Pattern](./docs/Mastery-05-Aggregator-Pattern.md)** | • Проблема балакучих клієнтів (Chatty Clients) у мобільних мережах<br>• Математичне доведення латентності Scatter-Gather: $T_{\text{par}} = \max(T_1, T_2)$ проти $T_{\text{seq}} = T_1 + T_2$<br>• Реалізація Scatter-Gather через `Task.WhenAll` та `AsyncUnaryCall.ResponseAsync`<br>• Каскадне прокидання Deadlines крізь мікросервісний граф<br>• Стратегія часткових збоїв (Hard vs Soft Dependencies) та Fallback Pricing<br>• Перехоплювачі (Interceptors): серверний аудит часу та клієнтський `x-correlation-id` | [`OrderAggregatorService.cs`](./src/GatewayService/Services/OrderAggregatorService.cs)<br>[`ServerLoggingInterceptor.cs`](./src/GatewayService/Interceptors/ServerLoggingInterceptor.cs)<br>[`ClientTracingInterceptor.cs`](./src/GatewayService/Interceptors/ClientTracingInterceptor.cs) |

---

## 🔌 Порти та топологія сервісів

| Сервіс | Адреса | Протоколи | Призначення |
| :--- | :--- | :--- | :--- |
| **`GatewayService` (REST)** | `http://localhost:5000` | HTTP/1.1 | Гібридний REST-фасад для вебу та браузерів |
| **`GatewayService` (gRPC)** | `http://localhost:5005` | HTTP/2 (h2c) | Чистий gRPC OrderAggregator (Composite Service) |
| **`InventoryService`** | `http://localhost:5001` | HTTP/2 (h2c) | Чистий gRPC: облік та аудит залишків складу |
| **`PricingService`** | `http://localhost:5002` | HTTP/2 (h2c) | Чистий gRPC: ціноутворення, bulk знижки, торги |
| **`ClientApp`** | CLI Console | HTTP/1.1 + HTTP/2 | Інтерактивний полігон студента (сценарії 01–05) |

---

## ⚡ Швидкий старт (Quick Start)

### 1. Клонування та збірка рішення
```bash
git clone https://github.com/MrTomkaYurii/gRPC-Complete-Mastery.git
cd gRPC-Complete-Mastery
dotnet build
```

### 2. Запуск сервісів (у трьох окремих терміналах або фоном)

**Термінал 1: Сервіс складу**
```bash
dotnet run --project src/InventoryService
```

**Термінал 2: Сервіс цін**
```bash
dotnet run --project src/PricingService
```

**Термінал 3: Гібридний Шлюз & Агрегатор**
```bash
dotnet run --project src/GatewayService
```

*Примітка: SQLite бази даних (`inventory.db` та `pricing.db`) створюються та наповнюються тестовими даними автоматично при першому старті.*

---

### 3. Запуск клієнтського інтерактивного стенду

**Термінал 4: Запуск консольного стенду студента**
```bash
dotnet run --project src/ClientApp
```

Ви побачите інтерактивне меню:
```text
======================================================================
 🚀 gRPC Complete Mastery (.NET 10) - Interactive Student Test Bench  
======================================================================
 [1] Сценарій 01: Unary RPC & Обробка статус-кодів RpcException
 [2] Сценарій 02: Потокові режими: Server, Client та Bi-directional
 [3] Сценарій 03: Гібридний підхід: REST-over-gRPC через HttpClient
 [4] Сценарій 04: Патерн Aggregator: Scatter-Gather та Composite DTO
 [5] Сценарій 05: Deadlines, Каскадне скасування та Метадані
 [A] Запустити ВСІ сценарії послідовно
 [0] Вихід із програми
======================================================================
```

Або запустити всі тести в неінтерактивному автоматичному режимі:
```bash
dotnet run --project src/ClientApp -- --all
```

---

## 🧪 Тестування REST-ендпоінта через cURL або браузер

Оскільки `GatewayService` реалізує концепцію **Dual-Stack**, ви можете протестувати REST фасад будь-яким HTTP-клієнтом:

```bash
# Отримання агрегованої інформації про товар у форматі JSON
curl -X GET "http://localhost:5000/api/catalog/products/LAPTOP-PRO-16?tier=VIP&qty=2"
```

Приклад JSON-відповіді:
```json
{
  "source": "Hybrid REST-over-gRPC Gateway",
  "sku": "LAPTOP-PRO-16",
  "productName": "Apple MacBook Pro 16 (M3 Max / 36GB / 1TB)",
  "warehouse": "WH-KYIV-01",
  "stockStatus": "IN_STOCK",
  "availableUnits": 43,
  "pricing": {
    "basePrice": 2499,
    "finalUnitPrice": 2124.15,
    "discountPercent": 15,
    "campaign": "Tier: VIP (+10%), Bulk: (+0%)",
    "currency": "USD",
    "calculatedTotal": 4248.3
  }
}
```

---

## 🛠️ Використаний технологічний стек
* **Платформа:** .NET 10.0 (C# 14, Roslyn compiler)
* **RPC фреймворк:** `Grpc.AspNetCore 2.84`, `Grpc.Net.Client 2.84`, `Grpc.Tools 2.84`
* **Серіалізація:** `Google.Protobuf 3.36`
* **ORM & Бази даних:** `Microsoft.EntityFrameworkCore.Sqlite 10.0`
* **Мережевий транспорт:** HTTP/2 Cleartext (`h2c`) з підтримкою multiplexing & streaming
