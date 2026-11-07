using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Hardware.SensorHost;

public sealed class SensorHostClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);
    private readonly string _pipeName = SensorHostEndpoint.PipeName;

    public bool TryCapture(out HardwareSnapshot snapshot)
    {
        snapshot = new HardwareSnapshot();

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous);
            using var connectSource = new CancellationTokenSource(ConnectTimeout);
            pipe.ConnectAsync(connectSource.Token).GetAwaiter().GetResult();

            using var reader = new StreamReader(pipe);
            using var readSource = new CancellationTokenSource(ReadTimeout);
            var json = reader.ReadLineAsync(readSource.Token).AsTask().GetAwaiter().GetResult();
            var received = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<HardwareSnapshot>(json);
            if (received is null)
            {
                return false;
            }

            snapshot = received;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or OperationCanceledException or
                UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
