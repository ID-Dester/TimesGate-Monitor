using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Hardware.SensorHost;

namespace TimesGateMonitor.Tests.Hardware;

internal static class SensorHostClientTests
{
    public static void Run()
    {
        using var ready = new ManualResetEventSlim();
        var expected = new HardwareSnapshot
        {
            CapturedAt = DateTimeOffset.UnixEpoch,
            CpuLoadPercent = 42,
            CpuTemperatureCelsius = 61,
            CpuPowerWatts = 75
        };
        var serverTask = Task.Run(async () =>
        {
            await using var pipe = new NamedPipeServerStream(
                SensorHostEndpoint.PipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            ready.Set();
            await pipe.WaitForConnectionAsync();
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(expected));
        });

        TestAssert.True(ready.Wait(TimeSpan.FromSeconds(2)), "Sensor host test pipe did not start.");
        var client = new SensorHostClient();
        TestAssert.True(client.TryCapture(out var received), "Sensor host client did not receive a snapshot.");
        TestAssert.Equal(61d, received.CpuTemperatureCelsius!.Value, "CPU temperature IPC value changed.");
        TestAssert.True(serverTask.Wait(TimeSpan.FromSeconds(2)), "Sensor host test server did not finish.");
    }
}
