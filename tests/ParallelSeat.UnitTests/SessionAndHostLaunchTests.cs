using ParallelSeat.Ipc;
using Xunit;

namespace ParallelSeat.UnitTests;

public class HostLaunchOptionsTests
{
    [Theory]
    [InlineData("--headless", true)]
    [InlineData("--agent", true)]
    [InlineData("/headless", true)]
    [InlineData("/agent", true)]
    [InlineData("--Headless", true)]
    [InlineData("--verbose", false)]
    public void IsHeadless_ParsesFlags(string arg, bool expected)
    {
        Assert.Equal(expected, HostLaunchOptions.IsHeadless([arg]));
    }

    [Fact]
    public void IsHeadless_FalseWhenEmpty()
    {
        Assert.False(HostLaunchOptions.IsHeadless([]));
    }

    [Fact]
    public void GetMutexName_IncludesSidPrefix()
    {
        var name = HostLaunchOptions.GetMutexName();
        Assert.StartsWith("Local\\ParallelSeat.Host.", name);
        Assert.True(name.Length > "Local\\ParallelSeat.Host.".Length);
    }
}

public class SessionFileTests : IDisposable
{
    private readonly string _dir;

    public SessionFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ParallelSeat.SessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SessionFile.SetDirectoryPathOverride(_dir);
    }

    public void Dispose()
    {
        SessionFile.SetDirectoryPathOverride(null);
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Write_TryRead_Delete_RoundTrip()
    {
        Assert.Null(SessionFile.TryRead());

        SessionFile.Write("pipe-a", "token-b", 4242);
        var read = SessionFile.TryRead();
        Assert.NotNull(read);
        Assert.Equal("pipe-a", read!.PipeName);
        Assert.Equal("token-b", read.Token);
        Assert.Equal(4242, read.HostProcessId);
        Assert.True(File.Exists(SessionFile.FilePath));

        SessionFile.Delete();
        Assert.Null(SessionFile.TryRead());
        Assert.False(File.Exists(SessionFile.FilePath));
    }

    [Fact]
    public void Delete_IdempotentWhenMissing()
    {
        SessionFile.Delete();
        SessionFile.Delete();
        Assert.Null(SessionFile.TryRead());
    }

    [Fact]
    public void TryDeleteIfStale_RemovesDeadPid()
    {
        SessionFile.Write("pipe", "tok", hostProcessId: 1);
        Assert.True(SessionFile.TryDeleteIfStale(_ => false));
        Assert.Null(SessionFile.TryRead());
    }

    [Fact]
    public void TryDeleteIfStale_KeepsLivePid()
    {
        SessionFile.Write("pipe", "tok", hostProcessId: 99);
        Assert.False(SessionFile.TryDeleteIfStale(_ => true));
        Assert.NotNull(SessionFile.TryRead());
    }
}
