# 📜 Mastery 01: Protobuf & Contracts Foundation (Contract-First Design)

Ласкаво просимо до першого модуля серії **gRPC Mastery на .NET 10**.

У сучасній мікросервісній архітектурі існує два підходи до розробки API:
1. **Code-First** (типовий для класичного ASP.NET Web API) — ви пишете C#-контролери або моделі, а Swagger чи OpenAPI генеруються як похідний артефакт.
2. **Contract-First** (стандарт індустрії для gRPC) — ви спочатку проєктуєте незалежний, строго типізований контракт на мові **Protocol Buffers (proto3)**, і вже на його основі компілятори різних мов (C#, Go, Python, Java, Rust) автоматично генерують клієнтські та серверні класи (stubs).

---

## 1. Анатомія файлу `.proto` (proto3)

Розглянемо контракт нашого сервісу складу [`inventory.proto`](../src/Shared.Contracts/Protos/inventory.proto):

```protobuf
syntax = "proto3";

package mastery.inventory.v1;

option csharp_namespace = "Mastery.Inventory.V1";

import "google/protobuf/timestamp.proto";

service InventoryService {
  rpc GetStockItem (StockItemRequest) returns (StockItemResponse);
  rpc ReserveStock (ReserveStockRequest) returns (ReserveStockResponse);
  rpc StreamStockUpdates (StockAuditFilter) returns (stream StockItemResponse);
}

message StockItemRequest {
  string sku = 1;
}

message StockItemResponse {
  string sku = 1;
  string product_name = 2;
  int32 available_quantity = 3;
  int32 reserved_quantity = 4;
  string warehouse_code = 5;
  StockStatus status = 6;
  google.protobuf.Timestamp last_restocked_at = 7;
}

enum StockStatus {
  STOCK_STATUS_UNSPECIFIED = 0;
  IN_STOCK = 1;
  LOW_STOCK = 2;
  OUT_OF_STOCK = 3;
  DISCONTINUED = 4;
}
```

### Ключові елементи:
* `syntax = "proto3";` — версія синтаксису. Версія proto3 спростила мову, прибравши заплутані директиви `required`/`optional` старого proto2 (у proto3 всі поля є опціональними за замовчуванням).
* `package mastery.inventory.v1;` — логічний простір імен у світі Protobuf. Додавання версії `v1` — найкраща практика для запобігання конфліктам імен.
* `option csharp_namespace = "...";` — задає простір імен C#, у якому генератор `Grpc.Tools` створить класи.

---

## 2. Магія бінарної серіалізації: Теги полів (Field Numbers)

Зверніть увагу на цифри після полів:
```protobuf
string sku = 1;
string product_name = 2;
int32 available_quantity = 3;
```

> [!IMPORTANT]
> Числа `= 1`, `= 2` — це **НЕ значення за замовчуванням** і **НЕ порядок у базі даних**! Це **унікальні числові теги (Field Tags)**, які записуються в бінарний потік.

### Як це працює у двійковому форматі:
У класичному REST/JSON пакет передає імена ключів повністю:
```json
{"sku":"LAPTOP-PRO-16","product_name":"MacBook Pro 16","available_quantity":45}
```
Тут витрачається понад 70 байтів лише на текстові назви полів `sku`, `product_name`, `available_quantity` та фігурні дужки.

У Protobuf кожне поле кодується як **[Field Tag + Wire Type] + [Value]**:
* Замість тексту `"available_quantity"` передається лише **1 байт**: `(3 << 3) | 0` (номер поля 3, тип `varint`).
* **Поля з номерами від 1 до 15** займають у бінарному форматі лише **1 байт** для тегу!
* **Поля від 16 до 2047** займають **2 байти**.

> [!TIP]
> Найбільш часто використовувані поля повідомлення завжди нумеруйте від `1` до `15`, щоб досягти максимальної компресії трафіку.

---

## 3. Таблиця відповідності типів Protobuf $\leftrightarrow$ .NET 10

| Тип у `.proto` | Тип у C# (.NET 10) | Примітки |
| :--- | :--- | :--- |
| `int32` | `int` | Кодується як `varint` (ефективно для малих додатних чисел) |
| `int64` | `long` | 64-бітне ціле число |
| `double` | `double` | 64-бітне число з плаваючою крапкою IEEE 754 |
| `float` | `float` | 32-бітне число з плаваючою крапкою |
| `bool` | `bool` | 1 байт (`0` або `1`) |
| `string` | `string` | Завжди UTF-8 або 7-бітний ASCII |
| `bytes` | `Google.Protobuf.ByteString` | Довільний масив бінарних даних |
| `enum` | C# `enum` | **Перше значення обов'язково має дорівнювати 0** (значення за замовчуванням) |
| `google.protobuf.Timestamp` | `Google.Protobuf.WellKnownTypes.Timestamp` | UTC час із точністю до наносекунд. Легко конвертується: `.ToDateTimeOffset()` / `Timestamp.FromDateTime(dt)` |

---

## 4. Золоті правила еволюції контрактів (Backward / Forward Compatibility)

У реальному продакшені клієнти та сервери оновлюються в різний час. Protobuf спроєктований так, щоб стара версія клієнта могла без збоїв спілкуватися з новою версією сервера:

1. **НІКОЛИ не змінюйте числовий тег існуючого поля.** Якщо поле `string sku = 1;` вже опубліковане, воно завжди має залишатися `= 1`.
2. **НЕ перейменовуйте концептуальний сенс поля.** Ви можете змінити назву в proto-файлі (бо назва не передається мережею), але якщо замість `sku` ви почнете передавати `user_id` під тим самим тегом `= 1`, старі клієнти прочитають некоректні дані.
3. **Видалення полів через `reserved`:** Якщо поле більше не потрібне, видаліть його та зарезервуйте номер, щоб ніхто випадково не використав його повторно:
   ```protobuf
   message StockItemResponse {
     reserved 4, 8 to 10;
     reserved "old_field_name";
   }
   ```
4. **Невідомі поля (Unknown Fields):** Якщо новий сервер надішле старішому клієнту нове поле `= 8`, клієнт не впаде з помилкою парсингу, а просто збереже це поле в буфері невідомих полів і проігнорує його в бізнес-логіці.

---

## 5. Архітектура «Single Source of Truth» в .NET 10

У нашому рішенні створено спільну бібліотеку [`Shared.Contracts`](../src/Shared.Contracts/Shared.Contracts.csproj). 

Конфігурація MSBuild:
```xml
<ItemGroup>
  <Protobuf Include="Protos\inventory.proto" GrpcServices="Both" ProtoRoot="Protos" />
  <Protobuf Include="Protos\pricing.proto" GrpcServices="Both" ProtoRoot="Protos" />
  <Protobuf Include="Protos\aggregator.proto" GrpcServices="Both" ProtoRoot="Protos" />
</ItemGroup>
```

Параметр `GrpcServices="Both"` означає, що компілятор згенерує:
* **Server Base Class:** наприклад, `InventoryService.InventoryServiceBase`, який ми успадковуємо у сервісах ASP.NET Core (`InventoryService`).
* **Client Class:** наприклад, `InventoryService.InventoryServiceClient`, який ми інжектуємо у викликах з `GatewayService` та `ClientApp`.

Склавши проект `Shared.Contracts`, усі залежні проєкти одразу отримують доступ до суворо типізованих C#-класів без жодного ручного копіювання файлів.
