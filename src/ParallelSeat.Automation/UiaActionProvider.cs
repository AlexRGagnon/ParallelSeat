using System.Collections.Concurrent;
using System.Windows.Automation;
using ParallelSeat.Core;

namespace ParallelSeat.Automation;

public sealed class UiaWorker : IDisposable
{
    private readonly BlockingCollection<Func<Task>> _queue = new();
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    public UiaWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "ParallelSeat-UIA-STA"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                tcs.TrySetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }, cancellationToken);

        return tcs.Task.WaitAsync(cancellationToken);
    }

    public Task InvokeAsync(Action work, CancellationToken cancellationToken) =>
        InvokeAsync(() =>
        {
            work();
            return true;
        }, cancellationToken);

    private void Run()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
            {
                item().GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // best effort
        }

        _cts.Dispose();
        _queue.Dispose();
    }
}

public sealed class ElementResolver
{
    private readonly UiaWorker _worker;

    public ElementResolver(UiaWorker worker) => _worker = worker;

    public Task<AutomationElement?> ResolveAsync(ElementSelector selector, CancellationToken cancellationToken) =>
        _worker.InvokeAsync(() => Resolve(selector), cancellationToken);

    public static AutomationElement? Resolve(ElementSelector selector)
    {
        AutomationElement root = selector.TopLevelHwnd is { } hwnd && hwnd != IntPtr.Zero
            ? AutomationElement.FromHandle(hwnd)
            : AutomationElement.RootElement;

        var conditions = new List<Condition>();
        if (!string.IsNullOrWhiteSpace(selector.AutomationId))
        {
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, selector.AutomationId));
        }

        if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, selector.Name));
        }

        if (!string.IsNullOrWhiteSpace(selector.ClassName))
        {
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, selector.ClassName));
        }

        if (!string.IsNullOrWhiteSpace(selector.ControlType))
        {
            if (TryMapControlType(selector.ControlType, out var controlType))
            {
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
            }
        }

        if (conditions.Count == 0)
        {
            return null;
        }

        Condition condition = conditions.Count == 1
            ? conditions[0]
            : new AndCondition(conditions.ToArray());

        var matches = root.FindAll(TreeScope.Descendants, condition);
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1 && string.IsNullOrWhiteSpace(selector.AutomationId))
        {
            throw new InvalidOperationException("Selector matched multiple elements; refine the selector.");
        }

        return matches[0];
    }

    private static bool TryMapControlType(string name, out ControlType controlType)
    {
        controlType = ControlType.Custom;
        var map = new Dictionary<string, ControlType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Button"] = ControlType.Button,
            ["Edit"] = ControlType.Edit,
            ["CheckBox"] = ControlType.CheckBox,
            ["RadioButton"] = ControlType.RadioButton,
            ["ComboBox"] = ControlType.ComboBox,
            ["List"] = ControlType.List,
            ["ListItem"] = ControlType.ListItem,
            ["Tree"] = ControlType.Tree,
            ["Tab"] = ControlType.Tab,
            ["Slider"] = ControlType.Slider,
            ["DataGrid"] = ControlType.DataGrid,
            ["Text"] = ControlType.Text
        };
        return map.TryGetValue(name, out controlType!);
    }
}

public sealed class UiaActionProvider : IActionProvider, IDisposable
{
    private readonly UiaWorker _worker;
    private readonly ElementResolver _resolver;

    public UiaActionProvider(UiaWorker? worker = null)
    {
        _worker = worker ?? new UiaWorker();
        _resolver = new ElementResolver(_worker);
    }

    public ProviderKind Kind => ProviderKind.UiAutomation;
    public int Priority => 20;

