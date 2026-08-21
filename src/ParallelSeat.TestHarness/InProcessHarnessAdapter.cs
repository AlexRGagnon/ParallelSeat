using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ParallelSeat.Core;

namespace ParallelSeat.TestHarness;

/// <summary>
/// Tier-A style same-process adapter for the purpose-built harness.
/// Avoids UIA providers that may activate WPF windows on Invoke/SetValue.
/// </summary>
public sealed class InProcessHarnessAdapter : IActionProvider
{
    private static readonly ConcurrentDictionary<string, WeakReference<FrameworkElement>> Controls =
        new(StringComparer.Ordinal);

    public ProviderKind Kind => ProviderKind.ApplicationAdapter;
    public int Priority => 10;

    public static void Register(string automationId, FrameworkElement element) =>
        Controls[automationId] = new WeakReference<FrameworkElement>(element);

    public static void Clear() => Controls.Clear();

    public Task<CapabilitySet> ProbeCapabilitiesAsync(ElementRef element, CancellationToken cancellationToken)
    {
        if (!TryGet(element, out var control))
        {
            return Task.FromResult(CapabilitySet.Unsupported("Control not registered in harness adapter."));
        }

        ActionCapability flags = ActionCapability.None;
        if (control is ButtonBase)
        {
            flags |= ActionCapability.CanInvoke;
        }

        if (control is TextBox)
        {
            flags |= ActionCapability.CanSetValue | ActionCapability.CanSendText;
        }

        if (control is ToggleButton)
        {
            flags |= ActionCapability.CanToggle;
        }

        return Task.FromResult(new CapabilitySet(flags, ProviderKind.ApplicationAdapter, "InProcessHarnessAdapter"));
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
            TypeTextAction => caps.Has(ActionCapability.CanSendText),
            ToggleAction => caps.Has(ActionCapability.CanToggle),
            _ => false
        };
    }

    public Task ExecuteAsync(SeatAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case SetValueAction setValue:
            {
                if (!TryGet(setValue.Target, out var control) || control is not TextBox textBox)
                {
                    throw new StaleTargetException("Harness TextBox not available.");
                }

                InvokeOnDispatcher(textBox, () => textBox.Text = setValue.Value);
                break;
            }
            case TypeTextAction typeText:
            {
                if (!TryGet(typeText.Target, out var control) || control is not TextBox textBox)
                {
                    throw new StaleTargetException("Harness TextBox not available.");
                }

                InvokeOnDispatcher(textBox, () => textBox.Text = typeText.Text);
                break;
            }
            case InvokeAction invoke:
            {
                if (!TryGet(invoke.Target, out var control) || control is not ButtonBase button)
                {
                    throw new StaleTargetException("Harness Button not available.");
                }

                InvokeOnDispatcher(button, () => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
                break;
            }
            case ToggleAction toggle:
            {
                if (!TryGet(toggle.Target, out var control) || control is not ToggleButton toggleButton)
                {
                    throw new StaleTargetException("Harness Toggle not available.");
                }

                InvokeOnDispatcher(toggleButton, () => toggleButton.IsChecked = !(toggleButton.IsChecked ?? false));
                break;
            }
            default:
                throw new NotSupportedException(action.GetType().Name);
        }

        return Task.CompletedTask;
    }

    private static bool TryGet(ElementRef element, out FrameworkElement control)
    {
        control = null!;
        var id = element.Selector.AutomationId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!Controls.TryGetValue(id, out var weak) || !weak.TryGetTarget(out var target))
        {
            return false;
        }

        control = target;
        return true;
    }

    private static void InvokeOnDispatcher(DispatcherObject control, Action action)
    {
        var dispatcher = control.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static ElementRef? Extract(SeatAction action) => action switch
    {
        InvokeAction a => a.Target,
        SetValueAction a => a.Target,
        TypeTextAction a => a.Target,
        ToggleAction a => a.Target,
        _ => null
    };
}
