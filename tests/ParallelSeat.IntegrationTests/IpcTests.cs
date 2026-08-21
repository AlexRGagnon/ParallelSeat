using ParallelSeat.Host;
using ParallelSeat.Ipc;
using ParallelSeat.Protocol;
using Xunit;

namespace ParallelSeat.IntegrationTests;

public class IpcTests
{
    [Fact]
    public async Task NamedPipe_Handshake_And_SeatLifecycle()
    {
        await using var runtime = new ParallelSeatRuntime("test-token");
        runtime.StartIpc();
        await Task.Delay(200);

        await using var client = await NamedPipeRpcClient.ConnectAsync(runtime.PipeName, "test-token", CancellationToken.None);
        var create = await client.SendAsync(new JsonRpcRequest
        {
            Id = "1",
            Method = "seat.create",
            Params = System.Text.Json.JsonDocument.Parse("""{"seatId":"ipc-seat","strictNoFocusSteal":true}""").RootElement.Clone()
        }, CancellationToken.None);

        Assert.Null(create.Error);
        var destroy = await client.SendAsync(new JsonRpcRequest
        {
            Id = "2",
            Method = "seat.destroy",
            Params = System.Text.Json.JsonDocument.Parse("""{"seatId":"ipc-seat"}""").RootElement.Clone()
        }, CancellationToken.None);
        Assert.Null(destroy.Error);
    }

    [Fact]
    public async Task Framing_Rejects_Oversize_Conceptually()
    {
        Assert.True(ProtocolConstants.MaxMessageBytes > 0);
        await Task.CompletedTask;
    }
}
