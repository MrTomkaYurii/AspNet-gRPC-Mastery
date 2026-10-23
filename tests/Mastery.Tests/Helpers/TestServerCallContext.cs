using Grpc.Core;

namespace Mastery.Tests.Helpers;

/// <summary>
/// Тестовий двійник (Test Double) для абстрактного класу ServerCallContext.
/// Дозволяє тестувати gRPC-сервіси без запуску мережевого сервера Kestrel.
/// </summary>
public class TestServerCallContext : ServerCallContext
{
    private readonly Metadata _requestHeaders;
    private readonly CancellationToken _cancellationToken;
    private readonly Metadata _responseTrailers;
    private readonly AuthContext _authContext;
    private WriteOptions? _writeOptions;

    public TestServerCallContext(
        Metadata? requestHeaders = null,
        CancellationToken cancellationToken = default,
        DateTime? deadline = null)
    {
        _requestHeaders = requestHeaders ?? new Metadata();
        _cancellationToken = cancellationToken;
        DeadlineCore = deadline ?? DateTime.UtcNow.AddMinutes(5);
        _responseTrailers = new Metadata();
        _authContext = new AuthContext(null, new Dictionary<string, List<AuthProperty>>());
    }

    protected override string MethodCore => "/TestService/TestMethod";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "ipv4:127.0.0.1:50000";
    protected override DateTime DeadlineCore { get; }
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore => _responseTrailers;
    protected override Status StatusCore { get; set; } = Status.DefaultSuccess;
    protected override WriteOptions? WriteOptionsCore
    {
        get => _writeOptions;
        set => _writeOptions = value;
    }
    protected override AuthContext AuthContextCore => _authContext;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
