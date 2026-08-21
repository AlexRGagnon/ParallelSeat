using System.Text.Json;

namespace ParallelSeat.Ipc;

public static class SessionFile
{
    private static string? _directoryPathOverride;

    public static string DirectoryPath =>
        _directoryPathOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParallelSeat");

    public static string FilePath => Path.Combine(DirectoryPath, "session.json");

    /// <summary>
    /// Test hook: redirect session directory. Pass null to restore default.
    /// </summary>
    public static void SetDirectoryPathOverride(string? path) => _directoryPathOverride = path;

    public static void Write(string pipeName, string token, int hostProcessId)
    {
        Directory.CreateDirectory(DirectoryPath);
        var payload = new SessionPayload
        {
            PipeName = pipeName,
            Token = token,
            HostProcessId = hostProcessId,
            WrittenAtUtc = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static SessionPayload? TryRead()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<SessionPayload>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Deletes session.json when missing, unreadable, or host PID is not alive.
    /// Returns true if a stale/missing session was cleared (or already absent).
    /// </summary>
    public static bool TryDeleteIfStale(Func<int, bool>? isProcessAlive = null)
    {
        isProcessAlive ??= static pid =>
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        };

        var session = TryRead();
        if (session is null)
        {
            Delete();
            return true;
        }

        if (session.HostProcessId <= 0 || !isProcessAlive(session.HostProcessId))
        {
            Delete();
            return true;
        }

        return false;
    }
}

public sealed class SessionPayload
{
    public string PipeName { get; set; } = "";
    public string Token { get; set; } = "";
    public int HostProcessId { get; set; }
    public DateTime WrittenAtUtc { get; set; }
}
