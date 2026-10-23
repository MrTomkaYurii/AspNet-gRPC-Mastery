using GatewayService.Interceptors;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mastery.Tests;

/// <summary>
/// Тести для клієнтського перехоплювача ClientTracingInterceptor.
/// Перевіряють наскрізне впровадження заголовків x-correlation-id та x-client-timestamp.
/// </summary>
public class InterceptorTests
{
    [Fact]
    public void ClientTracingInterceptor_WhenNoCorrelationId_InjectsGeneratedId()
    {
        // Arrange
        var interceptor = new ClientTracingInterceptor(NullLogger<ClientTracingInterceptor>.Instance);
        var headers = new Metadata();

        var method = new Method<string, string>(
            MethodType.Unary,
            "TestService",
            "TestMethod",
            Marshallers.StringMarshaller,
            Marshallers.StringMarshaller);

        var callOptions = new CallOptions(headers: headers);
        var context = new ClientInterceptorContext<string, string>(method, "localhost", callOptions);

        Metadata? capturedHeaders = null;

        Interceptor.AsyncUnaryCallContinuation<string, string> continuation = (req, ctx) =>
        {
            capturedHeaders = ctx.Options.Headers;
            return new AsyncUnaryCall<string>(
                Task.FromResult("result"),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        };

        // Act
        interceptor.AsyncUnaryCall("test-request", context, continuation);

        // Assert
        Assert.NotNull(capturedHeaders);
        Assert.NotNull(capturedHeaders.GetValue("x-correlation-id"));
        Assert.NotNull(capturedHeaders.GetValue("x-client-timestamp"));
    }

    [Fact]
    public void ClientTracingInterceptor_WhenCorrelationIdAlreadyExists_PreservesExistingId()
    {
        // Arrange
        var interceptor = new ClientTracingInterceptor(NullLogger<ClientTracingInterceptor>.Instance);
        var headers = new Metadata
        {
            { "x-correlation-id", "EXISTING-TRACE-ID-999" }
        };

        var method = new Method<string, string>(
            MethodType.Unary,
            "TestService",
            "TestMethod",
            Marshallers.StringMarshaller,
            Marshallers.StringMarshaller);

        var callOptions = new CallOptions(headers: headers);
        var context = new ClientInterceptorContext<string, string>(method, "localhost", callOptions);

        Metadata? capturedHeaders = null;

        Interceptor.AsyncUnaryCallContinuation<string, string> continuation = (req, ctx) =>
        {
            capturedHeaders = ctx.Options.Headers;
            return new AsyncUnaryCall<string>(
                Task.FromResult("result"),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        };

        // Act
        interceptor.AsyncUnaryCall("test-request", context, continuation);

        // Assert
        Assert.NotNull(capturedHeaders);
        Assert.Equal("EXISTING-TRACE-ID-999", capturedHeaders.GetValue("x-correlation-id"));
    }
}
