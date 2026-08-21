namespace ParallelSeat.Core;

public sealed class ActionRouter
{
    private readonly IReadOnlyList<IActionProvider> _providers;
    private readonly ISafetyPolicy _safety;
    private readonly IForegroundProbe _foreground;
    private readonly ICursorProbe _cursor;
    private readonly IAuditLog _audit;

    public ActionRouter(
        IEnumerable<IActionProvider> providers,
        ISafetyPolicy safety,
        IForegroundProbe foreground,
        ICursorProbe cursor,
        IAuditLog audit)
    {
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _safety = safety;
        _foreground = foreground;
        _cursor = cursor;
        _audit = audit;
    }

    public async Task<ActionResult> ExecuteAsync(Seat seat, SeatAction action, CancellationToken cancellationToken)
    {
        if (seat.State != SeatState.Active)
        {
            return Reject(seat, action, ActionStatus.ConflictPaused, $"Seat is {seat.State}.");
        }

        if (seat.SafetyMode == SafetyMode.Strict && _safety.Mode != SafetyMode.Strict)
        {
            return Reject(seat, action, ActionStatus.RejectedBySafety, "Strict seat requires strict safety policy.");
        }

        var started = DateTimeOffset.UtcNow;
        var fgBefore = _foreground.GetForegroundWindow();
        var cursorBefore = _cursor.GetCursorPosition();

        IActionProvider? selected = null;
        CapabilitySet? decision = null;

        foreach (var provider in _providers)
        {
            if (!_safety.AllowsProvider(provider.Kind))
            {
                continue;
            }

            if (!await provider.CanExecuteAsync(action, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (action is InvokeAction or SetValueAction or ToggleAction or SelectAction or ExpandCollapseAction or ScrollAction or TypeTextAction)
            {
                var element = GetElement(action);
                if (element is not null)
                {
                    var caps = await provider.ProbeCapabilitiesAsync(element, cancellationToken).ConfigureAwait(false);
                    if (!_safety.AllowsCapability(caps.Flags))
                    {
                        continue;
                    }

                    decision = caps;
                }
            }

            selected = provider;
            break;
        }

        if (selected is null)
        {
            decision ??= CapabilitySet.Unsupported("No safe provider available.");
            var unsupported = new ActionResult(
                action.ActionId,
                ActionStatus.Unsupported,
                ProviderKind.Unsupported,
                decision.Notes,
                DateTimeOffset.UtcNow - started,
                fgBefore,
                _foreground.GetForegroundWindow(),
                cursorBefore,
                _cursor.GetCursorPosition(),
                false,
                decision);
            Audit(seat, action, unsupported);
            return unsupported;
        }

        seat.BeginAction(action);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(action.Timeout);
            await selected.ExecuteAsync(action, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timedOut = BuildResult(action, ActionStatus.TimedOut, selected.Kind, "Timed out.", started, fgBefore, cursorBefore, decision, false);
            Audit(seat, action, timedOut);
            seat.CompleteAction();
            return timedOut;
        }
        catch (StaleTargetException ex)
        {
            var stale = BuildResult(action, ActionStatus.StaleTarget, selected.Kind, ex.Message, started, fgBefore, cursorBefore, decision, false);
            Audit(seat, action, stale);
            seat.CompleteAction();
            return stale;
        }
        catch (Exception ex)
        {
            var failed = BuildResult(action, ActionStatus.Failed, selected.Kind, ex.Message, started, fgBefore, cursorBefore, decision, false);
            Audit(seat, action, failed);
            seat.CompleteAction();
            return failed;
        }

        var fgAfter = _foreground.GetForegroundWindow();
        var cursorAfter = _cursor.GetCursorPosition();
        var result = BuildResult(action, ActionStatus.Succeeded, selected.Kind, null, started, fgBefore, cursorBefore, decision, false, fgAfter, cursorAfter);

        if (seat.SafetyMode == SafetyMode.Strict && result.ViolatedStrictNoFocusSteal)
        {
            result = result with
            {
                Status = ActionStatus.RejectedBySafety,
                Message = "Strict mode violation: native foreground or cursor changed during action."
            };
            seat.Pause(ConflictReason.ApplicationStateInvalidated);
        }

        Audit(seat, action, result);
        seat.CompleteAction();
        seat.SetCapabilities(selected.Kind, decision ?? new CapabilitySet(ActionCapability.None, selected.Kind));
        return result;
    }

    private ActionResult Reject(Seat seat, SeatAction action, ActionStatus status, string message)
    {
        var result = new ActionResult(
            action.ActionId,
            status,
            ProviderKind.Unsupported,
            message,
            TimeSpan.Zero,
            null,
            null,
            null,
            null,
            status == ActionStatus.ConflictPaused,
            CapabilitySet.Unsupported(message));
        Audit(seat, action, result);
        return result;
    }

    private static ActionResult BuildResult(
        SeatAction action,
        ActionStatus status,
        ProviderKind provider,
        string? message,
        DateTimeOffset started,
        IntPtr fgBefore,
        ScreenPoint cursorBefore,
        CapabilitySet? decision,
        bool conflict,
        IntPtr? fgAfter = null,
        ScreenPoint? cursorAfter = null) =>
        new(
            action.ActionId,
            status,
            provider,
            message,
            DateTimeOffset.UtcNow - started,
            fgBefore,
            fgAfter,
            cursorBefore,
            cursorAfter,
            conflict,
            decision);

    private void Audit(Seat seat, SeatAction action, ActionResult result)
    {
        _audit.Record(new AuditEntry(
            DateTimeOffset.UtcNow,
            action.ActionId,
            seat.SeatId,
            seat.AttachedInstance?.Value ?? "",
            seat.Lease?.ClaimedWindowHwnd?.ToString(),
            GetElement(action)?.ElementId,
            action.GetType().Name,
            result.Provider,
            result.CapabilityDecision?.Flags.ToString() ?? "",
            seat.SafetyMode.ToString(),
            result.Status,
            result.Message,
            result.Duration,
            result.ForegroundChanged,
            result.CursorChanged,
            result.HumanConflictDetected));
    }

    private static ElementRef? GetElement(SeatAction action) => action switch
    {
        InvokeAction a => a.Target,
        SetValueAction a => a.Target,
        ToggleAction a => a.Target,
        SelectAction a => a.Target,
        ExpandCollapseAction a => a.Target,
        ScrollAction a => a.Target,
        TypeTextAction a => a.Target,
        PressKeyAction a => a.Target,
        _ => null
    };
}

public sealed class StaleTargetException : Exception
{
    public StaleTargetException(string message) : base(message)
    {
    }
}
