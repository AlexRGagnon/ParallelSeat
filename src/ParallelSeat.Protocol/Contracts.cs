using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParallelSeat.Protocol;

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
    public const int MaxMessageBytes = 1_048_576;
    public const string PipeNamePrefix = "ParallelSeat-";
}

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public sealed class HandshakeRequest
{
    public int ProtocolVersion { get; set; }
    public string Token { get; set; } = "";
    public string ClientName { get; set; } = "";
}

public sealed class HandshakeResult
{
    public bool Ok { get; set; }
    public int ProtocolVersion { get; set; }
    public string? Message { get; set; }
}

public static class ProtocolSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<T>(utf8, Options) ?? throw new InvalidOperationException("Null JSON payload.");
}