    public async Task<CapabilitySet> ProbeCapabilitiesAsync(ElementRef element, CancellationToken cancellationToken)
    {
        return await _worker.InvokeAsync(() =>
        {
            var ae = ResolveOrThrow(element);
            ActionCapability flags = ActionCapability.None;
            if (ae.TryGetCurrentPattern(InvokePattern.Pattern, out _))
            {
                flags |= ActionCapability.CanInvoke;
            }

            if (ae.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj) &&
                valueObj is ValuePattern value && !value.Current.IsReadOnly)
            {
                flags |= ActionCapability.CanSetValue | ActionCapability.CanSendText;
            }

            if (ae.TryGetCurrentPattern(TogglePattern.Pattern, out _))
            {
                flags |= ActionCapability.CanToggle;
            }

            if (ae.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
            {
                flags |= ActionCapability.CanSelect;
            }

            if (ae.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _))
            {
                flags |= ActionCapability.CanExpandCollapse;
            }

            if (ae.TryGetCurrentPattern(ScrollPattern.Pattern, out _))
            {
                flags |= ActionCapability.CanScroll;
            }

            if (ae.Current.IsPassword)
            {
                return CapabilitySet.Unsupported("Password fields are blocked.");
            }

            return new CapabilitySet(flags, ProviderKind.UiAutomation);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CanExecuteAsync(SeatAction action, CancellationToken cancellationToken)
    {
        var element = Extract(action);
        if (element is null)
        {
            return false;
        }

        var caps = await ProbeCapabilitiesAsync(element, cancellationToken).ConfigureAwait(false);
        return action switch
        {
            InvokeAction => caps.Has(ActionCapability.CanInvoke),
            SetValueAction => caps.Has(ActionCapability.CanSetValue),
            ToggleAction => caps.Has(ActionCapability.CanToggle),
            SelectAction => caps.Has(ActionCapability.CanSelect),
            ExpandCollapseAction => caps.Has(ActionCapability.CanExpandCollapse),
            ScrollAction => caps.Has(ActionCapability.CanScroll),
            TypeTextAction => caps.Has(ActionCapability.CanSendText),
            _ => false
        };
    }

    public async Task ExecuteAsync(SeatAction action, CancellationToken cancellationToken)
    {
        await _worker.InvokeAsync(() =>
        {
            switch (action)
            {
                case InvokeAction invoke:
                    ((InvokePattern)ResolveOrThrow(invoke.Target).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    break;
                case SetValueAction setValue:
                    ((ValuePattern)ResolveOrThrow(setValue.Target).GetCurrentPattern(ValuePattern.Pattern)).SetValue(setValue.Value);
                    break;
                case ToggleAction toggle:
                    ((TogglePattern)ResolveOrThrow(toggle.Target).GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                    break;
                case SelectAction select:
                    ((SelectionItemPattern)ResolveOrThrow(select.Target).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    break;
                case ExpandCollapseAction expand:
                {
                    var pattern = (ExpandCollapsePattern)ResolveOrThrow(expand.Target).GetCurrentPattern(ExpandCollapsePattern.Pattern);
                    if (expand.Expand)
                    {
                        pattern.Expand();
                    }
                    else
                    {
                        pattern.Collapse();
                    }

                    break;
                }
                case ScrollAction scroll:
                    ((ScrollPattern)ResolveOrThrow(scroll.Target).GetCurrentPattern(ScrollPattern.Pattern))
                        .SetScrollPercent(scroll.HorizontalPercent, scroll.VerticalPercent);
                    break;
                case TypeTextAction typeText:
                    ((ValuePattern)ResolveOrThrow(typeText.Target).GetCurrentPattern(ValuePattern.Pattern)).SetValue(typeText.Text);
                    break;
                default:
                    throw new NotSupportedException($"UIA provider cannot execute {action.GetType().Name}.");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ElementRef?> FindAsync(ApplicationInstanceId instance, ElementSelector selector, CancellationToken cancellationToken) =>
        _worker.InvokeAsync(() =>
        {
            var ae = ElementResolver.Resolve(selector);
            if (ae is null)
            {
                return null;
            }

            return new ElementRef(
                Guid.NewGuid().ToString("N"),
                instance,
                selector,
                new IntPtr(ae.Current.NativeWindowHandle),
                string.Join(".", ae.GetRuntimeId() ?? []));
        }, cancellationToken);

    private AutomationElement ResolveOrThrow(ElementRef element)
    {
        try
        {
            var ae = ElementResolver.Resolve(element.Selector);
            if (ae is null)
            {
                throw new StaleTargetException("UI Automation element could not be resolved.");
            }

            if (ae.Current.IsPassword)
            {
                throw new InvalidOperationException("Password field interaction is blocked.");
            }

            _ = ae.Current.Name; // touch to detect stale
            return ae;
        }
        catch (ElementNotAvailableException ex)
        {
            throw new StaleTargetException(ex.Message);
        }
    }

    private static ElementRef? Extract(SeatAction action) => action switch
    {
        InvokeAction a => a.Target,
        SetValueAction a => a.Target,
        ToggleAction a => a.Target,
        SelectAction a => a.Target,
        ExpandCollapseAction a => a.Target,
        ScrollAction a => a.Target,
        TypeTextAction a => a.Target,
        _ => null
    };

    public void Dispose() => _worker.Dispose();
}

internal static class AutomationElementExtensions
{
    public static bool TryGetCurrentPattern(this AutomationElement element, AutomationPattern pattern, out object? patternObject)
    {
        try
        {
            patternObject = element.GetCurrentPattern(pattern);
            return patternObject is not null;
        }
        catch (InvalidOperationException)
        {
            patternObject = null;
            return false;
        }
    }
}
