namespace TimesGateMonitor.App.Dashboard;

public enum DashboardWidgetDisplayMode
{
    Value,
    Bar
}

public sealed record DashboardWidgetAssignment(
    string WidgetKey,
    DashboardWidgetDisplayMode DisplayMode = DashboardWidgetDisplayMode.Value);

public sealed class DashboardScreenConfiguration
{
    public string Title { get; set; } = "SCREEN";

    public string? WidgetKey { get; set; }

    public DashboardWidgetAssignment?[] Slots { get; set; } = new DashboardWidgetAssignment?[3];

    public string? SolidColorHex { get; set; }

    public string? CustomMediaPath { get; set; }

    public string FrameColorHex { get; set; } = "#55FF88";

    public string TextColorHex { get; set; } = "#55FF88";

    public string HeaderIconKey { get; set; } = "dollar";

    public bool ClockShowSeconds { get; set; }

    public DashboardScreenConfiguration Clone() => new()
    {
        Title = Title,
        WidgetKey = WidgetKey,
        Slots = Slots.ToArray(),
        SolidColorHex = SolidColorHex,
        CustomMediaPath = CustomMediaPath,
        FrameColorHex = FrameColorHex,
        TextColorHex = TextColorHex,
        HeaderIconKey = HeaderIconKey,
        ClockShowSeconds = ClockShowSeconds
    };
}

public sealed record DashboardWidgetDefinition(
    string Key,
    string Title,
    string Description,
    string PreviewValue,
    string ValuePath,
    string? PreviewBar,
    string? BarValuePath,
    int UpdateSeconds)
{
    public bool SupportsBar => BarValuePath is not null;

    public string Preview(DashboardWidgetDisplayMode mode) =>
        mode == DashboardWidgetDisplayMode.Bar && PreviewBar is not null
            ? PreviewBar
            : PreviewValue;

    public string Path(DashboardWidgetDisplayMode mode) =>
        mode == DashboardWidgetDisplayMode.Bar && BarValuePath is not null
            ? BarValuePath
            : ValuePath;
}

public static class DashboardWidgetCatalog
{
    public static IReadOnlyList<DashboardWidgetDefinition> All { get; } =
    [
        new("gpu_load", "GPU LOAD", "Graphics load", "GPU: 66%", "value/gpu/load", "GPU [■■■■□□]", "value/gpu/load-bar", 5),
        new("gpu_temp", "GPU TEMP", "Graphics temperature", "TEMP: 57C", "value/gpu/temp", null, null, 10),
        new("gpu_power", "GPU POWER", "Current board power", "TDP: 125 W", "value/gpu/tdp", null, null, 10),
        new("cpu_load", "CPU LOAD", "Processor load", "CPU: 43%", "value/cpu/load", "CPU [■■■□□□]", "value/cpu/load-bar", 5),
        new("cpu_temp", "CPU TEMP", "Processor temperature", "TEMP: 56C", "value/cpu/temp", null, null, 10),
        new("cpu_power", "CPU POWER", "Current package power", "TDP: 52 W", "value/cpu/tdp", null, null, 10),
        new("cpu_summary", "CPU SUMMARY", "Load and temperature", "CPU: 43% 56C", "value/core/cpu", null, null, 5),
        new("gpu_summary", "GPU SUMMARY", "Load and temperature", "GPU: 66% 57C", "value/core/gpu", null, null, 5),
        new("ram_load", "RAM LOAD", "Memory load", "RAM: 51%", "value/ram/load", "RAM [■■■□□□]", "value/ram/load-bar", 10),
        new("ram_used", "RAM USED", "Used and total memory", "RAM: 16.1/31.6", "value/core/ram", null, null, 10),
        new("ram_free", "RAM FREE", "Available memory", "FREE 15.5G", "value/ram/free", null, null, 10),
        new("clock", "CLOCK", "Local time", "TIME: 22:14", "value/system/time", null, null, 60),
        new("date", "DATE", "Local date", "DATE: 29.08.2026", "value/system/date", null, null, 60),
        new("uptime", "UPTIME", "Windows uptime", "UP 02:31", "value/system/up", null, null, 15),
        new("temperatures", "TEMPS", "CPU and GPU temperature", "TEMP C56 G57", "value/system/temp", null, null, 15)
    ];

