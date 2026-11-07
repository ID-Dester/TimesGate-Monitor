using TimesGateMonitor.Core.Formatting;

namespace TimesGateMonitor.Tests.Formatting;

internal static class NetworkTextValueFormatterTests
{
    public static void Run()
    {
        TestAssert.Equal("47%", NetworkTextValueFormatter.Percent(47.4), "Percent formatting failed.");
        TestAssert.Equal("48C", NetworkTextValueFormatter.Temperature(47.6), "Temperature formatting failed.");
        TestAssert.Equal(
            "[■■■■■□□□□□]",
            NetworkTextValueFormatter.SquareBar(45),
            "Square progress bar formatting failed.");
        TestAssert.Equal(
            "[□□□□□□□□□□]",
            NetworkTextValueFormatter.SquareBar(-1),
            "Progress bar must clamp values below zero.");
        TestAssert.Equal(
            "[■■■■■■■■■■]",
            NetworkTextValueFormatter.SquareBar(101),
            "Progress bar must clamp values above 100.");
    }
}
