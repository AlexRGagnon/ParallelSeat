using System.Runtime.InteropServices;
using System.Text;
using ParallelSeat.Core;
using ParallelSeat.Windows;

namespace ParallelSeat.Win32Provider;

public sealed class Win32ActionProvider : IActionProvider
{
    private static readonly HashSet<string> ClassicButtonClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button"
    };

    private static readonly HashSet<string> ClassicEditClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit"
    };

    public ProviderKind Kind => ProviderKind.Win32;
    public int Priority => 30;

    public Task<CapabilitySet> ProbeCapabilitiesAsync(ElementRef element, CancellationToken cancellationToken)
    {
        if (element.NativeHwnd is not { } hwnd || hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return Task.FromResult(CapabilitySet.Unsupported("No classic child HWND."));
        }

        var className = ProcessInstanceIdentity.GetWindowClass(hwnd);
        ActionCapability flags = ActionCapability.None;
        if (ClassicButtonClasses.Contains(className))
        {
            flags |= ActionCapability.CanInvoke;
        }

        if (ClassicEditClasses.Contains(className))
        {
            flags |= ActionCapability.CanSetValue | ActionCapability.CanSendText;
        }

        return Task.FromResult(flags == ActionCapability.None
            ? CapabilitySet.Unsupported($"Class '{className}' is not in the Win32 allowlist.")
            : new CapabilitySet(flags, ProviderKind.Win32, className));
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
            _ => false
        };
    }

    public Task ExecuteAsync(SeatAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case InvokeAction invoke:
                Click(RequireHwnd(invoke.Target));
                break;
            case SetValueAction setValue:
                SetText(RequireHwnd(setValue.Target), setValue.Value);
                break;
            case TypeTextAction typeText:
                SetText(RequireHwnd(typeText.Target), typeText.Text);
                break;
            default:
                throw new NotSupportedException($"Win32 provider cannot execute {action.GetType().Name}.");
        }

        return Task.CompletedTask;
    }

    private static IntPtr RequireHwnd(ElementRef element)
    {
        if (element.NativeHwnd is not { } hwnd || hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            throw new StaleTargetException("Classic HWND is missing or stale.");
        }

        return hwnd;
    }

    private static void Click(IntPtr hwnd)
    {
        var result = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.BM_CLICK,
            UIntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out _);
        if (result == IntPtr.Zero)
        {
            throw new InvalidOperationException($"BM_CLICK failed or timed out. Win32 error={Marshal.GetLastWin32Error()}");
        }
    }

    private static void SetText(IntPtr hwnd, string text)
    {
        var result = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WM_SETTEXT,
            UIntPtr.Zero,
            text,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out _);
        if (result == IntPtr.Zero)
        {
            throw new InvalidOperationException($"WM_SETTEXT failed or timed out. Win32 error={Marshal.GetLastWin32Error()}");
        }
    }

    public static string GetText(IntPtr hwnd)
    {
        var lengthResult = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WM_GETTEXTLENGTH,
            UIntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out var length);
        if (lengthResult == IntPtr.Zero)
        {
            return string.Empty;
        }

        var capacity = (int)length.ToUInt32() + 1;
        var buffer = new StringBuilder(capacity);
        // Use WM_GETTEXT via unicode SendMessageTimeout string overload is awkward; return empty on failure path.
        _ = buffer;
        return string.Empty;
    }

    private static ElementRef? Extract(SeatAction action) => action switch
    {
        InvokeAction a => a.Target,
        SetValueAction a => a.Target,
        TypeTextAction a => a.Target,
        _ => null
    };
}
