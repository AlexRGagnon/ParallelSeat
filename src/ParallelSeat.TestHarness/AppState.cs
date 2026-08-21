using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ParallelSeat.TestHarness;

public sealed class AppState : INotifyPropertyChanged
{
    public static AppState Shared { get; } = new();

    private string _sharedText = "initial";
    private int _sharedNumber = 0;
    private bool _sharedToggle;
    private string _sharedSelection = "Alpha";
    private string _lastAction = "none";
    private int _buttonClicks;

    public string SharedText
    {
        get => _sharedText;
        set => SetField(ref _sharedText, value);
    }

    public int SharedNumber
    {
        get => _sharedNumber;
        set => SetField(ref _sharedNumber, value);
    }

    public bool SharedToggle
    {
        get => _sharedToggle;
        set => SetField(ref _sharedToggle, value);
    }

    public string SharedSelection
    {
        get => _sharedSelection;
        set => SetField(ref _sharedSelection, value);
    }

    public string LastAction
    {
        get => _lastAction;
        set => SetField(ref _lastAction, value);
    }

    public int ButtonClicks
    {
        get => _buttonClicks;
        set => SetField(ref _buttonClicks, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RecordAction(string action)
    {
        LastAction = action;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
