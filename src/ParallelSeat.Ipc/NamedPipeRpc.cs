using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ParallelSeat.Protocol;

namespace ParallelSeat.Ipc;

public static class PipeSecurityFactory
{
    public static PipeSecurity CreateSameUserPipeSecurity()
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent();
        if (identity.User is null)
        {
            throw new InvalidOperationException("Unable to resolve current user SID.");
        }

        // Allow only the interactive user (and SYSTEM for service scenarios).
        // Do not add Deny(Everyone): that also denies the current user.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            identity.User,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    public static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        return ProtocolConstants.PipeNamePrefix + sid;
    }
}

public sealed class LengthPrefixedFraming
{
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > ProtocolConstants.MaxMessageBytes)
        {
            throw new InvalidOperationException("Message exceeds max size.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > ProtocolConstants.MaxMessageBytes)
        {
            throw new InvalidOperationException($"Invalid frame length {length}.");
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}

public delegate Task<JsonRpcResponse> RpcHandler(JsonRpcRequest request, CancellationToken cancellationToken);

public sealed class NamedPipeRpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly string _token;
    private readonly RpcHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public NamedPipeRpcServer(string pipeName, string token, RpcHandler handler)
    {
        _pipeName = pipeName;
        _token = token;
        _handler = handler;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var security = PipeSecurityFactory.CreateSameUserPipeSecurity();
            var server = NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                security);

            try
            {
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(server), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        await using (server)
        {
            try
            {
                var handshakeBytes = await LengthPrefixedFraming.ReadAsync(server, _cts.Token).ConfigureAwait(false);
                var handshake = ProtocolSerializer.Deserialize<HandshakeRequest>(handshakeBytes);
                var ok = handshake.ProtocolVersion == ProtocolConstants.CurrentVersion &&
                         string.Equals(handshake.Token, _token, StringComparison.Ordinal);
                var handshakeResult = new HandshakeResult
                {
                    Ok = ok,
                    ProtocolVersion = ProtocolConstants.CurrentVersion,
                    Message = ok ? "ok" : "unauthorized or version mismatch"
                };
                await LengthPrefixedFraming.WriteAsync(server, ProtocolSerializer.Serialize(handshakeResult), _cts.Token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    return;
                }

                while (server.IsConnected && !_cts.IsCancellationRequested)
                {
                    var payload = await LengthPrefixedFraming.ReadAsync(server, _cts.Token).ConfigureAwait(false);
                    var request = ProtocolSerializer.Deserialize<JsonRpcRequest>(payload);
                    JsonRpcResponse response;
                    try
                    {
                        response = await _handler(request, _cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        response = new JsonRpcResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Error = new JsonRpcError { Code = -32000, Message = ex.Message }
                        };
                    }

                    try
                    {
                        await LengthPrefixedFraming.WriteAsync(server, ProtocolSerializer.Serialize(response), _cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Never let serialization/write failures kill the accept loop silently for the client.
                        try
                        {
                            var fallback = new JsonRpcResponse
                            {
                                Id = request.Id,
                                CorrelationId = request.CorrelationId,
                                Error = new JsonRpcError { Code = -32001, Message = $"Response write failed: {ex.Message}" }
                            };
                            await LengthPrefixedFraming.WriteAsync(server, ProtocolSerializer.Serialize(fallback), _cts.Token)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // client disconnected
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _cts.Dispose();
    }
}

public sealed class NamedPipeRpcClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NamedPipeRpcClient(NamedPipeClientStream client) => _client = client;

    public static async Task<NamedPipeRpcClient> ConnectAsync(string pipeName, string token, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
        var handshake = new HandshakeRequest
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Token = token,
            ClientName = "ParallelSeat.Client"
        };
        await LengthPrefixedFraming.WriteAsync(client, ProtocolSerializer.Serialize(handshake), cancellationToken)
            .ConfigureAwait(false);
        var responseBytes = await LengthPrefixedFraming.ReadAsync(client, cancellationToken).ConfigureAwait(false);
        var result = ProtocolSerializer.Deserialize<HandshakeResult>(responseBytes);
        if (!result.Ok)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new UnauthorizedAccessException(result.Message ?? "Handshake failed.");
        }

        return new NamedPipeRpcClient(client);
    }

    public async Task<JsonRpcResponse> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedFraming.WriteAsync(_client, ProtocolSerializer.Serialize(request), cancellationToken)
                .ConfigureAwait(false);
            var bytes = await LengthPrefixedFraming.ReadAsync(_client, cancellationToken).ConfigureAwait(false);
            return ProtocolSerializer.Deserialize<JsonRpcResponse>(bytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
