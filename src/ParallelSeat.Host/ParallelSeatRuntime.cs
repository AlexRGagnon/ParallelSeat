using System.Text.Json;
using ParallelSeat.Automation;
using ParallelSeat.Core;
using ParallelSeat.Ipc;
using ParallelSeat.Overlay;
using ParallelSeat.Protocol;
using ParallelSeat.Windows;
using ParallelSeat.Win32Provider;

namespace ParallelSeat.Host;

public sealed class ParallelSeatRuntime : IAsyncDisposable
{
    private readonly SeatManager _seats;
    private readonly ActionRouter _router;
    private readonly UiaActionProvider _uia;
    private readonly Win32ActionProvider _win32;
    private readonly IAuditLog _audit = new InMemoryAuditLog();
    private readonly PollingConflictMonitor _conflicts;
    private readonly WpfOverlayController _overlay;
    private readonly string _token;
    private NamedPipeRpcServer? _server;

    public ParallelSeatRuntime(string? token = null, bool enableOverlay = true)
    {
        _token = token ?? Guid.NewGuid().ToString("N");
        _overlay = new WpfOverlayController(enableOverlay);
        _uia = new UiaActionProvider();
        _win32 = new Win32ActionProvider();
        var safety = new StrictSafetyPolicy();
        var foreground = new Win32ForegroundProbe();
        var cursor = new Win32CursorProbe();
        _router = new ActionRouter([_uia, _win32], safety, foreground, cursor, _audit);
        _seats = new SeatManager(_overlay);
        _conflicts = new PollingConflictMonitor(cursor, foreground);
        _conflicts.ConflictDetected += (_, e) =>
        {
            if (_seats.TryGet(e.SeatId, out var seat) && seat is not null)
            {
                seat.Pause(e.Reason);
                _overlay.Update(e.SeatId, seat.LogicalCursor, null, $"Paused: {e.Reason}");
            }
        };
    }

    public string Token => _token;
    public string PipeName => PipeSecurityFactory.GetPipeName();
    public SeatManager Seats => _seats;
    public ActionRouter Router => _router;
    public UiaActionProvider Uia => _uia;
    public IAuditLog Audit => _audit;

    /// <summary>
    /// Raised when a client requests graceful Host shutdown via <c>host.shutdown</c>.
    /// </summary>
    public event Action? ShutdownRequested;

    public void StartIpc()
    {
        _server = new NamedPipeRpcServer(PipeName, _token, HandleAsync);
        _server.Start();
        SessionFile.Write(PipeName, _token, Environment.ProcessId);
    }

    public async Task<JsonRpcResponse> HandleAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            object? result = request.Method switch
            {
                "seat.create" => SeatCreate(request),
                "seat.get_state" => SeatGetState(request),
                "seat.pause" => SeatPause(request),
                "seat.resume" => SeatResume(request),
                "seat.destroy" => SeatDestroy(request),
                "seat.attach" => await SeatAttachAsync(request, cancellationToken).ConfigureAwait(false),
                "element.find" => await ElementFindAsync(request, cancellationToken).ConfigureAwait(false),
                "element.get_capabilities" => await ElementCapsAsync(request, cancellationToken).ConfigureAwait(false),
                "keyboard.set_text" => await SetTextAsync(request, cancellationToken).ConfigureAwait(false),
                "pointer.click" => await InvokeAsync(request, cancellationToken).ConfigureAwait(false),
                "safety.emergency_stop" => EmergencyStop(),
                "diagnostics.get_status" => Diagnostics(),
                "host.shutdown" => RequestShutdown(),
                _ => throw new InvalidOperationException($"Unknown method '{request.Method}'.")
            };

