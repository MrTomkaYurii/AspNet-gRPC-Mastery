using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GatewayService.Interceptors;

/// <summary>
/// Клієнтський перехоплювач (Client Interceptor) для наскрізного розподіленого трасування (Distributed Tracing).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// У мікросервісних архітектурах один користувацький клік може спричинити ланцюжок із десятків викликів між сервісами:
/// Client -> Gateway -> InventoryService / PricingService.
/// Якщо виникає помилка, знайти її в розрізнених логах неможливо без унікального наскрізного ідентифікатора (Correlation ID).
/// 
/// 'ClientTracingInterceptor' автоматично перехоплює ВСІ вихідні виклики згенерованих gRPC-клієнтів
/// ('InventoryServiceClient', 'PricingServiceClient') і:
/// 1. Перевіряє наявність заголовка 'x-correlation-id' у поточних 'CallOptions.Headers'.
/// 2. Якщо заголовок відсутній — генерує новий унікальний токен та додає його до метаданих.
/// 3. Додає мітку часу відправки запиту 'x-client-timestamp'.
/// 4. Логує факт виходу запиту із зазначенням хоста призначення.
/// Завдяки цьому бекенд-сервіси можуть прочитати цей заголовок і продовжити спільний ланцюжок логів.
/// </summary>
public class ClientTracingInterceptor : Interceptor
{
    private readonly ILogger<ClientTracingInterceptor> _logger;

    /// <summary>
    /// Конструктор клієнтського перехоплювача трасування.
    /// </summary>
    /// <param name="logger">Логер клієнтської підсистеми GatewayService.</param>
    public ClientTracingInterceptor(ILogger<ClientTracingInterceptor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Перехоплює асинхронні вихідні Unary RPC-виклики клієнта перед відправкою у сокет HTTP/2.
    /// </summary>
    /// <typeparam name="TRequest">Тип вихідного запиту.</typeparam>
    /// <typeparam name="TResponse">Тип очікуваної відповіді.</typeparam>
    /// <param name="request">Об'єкт повідомлення, що надсилається.</param>
    /// <param name="context">Контекст клієнтського виклику (дескриптор методу, хост, опції CallOptions).</param>
    /// <param name="continuation">Делегат виконання наступного етапу клієнтського конвеєра.</param>
    /// <returns>Дескриптор асинхронного виклику AsyncUnaryCall.</returns>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // КРОК 1: Отримуємо існуючу колекцію метаданих або ініціалізуємо нову
        var headers = context.Options.Headers ?? new Metadata();

        // КРОК 2: Забезпечуємо наявність наскрізного Correlation ID
        if (headers.GetValue("x-correlation-id") is null)
        {
            var correlationId = Guid.NewGuid().ToString("N")[..12];
            headers.Add("x-correlation-id", correlationId);
        }

        // КРОК 3: Додаємо мітку часу генерації запиту на клієнті в мілісекундах Unix Epoch
        headers.Add("x-client-timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        // КРОК 4: Створюємо оновлений незмінний контекст виклику із модифікованими заголовками
        var newOptions = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);

        _logger.LogInformation("--> [gRPC Outbound Client Call] До {Host}{Path} | CorrelationId: {CorrelationId}",
            context.Host, context.Method.FullName, headers.GetValue("x-correlation-id"));

        // КРОК 5: Передаємо керування транспорту Kestrel/SocketsHttpHandler
        return continuation(request, newContext);
    }
}
