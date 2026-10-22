using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GatewayService.Interceptors;

/// <summary>
/// Клієнтський перехоплювач (Client Interceptor):
/// Автоматично впроваджує заголовок 'x-correlation-id' та мітку часу
/// в метадані кожного вихідного gRPC-запиту до бекенд-сервісів.
/// </summary>
public class ClientTracingInterceptor : Interceptor
{
    private readonly ILogger<ClientTracingInterceptor> _logger;

    public ClientTracingInterceptor(ILogger<ClientTracingInterceptor> logger)
    {
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = context.Options.Headers ?? new Metadata();

        // Якщо кореляційний ID ще не встановлено, генеруємо новий
        if (headers.GetValue("x-correlation-id") is null)
        {
            var correlationId = Guid.NewGuid().ToString("N")[..12];
            headers.Add("x-correlation-id", correlationId);
        }

        headers.Add("x-client-timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        var newOptions = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);

        _logger.LogInformation("--> [gRPC Outbound Client Call] До {Host}{Path} | CorrelationId: {CorrelationId}",
            context.Host, context.Method.FullName, headers.GetValue("x-correlation-id"));

        return continuation(request, newContext);
    }
}