            return new JsonRpcResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Error = new JsonRpcError { Code = -32000, Message = ex.Message }
            };
        }
    }

    private object SeatCreate(JsonRpcRequest request)
    {
        var seatId = GetString(request, "seatId") ?? Guid.NewGuid().ToString("N");
        var strict = GetBool(request, "strictNoFocusSteal") ?? true;
        var seat = _seats.Create(seatId, strict ? SafetyMode.Strict : SafetyMode.CompatibilityFocusStealOptIn);
        return new { seatId = seat.SeatId, state = seat.State.ToString() };
    }

    private object SeatGetState(JsonRpcRequest request)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        return new
        {
            seatId = seat.SeatId,
            state = seat.State.ToString(),
            conflict = seat.ConflictState.ToString(),
            instanceId = seat.AttachedInstance?.Value,
            logicalCursor = seat.LogicalCursor
        };
    }

    private object SeatPause(JsonRpcRequest request)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        seat.Pause(ConflictReason.None);
        return new { state = seat.State.ToString() };
    }

    private object SeatResume(JsonRpcRequest request)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        seat.Resume();
        return new { state = seat.State.ToString() };
    }

    private object SeatDestroy(JsonRpcRequest request)
    {
        var seatId = RequireString(request, "seatId");
        _conflicts.Stop(seatId);
        _seats.Destroy(seatId);
        return new { destroyed = true };
    }

    private async Task<object> SeatAttachAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        var pid = GetInt(request, "processId") ?? throw new ArgumentException("processId required");
        seat.BeginAttach();
        var instance = ProcessInstanceIdentity.FromPid(pid);
        var hwnds = ProcessInstanceIdentity.EnumerateTopLevelWindows(pid);
        seat.Activate(instance, instance.ProcessTreeIds, hwnds);
        await Task.CompletedTask.ConfigureAwait(false);
        return new { instanceId = instance.Value, windows = hwnds.Select(h => h.ToInt64()).ToArray() };
    }

    private async Task<object> ElementFindAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        if (seat.AttachedInstance is null)
        {
            throw new InvalidOperationException("Seat is not attached.");
        }

        var selector = new ElementSelector(
            AutomationId: GetString(request, "automationId"),
            Name: GetString(request, "name"),
            TopLevelHwnd: GetIntPtr(request, "topLevelHwnd"));
        var element = await _uia.FindAsync(seat.AttachedInstance, selector, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Element not found.");
        var hwnd = selector.TopLevelHwnd ?? element.NativeHwnd;
        seat.Claim(new TargetLease(seat.SeatId, seat.AttachedInstance, hwnd, element, DateTimeOffset.UtcNow));
        if (hwnd is { } claimed)
        {
            _conflicts.Start(seat.SeatId, seat.Lease!);
            var bounds = ProcessInstanceIdentity.GetBounds(claimed);
            var point = bounds is null
                ? new ScreenPoint(0, 0)
                : new ScreenPoint((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
            seat.SetLogicalCursor(point);
            _overlay.Show(seat.SeatId, point, bounds);
        }

        return new { elementId = element.ElementId, hwnd = element.NativeHwnd?.ToInt64() };
    }

    private async Task<object> ElementCapsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        var element = seat.Lease?.ClaimedElement ?? throw new InvalidOperationException("No claimed element.");
        var caps = await _uia.ProbeCapabilitiesAsync(element, cancellationToken).ConfigureAwait(false);
        return new { provider = caps.Provider.ToString(), flags = caps.Flags.ToString(), notes = caps.Notes };
    }

    private async Task<object> SetTextAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        var element = seat.Lease?.ClaimedElement ?? throw new InvalidOperationException("No claimed element.");
        var value = RequireString(request, "value");
        seat.Heartbeat();
        var result = await _router.ExecuteAsync(seat, new SetValueAction(Guid.NewGuid().ToString("N"), element, value), cancellationToken)
            .ConfigureAwait(false);
        return ToRpcActionResult(result);
    }

    private async Task<object> InvokeAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var seat = _seats.Get(RequireString(request, "seatId"));
        var element = seat.Lease?.ClaimedElement ?? throw new InvalidOperationException("No claimed element.");
        seat.Heartbeat();
        var result = await _router.ExecuteAsync(seat, new InvokeAction(Guid.NewGuid().ToString("N"), element), cancellationToken)
            .ConfigureAwait(false);
        return ToRpcActionResult(result);
    }

    private static object ToRpcActionResult(ActionResult result) => new
    {
        actionId = result.ActionId,
        status = result.Status.ToString(),
        provider = result.Provider.ToString(),
        message = result.Message,
        duration = result.Duration.ToString(),
        foregroundBefore = result.ForegroundBefore?.ToInt64(),
        foregroundAfter = result.ForegroundAfter?.ToInt64(),
        cursorBefore = result.CursorBefore is { } cb ? new { x = cb.X, y = cb.Y } : null,
        cursorAfter = result.CursorAfter is { } ca ? new { x = ca.X, y = ca.Y } : null,
        humanConflictDetected = result.HumanConflictDetected,
        capabilityDecision = result.CapabilityDecision is null
            ? null
            : new
            {
                flags = result.CapabilityDecision.Flags.ToString(),
                provider = result.CapabilityDecision.Provider.ToString(),
                notes = result.CapabilityDecision.Notes
            },
        foregroundChanged = result.ForegroundChanged,
        cursorChanged = result.CursorChanged,
        violatedStrictNoFocusSteal = result.ViolatedStrictNoFocusSteal
    };

    private object EmergencyStop()
    {
        _seats.EmergencyStop();
        return new { stopped = true };
    }

    private object RequestShutdown()
    {
        _seats.EmergencyStop();
        // Return before the process tears down so the RPC client gets a response.
        ThreadPool.QueueUserWorkItem(_ => ShutdownRequested?.Invoke());
        return new { shuttingDown = true };
    }

    private object Diagnostics() => new
    {
        pipe = PipeName,
        seats = _seats.List().Select(s => new { s.SeatId, state = s.State.ToString() }).ToArray(),
        auditCount = _audit.Snapshot().Count,
        processId = Environment.ProcessId
    };

    private static string RequireString(JsonRpcRequest request, string name) =>
        GetString(request, name) ?? throw new ArgumentException($"{name} required");

    private static string? GetString(JsonRpcRequest request, string name)
    {
        if (request.Params is not { } p || !p.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.GetString();
    }

    private static int? GetInt(JsonRpcRequest request, string name)
    {
        if (request.Params is not { } p || !p.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.TryGetInt32(out var i))
        {
            return i;
        }

        if (value.TryGetInt64(out var l) && l <= int.MaxValue && l >= int.MinValue)
        {
            return (int)l;
        }

        return null;
    }

    private static bool? GetBool(JsonRpcRequest request, string name)
    {
        if (request.Params is not { } p || !p.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static long? GetLong(JsonRpcRequest request, string name)
    {
        if (request.Params is not { } p || !p.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.TryGetInt64(out var l))
        {
            return l;
        }

        if (value.TryGetInt32(out var i))
        {
            return i;
        }

        return null;
    }

    private static IntPtr? GetIntPtr(JsonRpcRequest request, string name)
    {
        var value = GetLong(request, name);
        return value is null ? null : new IntPtr(value.Value);
    }

    public async ValueTask DisposeAsync()
    {
        SessionFile.Delete();
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        _conflicts.Dispose();
        _uia.Dispose();
        _overlay.HideAll();
    }
}
