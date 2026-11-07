using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Tests.TimesGate;

internal static class TimesGateConnectionTests
{
    public static void Run()
    {
        var cases = new (string Host, string Expected)[]
        {
            ("192.168.1.42", "http://192.168.1.42/post"),
            ("192.168.1.42:9000", "http://192.168.1.42:9000/post"),
            ("http://timesgate.local:9000", "http://timesgate.local:9000/post")
        };

        foreach (var testCase in cases)
        {
            var connection = new TimesGateConnection(testCase.Host, 123456);
            TestAssert.Equal(
                testCase.Expected,
                connection.Endpoint.AbsoluteUri.TrimEnd('/'),
                "Times Gate endpoint was normalized incorrectly.");
        }
    }
}
