using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GatewayService.Interceptors;

/// <summary>
/// Серверний перехоплювач (Server Interceptor):
/// Логує всі вхідні gRPC-запити, вимірює час виконання та витягує Correlation-ID.
/// </summary>
public class ServerLoggingInterceptor : Interceptor
{
    private readonly ILogger<ServerLoggingInterceptor> _logger;

    public ServerLoggingInterceptor(ILogger<ServerLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var correlationId = context.RequestHeaders.GetValue("x-correlation-id") ?? Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("==> [gRPC Inbound] {Method} | CorrelationId: {CorrelationId} | Peer: {Peer}",
            context.Method, correlationId, context.Peer);

        try
        {
            var response = await continuation(request, context);
            stopwatch.Stop();

            _logger.LogInformation("<== [gRPC Success] {Method} | {ElapsedMs} ms | StatusCode: OK",
                context.Method, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (RpcException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "<== [gRPC RpcException] {Method} | {ElapsedMs} ms | StatusCode: {StatusCode} | Detail: {Detail}",
                context.Method, stopwatch.ElapsedMilliseconds, ex.StatusCode, ex.Status.Detail);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "<== [gRPC Unhandled Error] {Method} | {ElapsedMs} ms",
                context.Method, stopwatch.ElapsedMilliseconds);
            throw new RpcException(new Status(StatusCode.Internal, "Внутрішня помилка сервера при обробці gRPC запиту."));
        }
    }
}