    public static DashboardWidgetDefinition GetRequired(string key) =>
        All.FirstOrDefault(widget => string.Equals(widget.Key, key, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Dashboard widget '{key}' is not registered.");
}

public sealed record DashboardHeaderIconDefinition(
    string Key,
    string Label,
    string Symbol,
    string? FixedColorHex = null);

public static class DashboardHeaderIconCatalog
{
    public static IReadOnlyList<DashboardHeaderIconDefinition> All { get; } =
    [
        new("dollar", "DOLLAR  $", "$"),
        new("smile", "SMILE  :)", ":)"),
        new("wink", "WINK  ;)", ";)"),
        new("terminal", "TERMINAL  >_", ">_")
    ];

    public static DashboardHeaderIconDefinition GetRequired(string key) =>
        All.FirstOrDefault(icon => string.Equals(icon.Key, key, StringComparison.Ordinal))
        ?? All[0];
}

public sealed record DashboardModuleDefinition(
    string Key,
    string Title,
    string Description,
    string AccentHex,
    IReadOnlyList<DashboardWidgetAssignment> Widgets,
    bool IsSolidColor = false)
{
    public DashboardScreenConfiguration CreateScreen()
    {
        var slots = new DashboardWidgetAssignment?[3];
        for (var index = 0; index < Math.Min(slots.Length, Widgets.Count); index++)
        {
            slots[index] = Widgets[index];
        }

        return new DashboardScreenConfiguration
        {
            Title = Title,
            WidgetKey = Key,
            Slots = slots,
            SolidColorHex = IsSolidColor ? AccentHex : null,
            FrameColorHex = AccentHex,
            TextColorHex = AccentHex
        };
    }
}

public static class DashboardModuleCatalog
{
    public static IReadOnlyList<DashboardModuleDefinition> All { get; } =
    [
        new("gpu", "GPU", "GPU preset", "#55FF88",
            [new("gpu_load"), new("gpu_temp"), new("gpu_power")]),
        new("cpu", "CPU", "CPU preset", "#55FF88",
            [new("cpu_load"), new("cpu_temp"), new("cpu_power")]),
        new("core", "CORE", "CPU · GPU · memory", "#55FF88",
            [new("cpu_summary"), new("gpu_summary"), new("ram_used")]),
        new("ram", "RAM", "Memory preset", "#55FF88",
            [new("ram_load", DashboardWidgetDisplayMode.Bar), new("ram_used"), new("ram_free")]),
        new("system", "SYSTEM", "Clock · uptime · temperatures", "#55FF88",
            [new("clock"), new("uptime"), new("temperatures")]),
        new("clock_large", "SMALL_CLOCK", "Native local time", "#55FF88", []),
        new("custom_layout", "CUSTOM_WIDGET", "Build your own widget", "#55FF88", [])
    ];

    public static IReadOnlyList<string> DefaultKeys { get; } =
        ["gpu", "cpu", "core", "ram", "system"];

    public static DashboardModuleDefinition GetRequired(string key) =>
        All.FirstOrDefault(module => string.Equals(module.Key, key, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Dashboard module '{key}' is not registered.");

    public static DashboardScreenConfiguration[] CreateDefaults() =>
        DefaultKeys.Select(key => GetRequired(key).CreateScreen()).ToArray();

    public static DashboardScreenConfiguration[] CreateEmptyScreens() =>
        Enumerable.Range(1, 5)
            .Select(index => new DashboardScreenConfiguration { Title = $"SCREEN_{index}" })
            .ToArray();
}
