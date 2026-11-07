using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using Microsoft.Win32;
using TimesGateMonitor.App.Dashboard;
using TimesGateMonitor.App.Infrastructure;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Rendering.Renderers.Hardware;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

namespace TimesGateMonitor.App;

internal sealed record WidgetOption(string? Key, string Label);

internal sealed record PreviewBinding(TextBlock Target, Func<HardwareSnapshot, string> Formatter);

public partial class MainWindow : Window, IAsyncDisposable
{
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private readonly DashboardController _controller;
    private readonly ApplicationPreferences _preferences;
    private readonly List<PreviewBinding> _previewBindings = [];
    private DashboardScreenConfiguration[] _screens = DashboardModuleCatalog.CreateEmptyScreens();
    private HardwareSnapshot _previewSnapshot = new();
    private CancellationTokenSource? _operationSource;
    private int _selectedScreenIndex;
    private int _modalScreenIndex;
    private bool _isBuildingModal;
    private bool _allowClose;
    private bool _suppressDirtyTracking;
    private bool _disposed;

    public MainWindow() : this(new DashboardController(), ApplicationPreferencesStore.Load())
    {
    }

    internal MainWindow(DashboardController controller, ApplicationPreferences preferences)
    {
        _controller = controller;
        _preferences = preferences;
        _controller.SetSensorRefreshSeconds(_preferences.SensorRefreshSeconds);
        InitializeComponent();
        _controller.SnapshotUpdated += Controller_SnapshotUpdated;
        _controller.ConnectionStateChanged += Controller_ConnectionStateChanged;
        _controller.ProgressChanged += Controller_ProgressChanged;
        _controller.ListenAddressChanged += Controller_ListenAddressChanged;
        LoadSavedConfiguration();
        RenderActiveScreens();
        AttachDirtyTracking();
        MaxHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(Height, MaxHeight);
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && ModalOverlay.Visibility == Visibility.Visible)
            {
                CloseModal();
                args.Handled = true;
            }
        };
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    public bool LivePreviewEnabled { get; set; } = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && LivePreviewEnabled && _preferences.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && LivePreviewEnabled && _preferences.MinimizeToTray)
        {
            HideToTray();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            new WindowInteropHelper(this).Handle,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));
    }

    private void WindowHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplicationSettingsButton_Click(object sender, RoutedEventArgs e) =>
        OpenApplicationSettings();

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void HideToTray()
    {
        Hide();
    }

    public void RequestApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    public async Task ReconnectAsync()
    {
        _operationSource?.Cancel();
        _operationSource?.Dispose();
        _operationSource = new CancellationTokenSource();
        SetUiBusy(true);
        try
        {
            await _controller.ReconnectNowAsync(_operationSource.Token);
        }
        catch (OperationCanceledException)
        {
            SetError("Reconnect operation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    public void SaveScreenshot(string path)
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public void SavePublicScreenshot(string path)
    {
        _suppressDirtyTracking = true;
        try
        {
            _screens = DashboardModuleCatalog.CreateDefaults();
            _selectedScreenIndex = 0;
            _previewSnapshot = new HardwareSnapshot
            {
                CpuLoadPercent = 43,
                CpuTemperatureCelsius = 56,
                CpuPowerWatts = 52,
                GpuLoadPercent = 66,
                GpuTemperatureCelsius = 57,
                GpuPowerWatts = 125,
                RamLoadPercent = 51,
                RamUsedGigabytes = 16.1,
                RamTotalGigabytes = 31.6,
                RamAvailableGigabytes = 15.5,
                Uptime = TimeSpan.FromHours(8) + TimeSpan.FromMinutes(24)
            };
            DeviceIpTextBox.Text = "192.0.2.181";
            TokenPasswordBox.Password = string.Empty;
            ServeIpTextBox.Text = "192.0.2.159";
            BrightnessTextBox.Text = "70";
            RenderActiveScreens();
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        SaveScreenshot(path);
    }

    public void SaveWidgetPickerScreenshot(string path)
    {
        _screens = DashboardModuleCatalog.CreateEmptyScreens();
        _selectedScreenIndex = 0;
        RenderActiveScreens();
        OpenWidgetPicker(0);
        SaveScreenshot(path);
    }

    public void SaveWidgetSettingsScreenshot(string path)
    {
        _screens = DashboardModuleCatalog.CreateEmptyScreens();
        _screens[0] = DashboardModuleCatalog.GetRequired("gpu").CreateScreen();
        _selectedScreenIndex = 0;
        RenderActiveScreens();
        OpenWidgetSettings(0);
        SaveScreenshot(path);
    }

    public void SaveClockSettingsScreenshot(string path)
    {
        _screens = DashboardModuleCatalog.CreateEmptyScreens();
        _screens[0] = DashboardModuleCatalog.GetRequired("clock_large").CreateScreen();
        _selectedScreenIndex = 0;
        RenderActiveScreens();
        OpenWidgetSettings(0);
        SaveScreenshot(path);
    }

    public void SaveApplicationSettingsScreenshot(string path)
    {
        OpenApplicationSettings();
        SaveScreenshot(path);
    }

    public void SaveCpuSensorAccessScreenshot(string path)
    {
        OpenCpuSensorFirstRunPrompt();
        SaveScreenshot(path);
    }

    public void SaveTokenErrorScreenshot(string path)
    {
        if (!TryReadConnectionSettings(out _, out _, out _, out _, out var error))
        {
            SetError(error);
            OpenTokenRequiredPrompt();
        }

        SaveScreenshot(path);
    }

    public void SaveMediaImportScreenshot(string path)
    {
        _screens = DashboardModuleCatalog.CreateEmptyScreens();
        _selectedScreenIndex = 0;
        RenderActiveScreens();
        OpenMediaImport(0);
        SaveScreenshot(path);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.SnapshotUpdated -= Controller_SnapshotUpdated;
        _controller.ConnectionStateChanged -= Controller_ConnectionStateChanged;
        _controller.ProgressChanged -= Controller_ProgressChanged;
        _controller.ListenAddressChanged -= Controller_ListenAddressChanged;
        _operationSource?.Cancel();
        _operationSource?.Dispose();
        await _controller.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void OpenApplicationSettings()
    {
        ShowModal(
            "TGm SETTINGS",
            "Configure background behavior and connection recovery for this Windows account.");
        _isBuildingModal = true;
        try
        {
            var settings = new StackPanel();
            settings.Children.Add(BuildPreferenceToggle(
                "MINIMIZE TO TRAY",
                "The close and minimize buttons keep monitoring active in the notification area.",
                _preferences.MinimizeToTray,
                enabled => UpdatePreferences(preferences => preferences.MinimizeToTray = enabled)));
            settings.Children.Add(BuildPreferenceToggle(
                "START MINIMIZED",
                "Open TGm directly in the notification area.",
                _preferences.StartMinimized,
                enabled => UpdatePreferences(preferences => preferences.StartMinimized = enabled)));
            settings.Children.Add(BuildPreferenceToggle(
                "START WITH WINDOWS",
                "Uses the current-user startup entry and does not require administrator rights.",
                _preferences.StartWithWindows,
                enabled =>
                {
                    AutostartService.SetEnabled(enabled);
                    UpdatePreferences(preferences => preferences.StartWithWindows = enabled);
                }));
            settings.Children.Add(BuildPreferenceToggle(
                "AUTOMATIC RECONNECT",
                "Restore the last complete dashboard after the device or network returns.",
                _preferences.AutoReconnect,
                enabled =>
                {
                    _controller.SetAutoReconnect(enabled);
                    UpdatePreferences(preferences => preferences.AutoReconnect = enabled);
                }));

            settings.Children.Add(BuildSensorRefreshSettings());
            settings.Children.Add(BuildCpuSensorAccessSettings());

            var utilityButtons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 14)
            };
            var openLogs = SecondaryButton("OPEN LOGS");
            openLogs.Margin = new Thickness(0, 0, 8, 0);
            openLogs.Click += (_, _) =>
            {
                Directory.CreateDirectory(AppDataPaths.Logs);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppDataPaths.Logs,
                    UseShellExecute = true
                });
            };
            utilityButtons.Children.Add(openLogs);

            var exportDiagnostics = SecondaryButton("EXPORT DIAGNOSTICS");
            exportDiagnostics.Click += (_, _) => ExportDiagnostics();
            utilityButtons.Children.Add(exportDiagnostics);
            settings.Children.Add(utilityButtons);

            settings.Children.Add(new Border
            {
                Padding = new Thickness(12),
                Background = ThemeBrush("WindowBrush"),
                CornerRadius = new CornerRadius(0),
                Child = new TextBlock
                {
                    Text = "Unofficial monitor for Divoom Times Gate.\nThis project is not affiliated with or endorsed by Divoom.",
                    Foreground = ThemeBrush("MutedTextBrush"),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                }
            });
            ModalContentPanel.Children.Add(settings);
        }
        finally
        {
            _isBuildingModal = false;
        }
    }

    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export sanitized TGm diagnostics",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"TGm-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            DiagnosticBundleExporter.Export(dialog.FileName, _preferences);
            FooterTitleTextBlock.Text = "Sanitized diagnostics exported";
            FooterDetailTextBlock.Text = "LocalToken is excluded; IP addresses and personal paths are redacted.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError($"Could not export diagnostics: {exception.Message}");
        }
    }

    private Border BuildPreferenceToggle(
        string title,
        string description,
        bool initialValue,
        Action<bool> changed)
    {
        var panel = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = UiLabel(title),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 4, 16, 0)
        });
        layout.Children.Add(text);

        var toggle = new CheckBox
        {
            IsChecked = initialValue,
            Style = FindResource("ToggleSwitchStyle") as Style,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Checked += (_, _) => Apply(true);
        toggle.Unchecked += (_, _) => Apply(false);
        Grid.SetColumn(toggle, 1);
        layout.Children.Add(toggle);
        panel.Child = layout;
        return panel;

        void Apply(bool enabled)
        {
            if (_isBuildingModal)
            {
                return;
            }

            try
            {
                changed(enabled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                toggle.IsChecked = !enabled;
                SetError(exception.Message);
            }
        }
    }

    private void UpdatePreferences(Action<ApplicationPreferences> update)
    {
        update(_preferences);
        ApplicationPreferencesStore.Save(_preferences);
    }

    private Border BuildSensorRefreshSettings()
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = "SENSOR REFRESH",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11
        });
        text.Children.Add(new TextBlock
        {
            Text = "Minimum interval for hardware values. Lower values are more responsive but create more device requests. Apply All is required.",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 430,
            Margin = new Thickness(0, 4, 16, 0)
        });
        layout.Children.Add(text);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var slider = new Slider
        {
            Minimum = 5,
            Maximum = 30,
            TickFrequency = 5,
            SmallChange = 5,
            LargeChange = 5,
            IsSnapToTickEnabled = true,
            Width = 180,
            Value = _preferences.SensorRefreshSeconds,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "5 · 10 · 15 · 20 · 25 · 30 seconds"
        };
        var valueLabel = new TextBlock
        {
            Text = $"{_preferences.SensorRefreshSeconds} S",
            Foreground = ThemeBrush("TextBrush"),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Width = 42,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        slider.ValueChanged += (_, _) =>
        {
            var seconds = (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero);
            valueLabel.Text = $"{seconds} S";
            if (_isBuildingModal)
            {
                return;
            }

            _controller.SetSensorRefreshSeconds(seconds);
            UpdatePreferences(preferences => preferences.SensorRefreshSeconds = seconds);
            MarkDirty();
        };
        controls.Children.Add(slider);
        controls.Children.Add(valueLabel);
        Grid.SetColumn(controls, 1);
        layout.Children.Add(controls);
        container.Child = layout;
        return container;
    }

    private Border BuildCpuSensorAccessSettings()
    {
        var installed = SensorHostManager.IsInstalled();
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = "CPU sensor access",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });
        text.Children.Add(new TextBlock
        {
            Text = installed
                ? "Enabled · the elevated reader runs only while TGm requests data."
                : "Optional · enables CPU temperature, power and clock after one UAC approval.",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 4, 16, 0)
        });
        layout.Children.Add(text);

        var action = installed ? SecondaryButton("REMOVE") : PrimaryButton("ENABLE");
        action.IsEnabled = installed || SensorHostManager.IsAvailable;
        action.VerticalAlignment = VerticalAlignment.Center;
        action.Click += async (_, _) =>
        {
            action.IsEnabled = false;
            var result = installed
                ? await SensorHostManager.UninstallAsync()
                : await SensorHostManager.InstallAsync();
            UpdatePreferences(preferences =>
            {
                preferences.CpuSensorPromptCompleted = result.IsSuccess || result.WasCancelled;
                preferences.CpuSensorHostEnabled = !installed && result.IsSuccess;
                if (result.IsSuccess || result.WasCancelled)
                {
                    preferences.CpuSensorSetupVersion = SensorHostManager.CurrentSetupVersion;
                }
            });
            CloseModal();
            FooterTitleTextBlock.Text = result.IsSuccess
                ? (installed ? "CPU sensor access removed" : "CPU sensor access enabled")
                : "CPU sensor access unchanged";
            FooterDetailTextBlock.Text = result.Message;
        };
        Grid.SetColumn(action, 1);
        layout.Children.Add(action);
        container.Child = layout;
        return container;
    }

    private void LoadSavedConfiguration()
    {
        if (LocalTokenCredentialStore.TryRead(out var token))
        {
            TokenPasswordBox.Password = token;
        }

        var saved = DashboardConfigurationStore.TryLoad();
        if (saved is null)
        {
            ServeIpTextBox.Text = LocalIpv4AddressDetector.TryGetPreferredAddress() ?? string.Empty;
            return;
        }

        _screens = saved.Screens.Select(screen => screen.Clone()).ToArray();
        DeviceIpTextBox.Text = saved.DeviceIp;
        ServeIpTextBox.Text = LocalIpv4AddressDetector.TryGetPreferredAddress(saved.DeviceIp) ?? saved.ServeIp;
        BrightnessTextBox.Text = saved.Brightness.ToString(CultureInfo.InvariantCulture);
    }

    private void RenderActiveScreens()
    {
        _previewBindings.Clear();
        ActiveScreensPanel.Children.Clear();
        for (var index = 0; index < _screens.Length; index++)
        {
            var screenIndex = index;
            var screen = _screens[index];
            var hasWidget = screen.WidgetKey is not null ||
                            screen.SolidColorHex is not null ||
                            screen.CustomMediaPath is not null;
            var configurable = IsConfigurable(screen);

            var card = new Border
            {
                Width = 151,
                Height = 175,
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(8),
                Background = index == _selectedScreenIndex ? ThemeBrush("SelectedBrush") : ThemeBrush("WindowBrush"),
                BorderBrush = index == _selectedScreenIndex ? ThemeBrush("PrimaryBrush") : ThemeBrush("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Cursor = Cursors.Hand
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            layout.RowDefinitions.Add(new RowDefinition());

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"{(index == _selectedScreenIndex ? ">" : " ")} LCD{index + 1}",
                Foreground = index == _selectedScreenIndex ? ThemeBrush("TextBrush") : ThemeBrush("MutedTextBrush"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var settingsButton = new Button
            {
                Content = BuildSettingsGlyph(),
                Width = 22,
                Height = 25,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = ThemeBrush("MutedTextBrush"),
                BorderThickness = new Thickness(0),
                Style = FindResource("SecondaryButtonStyle") as Style,
                Cursor = configurable ? Cursors.Hand : Cursors.Arrow,
                IsEnabled = configurable,
                Opacity = configurable ? 1 : 0.25,
                ToolTip = configurable ? "Widget settings" : "This widget has no settings"
            };
            settingsButton.Click += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                _selectedScreenIndex = screenIndex;
                OpenWidgetSettings(screenIndex);
                RenderActiveScreens();
            };
            Grid.SetColumn(settingsButton, 1);
            header.Children.Add(settingsButton);
            layout.Children.Add(header);

            FrameworkElement preview = hasWidget
                ? BuildScreenPreview(screen)
                : BuildEmptyScreenPreview();
            preview.Width = 125;
            preview.Height = 125;
            Grid.SetRow(preview, 1);
            layout.Children.Add(preview);
            card.Child = layout;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _selectedScreenIndex = screenIndex;
                RenderActiveScreens();
                OpenWidgetPicker(screenIndex);
            };
            EnableTileKeyboard(card, () => { _selectedScreenIndex = screenIndex; RenderActiveScreens(); OpenWidgetPicker(screenIndex); }, $"Choose widget for LCD {index + 1}");
            ActiveScreensPanel.Children.Add(card);
        }
    }

    private Border BuildScreenPreview(DashboardScreenConfiguration screen)
    {
        if (screen.CustomMediaPath is not null)
        {
            var border = PreviewBorder(Brush("#050806"));
            border.Child = BuildMediaPreview(screen.CustomMediaPath);
            border.ClipToBounds = true;
            return border;
        }

        if (screen.SolidColorHex is not null)
        {
            var border = PreviewBorder(Brush(screen.SolidColorHex));
            border.Child = CenteredMessage(DisplayTitle(screen.Title), "#FFFFFF");
            return border;
        }

        var terminal = PreviewBorder(Brush("#050806"));
        terminal.BorderBrush = Brush(screen.FrameColorHex);
        terminal.Padding = new Thickness(7, 6, 6, 6);
        var stack = new StackPanel();
        stack.Children.Add(BuildPreviewHeader(screen));
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = MutedBrush(screen.FrameColorHex),
            Margin = new Thickness(0, 4, 0, 7)
        });

        if (string.Equals(screen.WidgetKey, "clock_large", StringComparison.Ordinal))
        {
            var clock = new TextBlock
            {
                Text = DashboardWidgetValueFormatter.LargeClock(screen.ClockShowSeconds),
                Foreground = Brush(screen.TextColorHex),
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = screen.ClockShowSeconds ? 17 : 21,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 19, 0, 0)
            };
            _previewBindings.Add(new PreviewBinding(
                clock,
                _ => DashboardWidgetValueFormatter.LargeClock(screen.ClockShowSeconds)));
            stack.Children.Add(clock);
            stack.Children.Add(new TextBlock
            {
                Text = "LOCAL TIME",
                Foreground = MutedBrush(screen.FrameColorHex),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });
            terminal.Child = stack;
            return terminal;
        }

        foreach (var assignment in screen.Slots)
        {
            var value = new TextBlock
            {
                Text = assignment is null
                    ? "> [ EMPTY ]"
                    : $"> {DashboardWidgetValueFormatter.Format(assignment.WidgetKey, assignment.DisplayMode, _previewSnapshot)}",
                Foreground = assignment is null ? MutedBrush(screen.FrameColorHex) : Brush(screen.TextColorHex),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8.5,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            if (assignment is not null)
            {
                var capturedAssignment = assignment;
                _previewBindings.Add(new PreviewBinding(
                    value,
                    snapshot => $"> {DashboardWidgetValueFormatter.Format(
                        capturedAssignment.WidgetKey,
                        capturedAssignment.DisplayMode,
                        snapshot)}"));
            }

            stack.Children.Add(value);
        }

        terminal.Child = stack;
        return terminal;
    }

    private static Viewbox BuildSettingsGlyph()
    {
        return new Viewbox
        {
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            Child = new System.Windows.Shapes.Path
            {
                Data = (Geometry)System.Windows.Application.Current.FindResource("SettingsGearGeometry"),
                Fill = ThemeBrush("MutedTextBrush")
            }
        };
    }

    private static StackPanel BuildPreviewHeader(DashboardScreenConfiguration screen)
    {
        var icon = DashboardHeaderIconCatalog.GetRequired(screen.HeaderIconKey);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = icon.Symbol,
            Foreground = Brush(icon.FixedColorHex ?? screen.TextColorHex),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = icon.Symbol.Length > 1 ? 9 : 11,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = DisplayTitle(screen.Title),
            Foreground = Brush(screen.TextColorHex),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 89,
            VerticalAlignment = VerticalAlignment.Center
        });
        return header;
    }

    private static Grid BuildEmptyScreenPreview()
    {
        var grid = new Grid { Margin = new Thickness(1) };
        grid.Children.Add(new Rectangle
        {
            Stroke = ThemeBrush("PrimaryBrush"),
            StrokeThickness = 1.5,
            StrokeDashArray = [5, 4],
            RadiusX = 0, RadiusY = 0,
            Opacity = 0.7
        });
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = "+",
            Foreground = ThemeBrush("TextBrush"),
            FontSize = 36,
            FontWeight = FontWeights.Light,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Add widget",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        grid.Children.Add(content);
        return grid;
    }

    private void OpenWidgetPicker(int screenIndex)
    {
        _modalScreenIndex = screenIndex;
        ShowModal(
            $"SELECT WIDGET · SCREEN {screenIndex + 1}",
            "Choose a widget to place on this physical Times Gate display.");

        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var preset in DashboardModuleCatalog.All)
        {
            panel.Children.Add(BuildWidgetPickerTile(preset));
        }

        panel.Children.Add(BuildCustomWidgetTile());
        ModalContentPanel.Children.Add(panel);
    }

    private Border BuildWidgetPickerTile(DashboardModuleDefinition preset)
    {
        var accent = Brush(preset.AccentHex);
        var tile = new Border
        {
            Width = 158,
            Height = 238,
            Margin = new Thickness(0, 0, 12, 6),
            Padding = new Thickness(10),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Cursor = Cursors.Hand
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        layout.Children.Add(new TextBlock
        {
            Text = preset.Title,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = ThemeBrush("TextBrush")
        });

        FrameworkElement preview;
        if (preset.IsSolidColor)
        {
            preview = new Border
            {
                Background = accent,
                CornerRadius = new CornerRadius(0),
                Child = CenteredMessage(preset.Title, "#FFFFFF")
            };
        }
        else
        {
            var screen = preset.CreateScreen();
            preview = BuildScreenPreview(screen);
            preview.Width = 125;
            preview.Height = 125;
            preview.HorizontalAlignment = HorizontalAlignment.Center;
            preview.VerticalAlignment = VerticalAlignment.Center;
        }

        Grid.SetRow(preview, 1);
        layout.Children.Add(preview);
        var description = new TextBlock
        {
            Text = preset.Description,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 2);
        layout.Children.Add(description);
        tile.Child = layout;
        tile.MouseEnter += (_, _) => tile.BorderBrush = ThemeBrush("PrimaryBrush");
        tile.MouseLeave += (_, _) => tile.BorderBrush = ThemeBrush("LineBrush");
        tile.MouseLeftButtonUp += (_, _) => SelectPreset(preset);
        EnableTileKeyboard(tile, () => SelectPreset(preset), preset.Title);
        return tile;
    }

    private Border BuildCustomWidgetTile()
    {
        var tile = new Border
        {
            Width = 158,
            Height = 238,
            Margin = new Thickness(0, 0, 12, 6),
            Padding = new Thickness(10),
            Background = ThemeBrush("PanelBrush"),
            CornerRadius = new CornerRadius(0),
            Cursor = Cursors.Hand
        };
        var grid = new Grid
        {
            Width = 125,
            Height = 125,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(new Rectangle
        {
            Stroke = ThemeBrush("MutedTextBrush"),
            StrokeThickness = 1.5,
            StrokeDashArray = [5, 4],
            RadiusX = 0, RadiusY = 0
        });
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = "+",
            Foreground = ThemeBrush("TextBrush"),
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Custom media",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "PNG · JPG · GIF",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        grid.Children.Add(content);
        tile.Child = grid;
        tile.MouseLeftButtonUp += (_, _) => OpenMediaImport(_modalScreenIndex);
        EnableTileKeyboard(tile, () => OpenMediaImport(_modalScreenIndex), "Custom media");
        return tile;
    }

    private void SelectPreset(DashboardModuleDefinition preset)
    {
        _screens[_modalScreenIndex] = preset.CreateScreen();
        _selectedScreenIndex = _modalScreenIndex;
        MarkDirty();
        RenderActiveScreens();
        CloseModal();
    }

    private void OpenWidgetSettings(int screenIndex)
    {
        var screen = _screens[screenIndex];
        if (screen.CustomMediaPath is not null)
        {
            OpenMediaImport(screenIndex);
            return;
        }

        if (!IsConfigurable(screen))
        {
            return;
        }

        _modalScreenIndex = screenIndex;
        ShowModal(
            $"WIDGET SETTINGS · SCREEN {screenIndex + 1}",
            "Configure appearance and the live network-text rows inside this widget.");
        _isBuildingModal = true;
        try
        {
            var titlePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            titlePanel.Children.Add(FieldLabel("WIDGET TITLE"));
            var titleBox = new TextBox
            {
                Padding = new Thickness(6, 5, 6, 5),
                Text = screen.Title,
                MaxLength = 12,
                FontFamily = new FontFamily("Consolas")
            };
            titleBox.TextChanged += (_, _) =>
            {
                if (_isBuildingModal)
                {
                    return;
                }

                screen.Title = titleBox.Text;
                MarkDirty();
                RenderActiveScreens();
            };
            titlePanel.Children.Add(titleBox);
            ModalContentPanel.Children.Add(titlePanel);
            ModalContentPanel.Children.Add(BuildAppearanceSettings(screen));

            if (string.Equals(screen.WidgetKey, "clock_large", StringComparison.Ordinal))
            {
                ModalContentPanel.Children.Add(BuildClockSettings(screen));
            }
            else
            {
                for (var slotIndex = 0; slotIndex < screen.Slots.Length; slotIndex++)
                {
                    ModalContentPanel.Children.Add(BuildSettingRow(screen, slotIndex));
                }
            }
        }
        finally
        {
            _isBuildingModal = false;
        }
    }

    private Border BuildAppearanceSettings(DashboardScreenConfiguration screen)
    {
        var panel = new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

        var frameEditor = BuildColorEditor(
            "FRAME COLOR",
            screen.FrameColorHex,
            color => screen.FrameColorHex = color);
        frameEditor.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(frameEditor);

        var textEditor = BuildColorEditor(
            "TEXT COLOR",
            screen.TextColorHex,
            color => screen.TextColorHex = color);
        textEditor.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(textEditor, 1);
        grid.Children.Add(textEditor);

        var iconEditor = new StackPanel();
        iconEditor.Children.Add(FieldLabel("HEADER ICON"));
        var iconCombo = new ComboBox
        {
            Height = 34,
            Style = FindResource("DarkComboBoxStyle") as Style,
            ItemTemplate = FindResource("WidgetOptionTemplate") as DataTemplate
        };
        foreach (var icon in DashboardHeaderIconCatalog.All)
        {
            iconCombo.Items.Add(new WidgetOption(icon.Key, icon.Label));
        }

        iconCombo.SelectedIndex = DashboardHeaderIconCatalog.All
            .ToList()
            .FindIndex(icon => icon.Key == screen.HeaderIconKey);
        if (iconCombo.SelectedIndex < 0)
        {
            iconCombo.SelectedIndex = 0;
        }

        iconCombo.SelectionChanged += (_, _) =>
        {
            if (_isBuildingModal || iconCombo.SelectedItem is not WidgetOption selected || selected.Key is null)
            {
                return;
            }

            screen.HeaderIconKey = selected.Key;
            MarkDirty();
            RenderActiveScreens();
        };
        iconEditor.Children.Add(iconCombo);
        Grid.SetColumn(iconEditor, 2);
        grid.Children.Add(iconEditor);
        panel.Child = grid;
        return panel;
    }

    private Border BuildClockSettings(DashboardScreenConfiguration screen)
    {
        var panel = new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var description = new StackPanel();
        description.Children.Add(new TextBlock
        {
            Text = "CLOCK FORMAT",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        description.Children.Add(new TextBlock
        {
            Text = "The device renders time locally; seconds do not poll this PC.",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0)
        });
        layout.Children.Add(description);

        var secondsToggle = new CheckBox
        {
            Content = "SHOW SECONDS · HH:mm:ss",
            IsChecked = screen.ClockShowSeconds,
            Style = FindResource("ToggleSwitchStyle") as Style,
            VerticalAlignment = VerticalAlignment.Center
        };
        secondsToggle.Checked += (_, _) => SetSeconds(true);
        secondsToggle.Unchecked += (_, _) => SetSeconds(false);
        Grid.SetColumn(secondsToggle, 1);
        layout.Children.Add(secondsToggle);
        panel.Child = layout;
        return panel;

        void SetSeconds(bool showSeconds)
        {
            if (_isBuildingModal || screen.ClockShowSeconds == showSeconds)
            {
                return;
            }

            screen.ClockShowSeconds = showSeconds;
            MarkDirty();
            RenderActiveScreens();
        }
    }

    private StackPanel BuildColorEditor(
        string label,
        string currentColor,
        Action<string> applyColor)
    {
        string[] palette = ["#55FF88", "#55D8FF", "#FFE100", "#FF7A55", "#D67BFF", "#FFFFFF"];
        const double spectrumWidth = 158;
        const double spectrumHeight = 104;
        const double hueHeight = 104;
        var editor = new StackPanel();
        editor.Children.Add(FieldLabel(label));
        var currentBrush = new Border
        {
            Width = 24,
            Height = 24,
            Background = Brush(currentColor),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var colorButton = new Button
        {
            Width = 38,
            Height = 34,
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = currentBrush,
            Style = FindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Choose color"
        };
        var colorCode = new TextBlock
        {
            Text = currentColor,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var summary = new StackPanel { Orientation = Orientation.Horizontal };
        summary.Children.Add(colorButton);
        summary.Children.Add(colorCode);
        editor.Children.Add(summary);

        var hexBox = new TextBox
        {
            Text = currentColor,
            Width = 92,
            Height = 30,
            Padding = new Thickness(3, 4, 3, 4),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14
        };
        var redBox = BuildRgbBox();
        var greenBox = BuildRgbBox();
        var blueBox = BuildRgbBox();
        var selectedPreview = new Border
        {
            Width = 38,
            Height = spectrumHeight,
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var spectrumHue = new Border
        {
            Width = spectrumWidth,
            Height = spectrumHeight,
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true
        };
        var whiteOverlay = new Border
        {
            Background = new LinearGradientBrush(
                Colors.White,
                Color.FromArgb(0, 255, 255, 255),
                new Point(0, 0.5),
                new Point(1, 0.5))
        };
        var blackOverlay = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(0, 0, 0, 0),
                Colors.Black,
                new Point(0.5, 0),
                new Point(0.5, 1))
        };
        var spectrumMarker = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Transparent,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        var spectrum = new Canvas
        {
            Width = spectrumWidth,
            Height = spectrumHeight,
            Cursor = Cursors.Cross,
            ClipToBounds = true
        };
        spectrum.Children.Add(spectrumHue);
        spectrum.Children.Add(whiteOverlay);
        spectrum.Children.Add(blackOverlay);
        spectrum.Children.Add(spectrumMarker);

        var hueBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        hueBrush.GradientStops.Add(new GradientStop(Colors.Red, 0));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Yellow, 1d / 6d));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Lime, 2d / 6d));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Cyan, 3d / 6d));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Blue, 4d / 6d));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Magenta, 5d / 6d));
        hueBrush.GradientStops.Add(new GradientStop(Colors.Red, 1));
        var hueMarker = new Border
        {
            Width = 20,
            Height = 4,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        var hueSlider = new Canvas
        {
            Width = 16,
            Height = hueHeight,
            Background = hueBrush,
            Cursor = Cursors.Hand,
            ClipToBounds = false
        };
        hueSlider.Children.Add(hueMarker);

        var isSynchronizing = false;
        double currentHue = 0;
        double currentSaturation = 0;
        double currentValue = 1;

        void UpdatePickerVisuals(Color color)
        {
            ColorToHsv(color, out currentHue, out currentSaturation, out currentValue);
            spectrumHue.Background = new SolidColorBrush(ColorFromHsv(currentHue, 1, 1));
            Canvas.SetLeft(spectrumMarker, Math.Clamp(currentSaturation * spectrumWidth - 5, -1, spectrumWidth - 9));
            Canvas.SetTop(spectrumMarker, Math.Clamp((1 - currentValue) * spectrumHeight - 5, -1, spectrumHeight - 9));
            Canvas.SetTop(hueMarker, Math.Clamp((currentHue / 360d) * hueHeight - 2, -1, hueHeight - 3));
        }

        void CommitColor(string color)
        {
            if (!TryNormalizeColor(color, out var normalized))
            {
                return;
            }

            var parsed = (SolidColorBrush)Brush(normalized);
            isSynchronizing = true;
            hexBox.Text = normalized;
            redBox.Text = parsed.Color.R.ToString(CultureInfo.InvariantCulture);
            greenBox.Text = parsed.Color.G.ToString(CultureInfo.InvariantCulture);
            blueBox.Text = parsed.Color.B.ToString(CultureInfo.InvariantCulture);
            colorCode.Text = normalized;
            currentBrush.Background = parsed;
            selectedPreview.Background = parsed;
            colorButton.ToolTip = normalized;
            UpdatePickerVisuals(parsed.Color);
            isSynchronizing = false;

            if (_isBuildingModal)
            {
                return;
            }

            applyColor(normalized);
            MarkDirty();
            RenderActiveScreens();
        }

        void CommitRgb()
        {
            if (isSynchronizing ||
                !byte.TryParse(redBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var red) ||
                !byte.TryParse(greenBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var green) ||
                !byte.TryParse(blueBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue))
            {
                return;
            }

            CommitColor($"#{red:X2}{green:X2}{blue:X2}");
        }

        void PickSpectrum(Point point)
        {
            currentSaturation = Math.Clamp(point.X / spectrumWidth, 0, 1);
            currentValue = 1 - Math.Clamp(point.Y / spectrumHeight, 0, 1);
            var color = ColorFromHsv(currentHue, currentSaturation, currentValue);
            CommitColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        void PickHue(Point point)
        {
            currentHue = Math.Clamp(point.Y / hueHeight, 0, 1) * 360d;
            var color = ColorFromHsv(currentHue, currentSaturation, currentValue);
            CommitColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        hexBox.TextChanged += (_, _) =>
        {
            if (!isSynchronizing && TryNormalizeColor(hexBox.Text, out var color))
            {
                CommitColor(color);
            }
        };
        redBox.TextChanged += (_, _) => CommitRgb();
        greenBox.TextChanged += (_, _) => CommitRgb();
        blueBox.TextChanged += (_, _) => CommitRgb();

        spectrum.MouseLeftButtonDown += (_, e) =>
        {
            spectrum.CaptureMouse();
            PickSpectrum(e.GetPosition(spectrum));
        };
        spectrum.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && spectrum.IsMouseCaptured)
            {
                PickSpectrum(e.GetPosition(spectrum));
            }
        };
        spectrum.MouseLeftButtonUp += (_, e) =>
        {
            PickSpectrum(e.GetPosition(spectrum));
            spectrum.ReleaseMouseCapture();
        };
        hueSlider.MouseLeftButtonDown += (_, e) =>
        {
            hueSlider.CaptureMouse();
            PickHue(e.GetPosition(hueSlider));
        };
        hueSlider.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && hueSlider.IsMouseCaptured)
            {
                PickHue(e.GetPosition(hueSlider));
            }
        };
        hueSlider.MouseLeftButtonUp += (_, e) =>
        {
            PickHue(e.GetPosition(hueSlider));
            hueSlider.ReleaseMouseCapture();
        };

        var popupContent = new StackPanel();
        var pickerRow = new Grid();
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spectrumWidth) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        pickerRow.Children.Add(spectrum);
        Grid.SetColumn(hueSlider, 1);
        hueSlider.HorizontalAlignment = HorizontalAlignment.Center;
        pickerRow.Children.Add(hueSlider);
        Grid.SetColumn(selectedPreview, 2);
        pickerRow.Children.Add(selectedPreview);
        popupContent.Children.Add(pickerRow);

        var valuesRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        valuesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        valuesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        valuesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        valuesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        valuesRow.Children.Add(BuildColorValueField("HEX", hexBox, 6));
        var redField = BuildColorValueField("R", redBox, 6);
        Grid.SetColumn(redField, 1);
        valuesRow.Children.Add(redField);
        var greenField = BuildColorValueField("G", greenBox, 6);
        Grid.SetColumn(greenField, 2);
        valuesRow.Children.Add(greenField);
        var blueField = BuildColorValueField("B", blueBox, 0);
        Grid.SetColumn(blueField, 3);
        valuesRow.Children.Add(blueField);
        popupContent.Children.Add(valuesRow);
        popupContent.Children.Add(new TextBlock
        {
            Text = "PRESETS",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 5)
        });
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in palette)
        {
            var capturedColor = color;
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 7, 0),
                Background = Brush(color),
                BorderBrush = ThemeBrush("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Cursor = Cursors.Hand,
                ToolTip = color
            };
            swatch.MouseLeftButtonUp += (_, _) => CommitColor(capturedColor);
            presets.Children.Add(swatch);
        }

        popupContent.Children.Add(presets);
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = colorButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            VerticalOffset = 5,
            Child = new Border
            {
                Padding = new Thickness(14),
                Background = ThemeBrush("PanelBrush"),
                BorderBrush = ThemeBrush("PrimaryBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Child = popupContent
            }
        };
        colorButton.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        CommitColor(currentColor);
        return editor;

        static TextBox BuildRgbBox() => new()
        {
            Width = 42,
            Height = 30,
            MaxLength = 3,
            Padding = new Thickness(3, 4, 3, 4),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14
        };

        static StackPanel BuildColorValueField(string channel, TextBox input, double rightMargin)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, rightMargin, 0) };
            field.Children.Add(new TextBlock
            {
                Text = channel,
                Foreground = ThemeBrush("MutedTextBrush"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });
            field.Children.Add(input);
            return field;
        }

        static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            var red = color.R / 255d;
            var green = color.G / 255d;
            var blue = color.B / 255d;
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var delta = max - min;

            hue = delta == 0
                ? 0
                : max == red
                    ? 60 * (((green - blue) / delta) % 6)
                    : max == green
                        ? 60 * (((blue - red) / delta) + 2)
                        : 60 * (((red - green) / delta) + 4);
            if (hue < 0)
            {
                hue += 360;
            }

            saturation = max == 0 ? 0 : delta / max;
            value = max;
        }

        static Color ColorFromHsv(double hue, double saturation, double value)
        {
            hue = hue >= 360 ? 0 : Math.Max(0, hue);
            saturation = Math.Clamp(saturation, 0, 1);
            value = Math.Clamp(value, 0, 1);
            var chroma = value * saturation;
            var section = hue / 60d;
            var x = chroma * (1 - Math.Abs((section % 2) - 1));
            var match = value - chroma;
            var (red, green, blue) = section switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };

            return Color.FromRgb(
                (byte)Math.Round((red + match) * 255),
                (byte)Math.Round((green + match) * 255),
                (byte)Math.Round((blue + match) * 255));
        }
    }

    private Border BuildSettingRow(DashboardScreenConfiguration screen, int slotIndex)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12, 10, 12, 10),
            Background = ThemeBrush("WindowBrush"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.Children.Add(new TextBlock
        {
            Text = $"ROW {slotIndex + 1}",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        });

        var combo = new ComboBox
        {
            Height = 34,
            Margin = new Thickness(0, 0, 10, 0),
            Style = FindResource("DarkComboBoxStyle") as Style,
            ItemTemplate = FindResource("WidgetOptionTemplate") as DataTemplate
        };
        combo.Items.Add(new WidgetOption(null, "— EMPTY —"));
        foreach (var widget in DashboardWidgetCatalog.All)
        {
            combo.Items.Add(new WidgetOption(widget.Key, $"{widget.Title}  ·  {widget.PreviewValue}"));
        }

        var assignment = screen.Slots[slotIndex];
        combo.SelectedIndex = assignment is null
            ? 0
            : DashboardWidgetCatalog.All.ToList().FindIndex(widget => widget.Key == assignment.WidgetKey) + 1;
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);

        var modeButton = SecondaryButton("123 %");
        modeButton.Width = 82;
        modeButton.HorizontalAlignment = HorizontalAlignment.Right;
        UpdateModeButton(modeButton, assignment);
        modeButton.Click += (_, _) =>
        {
            var current = screen.Slots[slotIndex];
            if (current is null || !DashboardWidgetCatalog.GetRequired(current.WidgetKey).SupportsBar)
            {
                return;
            }

            screen.Slots[slotIndex] = current with
            {
                DisplayMode = current.DisplayMode == DashboardWidgetDisplayMode.Bar
                    ? DashboardWidgetDisplayMode.Value
                    : DashboardWidgetDisplayMode.Bar
            };
            UpdateModeButton(modeButton, screen.Slots[slotIndex]);
            MarkDirty();
            RenderActiveScreens();
        };
        Grid.SetColumn(modeButton, 2);
        grid.Children.Add(modeButton);

        combo.SelectionChanged += (_, _) =>
        {
            if (_isBuildingModal || combo.SelectedItem is not WidgetOption selected)
            {
                return;
            }

            var key = selected.Key;
            screen.Slots[slotIndex] = key is null ? null : new DashboardWidgetAssignment(key);
            screen.SolidColorHex = null;
            screen.CustomMediaPath = null;
            UpdateModeButton(modeButton, screen.Slots[slotIndex]);
            MarkDirty();
            RenderActiveScreens();
        };

        row.Child = grid;
        return row;
    }

    private void OpenMediaImport(int screenIndex)
    {
        _modalScreenIndex = screenIndex;
        var currentScreen = _screens[screenIndex];
        var selectedPath = currentScreen.CustomMediaPath;
        ShowModal(
            $"IMPORT CUSTOM WIDGET · SCREEN {screenIndex + 1}",
            "Images are cropped to 128×128. Animated GIF frames are resized while preserving their timing.");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        var preview = new Border
        {
            Width = 180,
            Height = 180,
            Background = Brush("#050806"),
            BorderBrush = ThemeBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true,
            Child = BuildMediaPreview(selectedPath)
        };
        layout.Children.Add(preview);

        var controls = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        controls.Children.Add(FieldLabel("MEDIA FILE"));
        var pathText = new TextBlock
        {
            Text = selectedPath is null ? "No file selected" : System.IO.Path.GetFileName(selectedPath),
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        controls.Children.Add(pathText);

        var chooseButton = SecondaryButton("CHOOSE IMAGE OR GIF");
        chooseButton.HorizontalAlignment = HorizontalAlignment.Left;
        controls.Children.Add(chooseButton);

        var hint = new TextBlock
        {
            Text = "Supported: PNG, JPG, JPEG, BMP and GIF. The imported file is copied into the TimesGate Monitor profile.",
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 18)
        };
        controls.Children.Add(hint);

        var importButton = PrimaryButton(currentScreen.CustomMediaPath is null ? "IMPORT WIDGET" : "REPLACE WIDGET");
        importButton.HorizontalAlignment = HorizontalAlignment.Left;
        importButton.IsEnabled = selectedPath is not null;
        controls.Children.Add(importButton);

        chooseButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a Times Gate widget image",
                Filter = "Supported images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|PNG|*.png|JPEG|*.jpg;*.jpeg|Bitmap|*.bmp|GIF|*.gif",
                Multiselect = false,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            selectedPath = dialog.FileName;
            pathText.Text = System.IO.Path.GetFileName(selectedPath);
            preview.Child = BuildMediaPreview(selectedPath);
            importButton.IsEnabled = true;
        };

        importButton.Click += (_, _) =>
        {
            if (selectedPath is null)
            {
                return;
            }

            try
            {
                _ = CustomMediaBackgroundRenderer.Render(selectedPath);
                var importedPath = string.Equals(selectedPath, currentScreen.CustomMediaPath, StringComparison.OrdinalIgnoreCase)
                    ? selectedPath
                    : DashboardConfigurationStore.ImportMedia(selectedPath);
                currentScreen.WidgetKey = "custom";
                currentScreen.Title = ShortMediaTitle(selectedPath);
                currentScreen.Slots = new DashboardWidgetAssignment?[3];
                currentScreen.SolidColorHex = null;
                currentScreen.CustomMediaPath = importedPath;
                _selectedScreenIndex = screenIndex;
                MarkDirty();
                RenderActiveScreens();
                CloseModal();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                FooterTitleTextBlock.Text = "Could not import custom widget";
                FooterDetailTextBlock.Text = exception.Message;
                StatusTextBlock.Text = "IMPORT ERROR";
                StatusDot.Fill = ThemeBrush("TextBrush");
            }
        };

        Grid.SetColumn(controls, 1);
        layout.Children.Add(controls);
        ModalContentPanel.Children.Add(layout);
    }

    private void ShowModal(string title, string subtitle)
    {
        _isBuildingModal = true;
        ModalTitleTextBlock.Text = UiLabel(title);
        ModalSubtitleTextBlock.Text = subtitle;
        ModalContentPanel.Children.Clear();
        MainContent.IsEnabled = false;
        ModalOverlay.Visibility = Visibility.Visible;
        ModalCloseButton.Focus();
        _isBuildingModal = false;
    }

    private void CloseModal()
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
        MainContent.IsEnabled = true;
        ApplyButton.Focus();
        ModalContentPanel.Children.Clear();
    }

    private void ModalOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ModalOverlay))
        {
            CloseModal();
        }
    }

    private void ModalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void ModalCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseModal();
    }

    private void AttachDirtyTracking()
    {
        DeviceIpTextBox.TextChanged += (_, _) => MarkDirtyUnlessSuppressed();
        TokenPasswordBox.PasswordChanged += (_, _) =>
        {
            TokenValidationTextBlock.Visibility = Visibility.Collapsed;
            TokenPasswordBox.BorderBrush = ThemeBrush("LineBrush");
            MarkDirtyUnlessSuppressed();
        };
        ServeIpTextBox.TextChanged += (_, _) => MarkDirtyUnlessSuppressed();
        BrightnessTextBox.TextChanged += (_, _) => MarkDirtyUnlessSuppressed();
        PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape && ModalOverlay.Visibility == Visibility.Visible)
            {
                CloseModal();
                eventArgs.Handled = true;
            }
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!LivePreviewEnabled)
        {
            return;
        }

        try
        {
            await _controller.StartPreviewAsync();
            _previewSnapshot = _controller.LatestSnapshot;
            UpdatePreviewValues();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Closing the window while hardware initializes is a normal shutdown path.
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or IOException or
                NotSupportedException or TypeInitializationException)
        {
            FooterTitleTextBlock.Text = "Live preview is unavailable";
            FooterDetailTextBlock.Text = exception.Message;
        }

        if (!_preferences.CpuSensorPromptCompleted)
        {
            OpenCpuSensorFirstRunPrompt();
        }
    }

    private void OpenCpuSensorFirstRunPrompt()
    {
        ShowModal(
            "OPTIONAL CPU SENSOR ACCESS",
            "Choose whether TGm may read low-level CPU sensors on this PC.");

        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = "CPU load works without administrator rights. CPU temperature, power and clock require a small elevated sensor reader because PawnIO blocks ordinary applications. Windows will ask for approval once; TGm itself will continue to start without UAC.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 12,
            LineHeight = 18,
            Margin = new Thickness(0, 4, 0, 18)
        });
        layout.Children.Add(new TextBlock
        {
            Text = "If you continue without access, the unavailable CPU values will be shown as --. You can enable them later from the gear menu.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var skip = SecondaryButton("CONTINUE WITHOUT");
        skip.Margin = new Thickness(0, 0, 10, 0);
        skip.Click += (_, _) =>
        {
            UpdatePreferences(preferences =>
            {
                preferences.CpuSensorPromptCompleted = true;
                preferences.CpuSensorHostEnabled = false;
                preferences.CpuSensorSetupVersion = SensorHostManager.CurrentSetupVersion;
            });
            CloseModal();
        };
        var enable = PrimaryButton("ENABLE CPU SENSORS");
        enable.IsEnabled = SensorHostManager.IsAvailable;
        enable.Click += async (_, _) =>
        {
            enable.IsEnabled = false;
            skip.IsEnabled = false;
            var result = await SensorHostManager.InstallAsync();
            UpdatePreferences(preferences =>
            {
                preferences.CpuSensorPromptCompleted = result.IsSuccess || result.WasCancelled;
                preferences.CpuSensorHostEnabled = result.IsSuccess;
                if (result.IsSuccess || result.WasCancelled)
                {
                    preferences.CpuSensorSetupVersion = SensorHostManager.CurrentSetupVersion;
                }
            });
            CloseModal();
            FooterTitleTextBlock.Text = result.IsSuccess
                ? "CPU sensor access enabled"
                : "Continuing without low-level CPU sensors";
            FooterDetailTextBlock.Text = result.Message;
        };
        buttons.Children.Add(skip);
        buttons.Children.Add(enable);
        layout.Children.Add(buttons);
        ModalContentPanel.Children.Add(layout);
    }

    private void Controller_SnapshotUpdated(HardwareSnapshot snapshot)
    {
        if (_disposed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            _previewSnapshot = snapshot;
            UpdatePreviewValues();
        });
    }

    private void Controller_ConnectionStateChanged(DashboardConnectionState state, string message)
    {
        if (_disposed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            StatusTextBlock.Text = state switch
            {
                DashboardConnectionState.Live => "LIVE · 5 SCREENS",
                DashboardConnectionState.Applying => "APPLYING",
                DashboardConnectionState.Reconnecting => "RECONNECTING",
                DashboardConnectionState.Paused => "PAUSED",
                DashboardConnectionState.Error => "CONNECTION ERROR",
                _ => "READY"
            };
            StatusDot.Fill = state switch
            {
                DashboardConnectionState.Live => ThemeBrush("TextBrush"),
                DashboardConnectionState.Applying or DashboardConnectionState.Reconnecting => ThemeBrush("MutedTextBrush"),
                DashboardConnectionState.Error => ThemeBrush("TextBrush"),
                _ => ThemeBrush("MutedTextBrush")
            };
            if (state is DashboardConnectionState.Reconnecting or DashboardConnectionState.Paused)
            {
                FooterTitleTextBlock.Text = message;
            }
        });
    }

    private void Controller_ProgressChanged(string message)
    {
        if (_disposed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (message.Contains("RESTOR", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("APPLY", StringComparison.OrdinalIgnoreCase))
            {
                FooterDetailTextBlock.Text = message;
            }
        });
    }

    private void Controller_ListenAddressChanged(IPAddress address)
    {
        if (_disposed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            _suppressDirtyTracking = true;
            try
            {
                ServeIpTextBox.Text = address.ToString();
                PersistConfiguration();
            }
            finally
            {
                _suppressDirtyTracking = false;
            }
        });
    }

    private void UpdatePreviewValues()
    {
        foreach (var binding in _previewBindings)
        {
            binding.Target.Text = binding.Formatter(_previewSnapshot);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _screens = DashboardModuleCatalog.CreateEmptyScreens();
        _selectedScreenIndex = 0;
        CloseModal();
        MarkDirty();
        RenderActiveScreens();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyConfigurationAsync(null);
    }

    private async void ApplySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyConfigurationAsync(_selectedScreenIndex);
    }

    private async Task ApplyConfigurationAsync(int? onlyScreenIndex)
    {
        if (!TryReadConnectionSettings(
                out var deviceHost,
                out var localToken,
                out var brightness,
                out var listenAddress,
                out var error))
        {
            SetError(error);
            if (string.IsNullOrWhiteSpace(TokenPasswordBox.Password))
            {
                OpenTokenRequiredPrompt();
            }
            return;
        }

        CloseModal();
        _operationSource?.Cancel();
        _operationSource?.Dispose();
        _operationSource = new CancellationTokenSource();
        SetUiBusy(true);
        var progress = new Progress<string>(message =>
        {
            StatusTextBlock.Text = message;
            StatusDot.Fill = ThemeBrush("MutedTextBrush");
            FooterTitleTextBlock.Text = message;
            FooterDetailTextBlock.Text = "Keep this application open after the configuration is applied.";
        });

        try
        {
            await _controller.ApplyAsync(
                _screens.Select(screen => screen.Clone()).ToArray(),
                deviceHost,
                localToken,
                brightness,
                listenAddress,
                8765,
                onlyScreenIndex,
                progress,
                _operationSource.Token,
                forceApply: true);

            var tokenSaved = TrySaveLocalToken();
            PersistConfiguration();
            StatusDot.Fill = ThemeBrush("TextBrush");
            if (onlyScreenIndex is int screenIndex)
            {
                StatusTextBlock.Text = $"LIVE · LCD {screenIndex + 1} APPLIED";
                FooterTitleTextBlock.Text = $"Screen {screenIndex + 1} passed Safe Apply";
                FooterDetailTextBlock.Text = tokenSaved
                    ? "Verify this LCD, then use Apply All to resend the complete dashboard."
                    : "Screen applied, but Windows Credential Manager could not save LocalToken.";
                ApplyButton.Content = "Apply all";
            }
            else
            {
                StatusTextBlock.Text = "LIVE · 5 SCREENS";
                FooterTitleTextBlock.Text = "Dashboard is live";
                FooterDetailTextBlock.Text = tokenSaved
                    ? "Use Reapply All if one or more LCDs stop responding."
                    : "Dashboard is live, but Windows Credential Manager could not save LocalToken.";
                ApplyButton.Content = "Reapply all";
            }
        }
        catch (OperationCanceledException)
        {
            SetError("Apply operation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private bool TryReadConnectionSettings(
        out string deviceHost,
        out int localToken,
        out int brightness,
        out IPAddress listenAddress,
        out string error)
    {
        deviceHost = DeviceIpTextBox.Text.Trim();
        localToken = 0;
        brightness = 0;
        listenAddress = IPAddress.None;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(deviceHost))
        {
            error = "Enter the Times Gate IP address.";
            return false;
        }

        if (!ConnectionSettingsValidator.TryParseLocalToken(
                TokenPasswordBox.Password,
                out localToken,
                out error))
        {
            TokenValidationTextBlock.Text = error;
            TokenValidationTextBlock.Visibility = Visibility.Visible;
            TokenPasswordBox.BorderBrush = ThemeBrush("TextBrush");
            TokenPasswordBox.Focus();
            return false;
        }

        if (!IPAddress.TryParse(ServeIpTextBox.Text.Trim(), out var parsedAddress) ||
            parsedAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = "Enter this PC's LAN IPv4 address.";
            return false;
        }

        listenAddress = parsedAddress;
        if (!int.TryParse(BrightnessTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out brightness) ||
            brightness is < 0 or > 100)
        {
            error = "Brightness must be between 0 and 100.";
            return false;
        }

        return true;
    }

    private void OpenTokenRequiredPrompt()
    {
        ShowModal(
            "LOCAL TOKEN REQUIRED",
            "TGm cannot send a configuration to Times Gate without the device token.");
        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = "Open the Divoom mobile app, select your Times Gate and copy LocalToken from the device settings. The token is used only for local LAN requests and is not saved to disk.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
            FontSize = 12,
            LineHeight = 18,
            Margin = new Thickness(0, 4, 0, 20)
        });
        var enter = PrimaryButton("ENTER TOKEN");
        enter.HorizontalAlignment = HorizontalAlignment.Right;
        enter.Click += (_, _) =>
        {
            CloseModal();
            TokenPasswordBox.Focus();
        };
        layout.Children.Add(enter);
        ModalContentPanel.Children.Add(layout);
    }

    private void MarkDirty()
    {
        ApplyButton.Content = "Apply all";
        ApplyButton.IsEnabled = true;
        FooterTitleTextBlock.Text = "Dashboard has unapplied changes";
        FooterDetailTextBlock.Text = "The layout is saved locally; Apply All sends all five screens.";
        PersistConfiguration();

        if (_controller.IsLive)
        {
            StatusTextBlock.Text = "LIVE · CHANGES PENDING";
            StatusDot.Fill = ThemeBrush("MutedTextBrush");
        }
    }

    private void MarkDirtyUnlessSuppressed()
    {
        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }
    }

    private void PersistConfiguration()
    {
        try
        {
            _ = int.TryParse(BrightnessTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var brightness);
            DashboardConfigurationStore.Save(new DashboardUserConfiguration
            {
                Version = 2,
                DeviceIp = DeviceIpTextBox.Text.Trim(),
                ServeIp = ServeIpTextBox.Text.Trim(),
                Brightness = Math.Clamp(brightness, 0, 100),
                Screens = _screens.Select(screen => screen.Clone()).ToArray()
            });
        }
        catch (IOException)
        {
            // A locked configuration file must not interrupt editing.
        }
        catch (UnauthorizedAccessException)
        {
            // Editing remains available even if the profile cannot be written.
        }
    }

    private bool TrySaveLocalToken()
    {
        try
        {
            LocalTokenCredentialStore.Save(TokenPasswordBox.Password);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or ExternalException)
        {
            return false;
        }
    }

    private void SetUiBusy(bool isBusy)
    {
        ApplyButton.IsEnabled = !isBusy;
        ApplySelectedButton.IsEnabled = !isBusy;
        ResetButton.IsEnabled = !isBusy;
        ActiveScreensPanel.IsEnabled = !isBusy;
        SettingsPanel.IsEnabled = !isBusy;
    }

    private void SetError(string message)
    {
        StatusTextBlock.Text = "ERROR";
        StatusDot.Fill = ThemeBrush("TextBrush");
        FooterTitleTextBlock.Text = "Could not apply dashboard";
        FooterDetailTextBlock.Text = message;
        ApplyButton.Content = "Retry apply";
        ApplyButton.IsEnabled = true;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }

    private static bool IsConfigurable(DashboardScreenConfiguration screen)
    {
        if (screen.CustomMediaPath is not null)
        {
            return true;
        }

        return screen.WidgetKey is not null &&
               DashboardModuleCatalog.All.Any(preset =>
                   preset.Key == screen.WidgetKey && !preset.IsSolidColor);
    }

    private static void UpdateModeButton(Button button, DashboardWidgetAssignment? assignment)
    {
        if (assignment is null)
        {
            button.Content = "123 %";
            button.IsEnabled = false;
            button.Opacity = 0.35;
            return;
        }

        var supportsBar = DashboardWidgetCatalog.GetRequired(assignment.WidgetKey).SupportsBar;
        button.Content = assignment.DisplayMode == DashboardWidgetDisplayMode.Bar ? "▦ BAR" : "123 %";
        button.IsEnabled = supportsBar;
        button.Opacity = supportsBar ? 1 : 0.35;
    }

    private static Border PreviewBorder(Brush background) => new()
    {
        Background = background,
        BorderBrush = ThemeBrush("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(0)
    };

    private static TextBlock CenteredMessage(string text, string color) => new()
    {
        Text = text,
        Foreground = Brush(color),
        FontFamily = new FontFamily("Consolas"),
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = UiLabel(text),
        Foreground = ThemeBrush("MutedTextBrush"),
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 5)
    };

    private Button SecondaryButton(string content) => new()
    {
        Content = UiLabel(content),
        Style = FindResource("SecondaryButtonStyle") as Style
    };

    private Button PrimaryButton(string content) => new()
    {
        Content = UiLabel(content),
        Style = FindResource("PrimaryButtonStyle") as Style
    };

    private static FrameworkElement BuildMediaPreview(string? path)
    {
        if (path is not null &&
            string.Equals(System.IO.Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            var animation = AnimatedGifImage.TryCreate(path);
            if (animation is not null)
            {
                animation.Stretch = Stretch.UniformToFill;
                return animation;
            }
        }

        var bitmap = path is null ? null : TryLoadBitmap(path);
        return bitmap is null
            ? CenteredMessage("NO PREVIEW", "#80848E")
            : new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
    }

    private static BitmapFrame? TryLoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ShortMediaTitle(string path)
    {
        var title = System.IO.Path.GetFileNameWithoutExtension(path).Trim().ToUpperInvariant();
        return title.Length <= 12 ? title : title[..12];
    }

    private static string DisplayTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "SCREEN" : title.Trim().ToUpperInvariant();

    private static Brush ThemeBrush(string key) =>
        (Brush)System.Windows.Application.Current.FindResource(key);

    private static string UiLabel(string value) => value.Length == 0 ? value :
        (char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()).Replace("Tgm", "TGm", StringComparison.Ordinal).Replace("Cpu", "CPU", StringComparison.Ordinal).Replace("gif", "GIF", StringComparison.Ordinal).Replace("cpu", "CPU", StringComparison.Ordinal).Replace("windows", "Windows", StringComparison.Ordinal);

    private static void EnableTileKeyboard(Border tile, Action activate, string label)
    {
        tile.Focusable = true;
        tile.FocusVisualStyle = (Style)System.Windows.Application.Current.FindResource("TerminalFocus");
        System.Windows.Automation.AutomationProperties.SetName(tile, label);
        tile.KeyDown += (_, args) =>
        {
            if (args.OriginalSource == tile && args.Key is Key.Enter or Key.Space)
            {
                args.Handled = true;
                activate();
            }
        };
        var restingBorder = tile.BorderBrush;
        tile.GotKeyboardFocus += (_, _) => tile.BorderBrush = ThemeBrush("PrimaryBrush");
        tile.LostKeyboardFocus += (_, _) => tile.BorderBrush = restingBorder;
    }
    private static SolidColorBrush Brush(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Invalid color '{hex}'.");
        }

        var color = Color.FromRgb(
            byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MutedBrush(string hex)
    {
        var baseBrush = Brush(hex);
        var color = baseBrush.Color;
        var muted = Color.FromRgb(
            (byte)Math.Max(24, color.R * 0.43),
            (byte)Math.Max(32, color.G * 0.43),
            (byte)Math.Max(28, color.B * 0.43));
        var brush = new SolidColorBrush(muted);
        brush.Freeze();
        return brush;
    }

    private static bool TryNormalizeColor(string value, out string color)
    {
        color = value.Trim().ToUpperInvariant();
        return color.Length == 7 &&
               color[0] == '#' &&
               int.TryParse(
                   color.AsSpan(1),
                   NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture,
               out _);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
