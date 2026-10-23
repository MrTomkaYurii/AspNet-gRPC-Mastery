using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GatewayService.Interceptors;

/// <summary>
/// Серверний перехоплювач (Server Interceptor) для аудиту та моніторингу вхідних gRPC-запитів.
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Перехоплювачі (Interceptors) у gRPC — це реалізація патерну "Chain of Responsibility" (Ланцюжок обов'язків)
/// та аспектно-орієнтованого програмування (AOP). Вони є прямим gRPC-аналогом Middleware у звичайному ASP.NET Core,
/// але оперують специфічними типами gRPC:
/// - 'TRequest' та 'TResponse' (типізовані Protobuf DTO замість сирого HTTP-тіла);
/// - 'ServerCallContext' (HTTP/2 метадані, трейлери, дедлайни замість HttpContext).
/// 
/// Цей перехоплювач виконує три критичні виробничі функції:
/// 1. Вилучення або генерація ідентифікатора наскрізного трасування ('x-correlation-id').
/// 2. Високоточне профілювання часу виконання RPC за допомогою 'Stopwatch'.
/// 3. Безпечне перехоплення некерованих винятків (Unhandled Exceptions) із трансляцією у стандартний 'StatusCode.Internal',
///    що захищає внутрішні деталі реалізації сервера (stack trace) від витоку назовні.
/// </summary>
public class ServerLoggingInterceptor : Interceptor
{
    private readonly ILogger<ServerLoggingInterceptor> _logger;

    /// <summary>
    /// Конструктор перехоплювача з впровадженням системного логера.
    /// </summary>
    /// <param name="logger">Екземпляр логера Kestrel/ASP.NET Core.</param>
    public ServerLoggingInterceptor(ILogger<ServerLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Перехоплює виконання будь-якого Unary RPC-виклику на боці сервера.
    /// </summary>
    /// <typeparam name="TRequest">Тип вхідного Protobuf-повідомлення.</typeparam>
    /// <typeparam name="TResponse">Тип вихідного Protobuf-повідомлення.</typeparam>
    /// <param name="request">Десеріалізований об'єкт вхідного запиту.</param>
    /// <param name="context">Контекст виклику (метадані, стан каналу, дедлайни).</param>
    /// <param name="continuation">Делегат, що викликає наступний перехоплювач або сам цільовий метод сервісу.</param>
    /// <returns>Результат виконання виклику (Protobuf-відповідь).</returns>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // КРОК 1: Читання заголовків з HTTP/2 HEADERS фрейму
        // У gRPC метадані передаються як пари ключ-значення у форматі ASCII або двійковому (з суфіксом '-bin')
        var correlationId = context.RequestHeaders.GetValue("x-correlation-id") ?? Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("==> [gRPC Inbound] Метод: {Method} | CorrelationId: {CorrelationId} | Peer: {Peer}",
            context.Method, correlationId, context.Peer);

        try
        {
            // КРОК 2: Передача керування далі по конвеєру (до реалізації сервісу)
            var response = await continuation(request, context);
            stopwatch.Stop();

            _logger.LogInformation("<== [gRPC Success] Метод: {Method} | Тривалість: {ElapsedMs} ms | StatusCode: OK",
                context.Method, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (RpcException ex)
        {
            // Очікуваний бізнес-виняток gRPC (наприклад NotFound або InvalidArgument) — логуємо як попередження
            stopwatch.Stop();
            _logger.LogWarning(ex, "<== [gRPC RpcException] Метод: {Method} | {ElapsedMs} ms | StatusCode: {StatusCode} | Причина: {Detail}",
                context.Method, stopwatch.ElapsedMilliseconds, ex.StatusCode, ex.Status.Detail);
            throw;
        }
        catch (Exception ex)
        {
            // Несподіваний збій (NullReferenceException, Database Timeout тощо) — логуємо як критичну помилку
            stopwatch.Stop();
            _logger.LogError(ex, "<== [gRPC Unhandled Error] Метод: {Method} | {ElapsedMs} ms | Фатальний збій!",
                context.Method, stopwatch.ElapsedMilliseconds);

            // Маскуємо внутрішній стек викликів від клієнта для забезпечення безпеки
            throw new RpcException(new Status(StatusCode.Internal, "Внутрішня помилка сервера при обробці gRPC запиту."));
        }
    }
}
