using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ParallelSeat.Windows;

namespace ParallelSeat.TestHarness;

public sealed class HumanWindow : Window
{
    public TextBox Editor { get; }

    public HumanWindow()
    {
        Title = "ParallelSeat Harness — Human Window";
        Width = 520;
        Height = 420;
        Left = 80;
        Top = 80;
        AutomationProperties.SetAutomationId(this, "HumanWindow");

        Editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 16,
            Margin = new Thickness(12)
        };
        AutomationProperties.SetAutomationId(Editor, "HumanEditor");

        var panel = new DockPanel();
        var header = new TextBlock
        {
            Text = "Type continuously here. Shared state is shown below.",
            Margin = new Thickness(12, 12, 12, 0)
        };
        DockPanel.SetDock(header, Dock.Top);

        var shared = new TextBlock { Margin = new Thickness(12) };
        shared.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AppState.SharedText))
        {
            Source = AppState.Shared,
            StringFormat = "SharedText: {0}"
        });
        DockPanel.SetDock(shared, Dock.Bottom);

        panel.Children.Add(header);
        panel.Children.Add(shared);
        panel.Children.Add(Editor);
        Content = panel;
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();
}

public sealed class AiWindow : Window
{
    public TextBox ValueField { get; }
    public Button InvokeButton { get; }
    public CheckBox ToggleBox { get; }
    public ListBox ItemList { get; }
    public Slider ValueSlider { get; }
    public ContentControl DynamicHost { get; }
    public int DynamicGeneration { get; private set; }

    public AiWindow()
    {
        Title = "ParallelSeat Harness — AI Window";
        Width = 560;
        Height = 640;
        Left = 640;
        Top = 80;
        AutomationProperties.SetAutomationId(this, "AiWindow");

        var root = new ScrollViewer();
        var panel = new StackPanel { Margin = new Thickness(12) };

        ValueField = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(ValueField, "AiValueField");
        InProcessHarnessAdapter.Register("AiValueField", ValueField);
        ValueField.TextChanged += (_, _) =>
        {
            AppState.Shared.SharedText = ValueField.Text;
            AppState.Shared.RecordAction("AiValueField.TextChanged");
        };

        InvokeButton = new Button { Content = "Invoke Shared Action", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8) };
        AutomationProperties.SetAutomationId(InvokeButton, "AiInvokeButton");
        InProcessHarnessAdapter.Register("AiInvokeButton", InvokeButton);
        InvokeButton.Click += (_, _) =>
        {
            AppState.Shared.ButtonClicks++;
            AppState.Shared.RecordAction("AiInvokeButton.Click");
        };

        ToggleBox = new CheckBox { Content = "Shared Toggle", Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(ToggleBox, "AiToggle");
        InProcessHarnessAdapter.Register("AiToggle", ToggleBox);
        ToggleBox.Checked += (_, _) => { AppState.Shared.SharedToggle = true; AppState.Shared.RecordAction("AiToggle.Checked"); };
        ToggleBox.Unchecked += (_, _) => { AppState.Shared.SharedToggle = false; AppState.Shared.RecordAction("AiToggle.Unchecked"); };

        ItemList = new ListBox { Height = 100, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(ItemList, "AiItemList");
        foreach (var item in new[] { "Alpha", "Beta", "Gamma" })
        {
            ItemList.Items.Add(item);
        }

        ItemList.SelectionChanged += (_, _) =>
        {
            if (ItemList.SelectedItem is string s)
            {
                AppState.Shared.SharedSelection = s;
                AppState.Shared.RecordAction("AiItemList.SelectionChanged");
            }
        };

        ValueSlider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(ValueSlider, "AiSlider");
        ValueSlider.ValueChanged += (_, _) =>
        {
            AppState.Shared.SharedNumber = (int)ValueSlider.Value;
            AppState.Shared.RecordAction("AiSlider.ValueChanged");
        };

        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(combo, "AiCombo");
        combo.Items.Add("One");
        combo.Items.Add("Two");

        var tree = new TreeView { Height = 80, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(tree, "AiTree");
        var rootNode = new TreeViewItem { Header = "Root" };
        rootNode.Items.Add(new TreeViewItem { Header = "Child" });
        tree.Items.Add(rootNode);

        var tabs = new TabControl { Height = 80, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(tabs, "AiTabs");
        tabs.Items.Add(new TabItem { Header = "Tab1", Content = new TextBlock { Text = "Tab 1" } });
        tabs.Items.Add(new TabItem { Header = "Tab2", Content = new TextBlock { Text = "Tab 2" } });

        var noId = new TextBox { Margin = new Thickness(0, 0, 0, 8), Text = "No AutomationId" };

        var canvas = new Canvas { Height = 80, Background = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(canvas, "AiCustomCanvas");
        canvas.Children.Add(new TextBlock { Text = "Custom-rendered surface", Margin = new Thickness(8) });

        DynamicHost = new ContentControl { Margin = new Thickness(0, 0, 0, 8) };
        RecreateDynamicControl();

        var recreate = new Button { Content = "Recreate Dynamic Control", Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(recreate, "AiRecreateDynamic");
        recreate.Click += (_, _) => RecreateDynamicControl();

        var sharedView = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        sharedView.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AppState.LastAction))
        {
            Source = AppState.Shared,
            StringFormat = "LastAction: {0}"
        });

        panel.Children.Add(new TextBlock { Text = "AI targets", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(ValueField);
        panel.Children.Add(InvokeButton);
        panel.Children.Add(ToggleBox);
        panel.Children.Add(ItemList);
        panel.Children.Add(ValueSlider);
        panel.Children.Add(combo);
        panel.Children.Add(tree);
        panel.Children.Add(tabs);
        panel.Children.Add(noId);
        panel.Children.Add(canvas);
        panel.Children.Add(DynamicHost);
        panel.Children.Add(recreate);
        panel.Children.Add(sharedView);

        root.Content = panel;
        Content = root;
    }

    public void RecreateDynamicControl()
    {
        DynamicGeneration++;
        var box = new TextBox { Text = $"dynamic-{DynamicGeneration}" };
        AutomationProperties.SetAutomationId(box, "AiDynamicField");
        InProcessHarnessAdapter.Register("AiDynamicField", box);
        DynamicHost.Content = box;
        AppState.Shared.RecordAction($"Dynamic recreated gen={DynamicGeneration}");
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();
}

public sealed class InstrumentationWindow : Window
{
    public InstrumentationWindow(HumanWindow human, AiWindow ai)
    {
        Title = "ParallelSeat Harness — Instrumentation";
        Width = 520;
        Height = 280;
        Left = 80;
        Top = 520;

        var text = new TextBlock
        {
            Margin = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Content = text;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            var fg = NativeMethods.GetForegroundWindow();
            NativeMethods.GetCursorPos(out var cursor);
            text.Text =
                $"PID={process.Id}\n" +
                $"StartUtc={process.StartTime.ToUniversalTime():o}\n" +
                $"HumanHwnd=0x{human.Handle.ToInt64():X}\n" +
                $"AiHwnd=0x{ai.Handle.ToInt64():X}\n" +
                $"Foreground=0x{fg.ToInt64():X}\n" +
                $"Cursor=({cursor.X},{cursor.Y})\n" +
                $"SharedText={AppState.Shared.SharedText}\n" +
                $"ButtonClicks={AppState.Shared.ButtonClicks}\n" +
                $"LastAction={AppState.Shared.LastAction}\n" +
                $"UIThread={Environment.CurrentManagedThreadId}";
        };
        timer.Start();
    }
}
