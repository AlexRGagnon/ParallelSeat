using ParallelSeat.Ipc;
using ParallelSeat.Protocol;
using ParallelSeat.Windows;

namespace ParallelSeat.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "pipe-name" => PipeName(),
            "session" => PrintSession(),
            "enumerate-windows" => EnumerateWindows(args),
            "rpc" => await RpcAsync(args).ConfigureAwait(false),
            _ => Unknown(command)
        };
    }

    private static int PipeName()
    {
        Console.WriteLine(PipeSecurityFactory.GetPipeName());
        return 0;
    }

    private static int PrintSession()
    {
        var session = SessionFile.TryRead();
        if (session is null)
        {
            Console.Error.WriteLine("No ParallelSeat session file. Start ParallelSeat.Host first.");
            return 1;
        }

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(session, ProtocolSerializer.Options));
        return 0;
    }

    private static int EnumerateWindows(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var pid))
        {
            Console.Error.WriteLine("Usage: ParallelSeat.Cli enumerate-windows <pid>");
            return 1;
        }

        var instance = ProcessInstanceIdentity.FromPid(pid);
        Console.WriteLine($"Instance: {instance.Value}");
        foreach (var hwnd in ProcessInstanceIdentity.EnumerateTopLevelWindows(pid))
        {
            Console.WriteLine($"0x{hwnd.ToInt64():X} class={ProcessInstanceIdentity.GetWindowClass(hwnd)} title={ProcessInstanceIdentity.GetWindowTitle(hwnd)}");
        }

        return 0;
    }

    private static async Task<int> RpcAsync(string[] args)
    {
        // rpc [<token>] <method> [json-params|@file]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ParallelSeat.Cli rpc [<token>] <method> [json-params|@file]");
            return 1;
        }

        string token;
        string method;
        string json;

        if (args.Length >= 2 && args[1].Contains('.', StringComparison.Ordinal))
        {
            // session-based: rpc <method> [json]
            var session = SessionFile.TryRead();
            if (session is null)
            {
                Console.Error.WriteLine("No session.json. Start ParallelSeat.Host, or pass token: rpc <token> <method> [json]");
                return 1;
            }

            token = session.Token;
            method = args[1];
            json = args.Length >= 3 ? args[2] : "{}";
        }
        else if (args.Length >= 3)
        {
            token = args[1];
            method = args[2];
            json = args.Length >= 4 ? args[3] : "{}";
        }
        else
        {
            Console.Error.WriteLine("Usage: ParallelSeat.Cli rpc [<token>] <method> [json-params|@file]");
            return 1;
        }

        if (json.StartsWith('@') && json.Length > 1)
        {
            var path = json[1..];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Params file not found: {path}");
                return 1;
            }

            json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        }

        await using var client = await NamedPipeRpcClient.ConnectAsync(PipeSecurityFactory.GetPipeName(), token, CancellationToken.None)
            .ConfigureAwait(false);
        var request = new JsonRpcRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = method,
            Params = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(),
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        try
        {
            var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, ProtocolSerializer.Options));
            return response.Error is null ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                error = new { code = -32002, message = ex.Message }
            }));
            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ParallelSeat.Cli");
        Console.WriteLine("  pipe-name");
        Console.WriteLine("  session");
        Console.WriteLine("  enumerate-windows <pid>");
        Console.WriteLine("  rpc [<token>] <method> [json-params]");
    }
}
