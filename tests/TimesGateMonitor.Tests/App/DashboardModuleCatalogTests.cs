using TimesGateMonitor.App.Dashboard;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Tests.App;

internal static class DashboardModuleCatalogTests
{
    public static void Run()
    {
        TestAssert.Equal(7, DashboardModuleCatalog.All.Count, "The prototype palette must contain seven modules.");
        TestAssert.Equal(5, DashboardModuleCatalog.DefaultKeys.Count, "The default layout must contain five screens.");
        TestAssert.Equal(15, DashboardWidgetCatalog.All.Count, "The widget palette has an unexpected size.");
        TestAssert.Equal("GPU", DashboardModuleCatalog.GetRequired("gpu").Title, "GPU preset title is outdated.");
        TestAssert.Equal("CPU", DashboardModuleCatalog.GetRequired("cpu").Title, "CPU preset title is outdated.");
        TestAssert.Equal("RAM", DashboardModuleCatalog.GetRequired("ram").Title, "RAM preset title is outdated.");
        TestAssert.Equal(
            "SMALL_CLOCK",
            DashboardModuleCatalog.GetRequired("clock_large").Title,
            "Native clock preset title is outdated.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in DashboardModuleCatalog.All)
        {
            TestAssert.True(keys.Add(module.Key), $"Duplicate module key: {module.Key}.");
            TestAssert.Equal(
                module.Key is "clock_large" or "custom_layout" ? 0 : 3,
                module.Widgets.Count,
                $"Module {module.Key} has an unexpected field count.");
            TestAssert.True(!module.IsSolidColor, "Test-only solid-color modules must not ship in the palette.");

            foreach (var assignment in module.Widgets)
            {
                _ = DashboardWidgetCatalog.GetRequired(assignment.WidgetKey);
            }
        }

        TestAssert.Equal(4, DashboardHeaderIconCatalog.All.Count, "The header icon palette has an unexpected size.");
        TestAssert.False(
            DashboardHeaderIconCatalog.All.Any(icon => icon.Key == "helldiver"),
            "Removed third-party artwork must not remain in the public icon catalog.");

        var widgetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in DashboardWidgetCatalog.All)
        {
            TestAssert.True(widgetKeys.Add(widget.Key), $"Duplicate widget key: {widget.Key}.");
            TestAssert.True(!string.IsNullOrWhiteSpace(widget.ValuePath), $"Widget {widget.Key} needs a value endpoint.");
            TestAssert.Equal(widget.PreviewBar is not null, widget.SupportsBar, $"Widget {widget.Key} bar metadata is inconsistent.");
        }

        foreach (var key in DashboardModuleCatalog.DefaultKeys)
        {
            _ = DashboardModuleCatalog.GetRequired(key);
        }

        var defaults = DashboardModuleCatalog.CreateDefaults();
        TestAssert.Equal(5, defaults.Length, "Default configuration must contain five screens.");
        foreach (var screen in defaults)
        {
            TestAssert.Equal(3, screen.Slots.Length, "Every screen must expose three slots.");
            TestAssert.True(screen.WidgetKey is not null, "Every preset screen must retain its widget identity.");
        }

        var gpuItems = DashboardController.CreateItems(
            defaults[0],
            1,
            new Uri("http://192.0.2.10:8765/"));
        TestAssert.Equal(3, gpuItems.Count, "Each row must remain a separate field for firmware compatibility.");
        TestAssert.Equal(5, gpuItems[0].UpdateSeconds, "GPU must keep the RC4 five-second default.");
        TestAssert.True(
            gpuItems.All(item => item.TextHeight == 16),
            "Every network-text row must retain its own single-line field.");

        var relaxedGpuItems = DashboardController.CreateItems(
            defaults[0],
            1,
            new Uri("http://192.0.2.10:8765/"),
            20);
        TestAssert.True(
            relaxedGpuItems.All(item => item.UpdateSeconds == 20),
            "The user refresh floor must slow every sensor row.");

        var ramItems = DashboardController.CreateItems(
            defaults[3],
            4,
            new Uri("http://192.0.2.10:8765/"));
        TestAssert.Equal(10, ramItems[0].UpdateSeconds, "RAM must keep its ten-second default.");

        var systemItems = DashboardController.CreateItems(
            defaults[4],
            5,
            new Uri("http://192.0.2.10:8765/"));
        TestAssert.Equal(
            ScreenHeartbeatPolicy.HeartbeatUpdateSeconds,
            systemItems[0].UpdateSeconds,
            "The first system row must retain the LCD heartbeat interval.");
        TestAssert.True(
            systemItems.Skip(1).All(item => item.UpdateSeconds == 15),
            "The remaining system values must keep their fifteen-second default.");

        var empty = DashboardModuleCatalog.CreateEmptyScreens();
        TestAssert.Equal(5, empty.Length, "The first-run dashboard must expose five empty screens.");
        foreach (var screen in empty)
        {
            TestAssert.True(screen.WidgetKey is null, "A first-run screen must not contain a widget.");
            TestAssert.True(screen.Slots.All(slot => slot is null), "A first-run screen must not contain network fields.");
        }

        var textIds = new HashSet<int>();
        for (var index = 0; index < defaults.Length; index++)
        {
            var screenIndex = index + 1;
            var items = DashboardController.CreateItems(
                defaults[index],
                screenIndex,
                new Uri("http://192.0.2.10:8765/"));
            foreach (var item in items)
            {
                TestAssert.True(textIds.Add(item.TextId), $"Duplicate network text ID: {item.TextId}.");
                TestAssert.True(
                    item.TextId >= ((screenIndex - 1) * 3) + 1 && item.TextId <= screenIndex * 3,
                    $"Text ID {item.TextId} escaped the stable range for LCD {screenIndex}.");
                TestAssert.True(
                    item.ValueUrl.AbsolutePath.StartsWith($"/screen/{screenIndex}/", StringComparison.Ordinal),
                    $"LCD {screenIndex} does not use an individual heartbeat route.");
            }
        }

        TestAssert.True(
            DashboardController.ScreenEquals(defaults[0], defaults[0].Clone()),
            "Cloned screens must compare equal for changed-only apply.");
        var changed = defaults[0].Clone();
        changed.Slots[0] = new DashboardWidgetAssignment("clock");
        TestAssert.True(
            !DashboardController.ScreenEquals(defaults[0], changed),
            "A changed widget must be detected by safe apply.");

        var recolored = defaults[0].Clone();
        recolored.FrameColorHex = "#55D8FF";
        TestAssert.True(
            !DashboardController.ScreenEquals(defaults[0], recolored),
            "A changed frame color must be detected by safe apply.");

        var clock = DashboardModuleCatalog.GetRequired("clock_large").CreateScreen();
        var clockItems = DashboardController.CreateItems(clock, 5, new Uri("http://192.0.2.10:8765/"));
        TestAssert.Equal(2, clockItems.Count, "The native clock must include one invisible heartbeat field.");
        TestAssert.Equal(5, clockItems[0].ItemType, "The large clock must use the native HourMinute item.");
        TestAssert.Equal(42, clockItems[0].Font, "The large clock must use the documented Divoom time font.");
        TestAssert.Equal(60, clockItems[0].UpdateSeconds, "The large clock must update once per minute.");
        TestAssert.Equal(1, clockItems[0].Alignment, "The clock position must not depend on ignored firmware alignment.");
        TestAssert.Equal(44, clockItems[0].X, "The HH:mm clock must be manually centered.");
        TestAssert.Equal(40, clockItems[0].TextWidth, "The HH:mm field must match the documented 16px font geometry.");
        TestAssert.Equal(16, clockItems[0].TextHeight, "The native clock must use the documented 16px item height.");
        TestAssert.False(clockItems[0].RequiresNetworkValue, "The native clock must not poll the PC web server.");
        TestAssert.True(clockItems[1].RequiresNetworkValue, "The native clock needs an invisible LCD heartbeat.");
        TestAssert.Equal(
            ScreenHeartbeatPolicy.HeartbeatUpdateSeconds,
            clockItems[1].UpdateSeconds,
            "The invisible LCD heartbeat interval is incorrect.");

        clock.ClockShowSeconds = true;
        var secondsItems = DashboardController.CreateItems(clock, 5, new Uri("http://192.0.2.10:8765/"));
        TestAssert.Equal(6, secondsItems[0].ItemType, "Seconds mode must use the native HourMinuteSecond item.");
        TestAssert.Equal(1, secondsItems[0].UpdateSeconds, "Seconds mode must update locally every second.");
        TestAssert.Equal(32, secondsItems[0].X, "HH:mm:ss must be manually centered.");
        TestAssert.Equal(64, secondsItems[0].TextWidth, "HH:mm:ss must use the documented 64px field width.");

        var clockWithoutSeconds = clock.Clone();
        clockWithoutSeconds.ClockShowSeconds = false;
        TestAssert.True(
            !DashboardController.ScreenEquals(clock, clockWithoutSeconds),
            "Changing the clock seconds toggle must mark the screen as changed.");

        var customLayout = DashboardModuleCatalog.GetRequired("custom_layout").CreateScreen();
        TestAssert.True(customLayout.Slots.All(slot => slot is null), "Custom Widget must start with three empty slots.");
        TestAssert.True(
            DashboardWidgetValueFormatter.Format("date", DashboardWidgetDisplayMode.Value, new HardwareSnapshot())
                .StartsWith("DATE: ", StringComparison.Ordinal),
            "The mini date widget is not formatted.");

        var snapshot = new HardwareSnapshot
        {
            CpuLoadPercent = 42,
            CpuTemperatureCelsius = 56,
            RamUsedGigabytes = 16.1,
            RamTotalGigabytes = 31.6
        };
        TestAssert.Equal(
            "CPU: 42% 56C",
            DashboardWidgetValueFormatter.Format("cpu_summary", DashboardWidgetDisplayMode.Value, snapshot),
            "Live preview formatting diverged from the server value.");
        var custom = empty[0].Clone();
        custom.WidgetKey = "custom";
        custom.CustomMediaPath = @"C:\media\one.gif";
        var otherCustom = custom.Clone();
        otherCustom.CustomMediaPath = @"C:\media\two.gif";
        TestAssert.True(
            !DashboardController.ScreenEquals(custom, otherCustom),
            "A changed custom-media file must be detected by safe apply.");
    }
}
