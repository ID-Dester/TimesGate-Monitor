using System.Globalization;
using System.Net;

namespace TimesGateMonitor.Diagnostic;

internal sealed record DiagnosticArguments(
    string? Host,
    int LocalToken,
    int Brightness,
    string? PreviewDirectory,
    bool RenderOnly,
    bool HardwareOnce,
    bool SensorDump,
    bool Monitor,
    int UpdateRateMilliseconds,
    int Cycles,
    bool TextDashboard,
    bool TextProofOfConcept,
    IPAddress? ServeAddress,
    int ServePort,
    int DurationSeconds,
    bool ShowHelp)
{
    private static readonly int[] AllowedUpdateRates = [500, 1000, 2000, 5000];

    public static DiagnosticArguments Parse(string[] args)
    {
        string? host = null;
        string? previewDirectory = null;
        var localToken = 0;
        var brightness = 70;
        var renderOnly = false;
        var hardwareOnce = false;
        var sensorDump = false;
        var monitor = false;
        var updateRateMilliseconds = 1000;
        var cycles = 0;
        var textProofOfConcept = false;
        var textDashboard = false;
        IPAddress? serveAddress = null;
        var servePort = 8765;
        var durationSeconds = 0;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--ip":
                case "--host":
                    host = ReadValue(args, ref index);
                    break;
                case "--token":
                    localToken = ParseInt(ReadValue(args, ref index), "--token");
                    break;
                case "--brightness":
                    brightness = ParseInt(ReadValue(args, ref index), "--brightness");
                    break;
                case "--preview-dir":
                    previewDirectory = ReadValue(args, ref index);
                    break;
                case "--render-only":
                    renderOnly = true;
                    break;
                case "--hardware-once":
                    hardwareOnce = true;
                    break;
                case "--sensor-dump":
                    sensorDump = true;
                    break;
                case "--monitor":
                    monitor = true;
                    break;
                case "--update-rate":
                    updateRateMilliseconds = ParseInt(ReadValue(args, ref index), "--update-rate");
                    break;
                case "--cycles":
                    cycles = ParseInt(ReadValue(args, ref index), "--cycles");
                    break;
                case "--text-poc":
                    textProofOfConcept = true;
                    break;
                case "--text-dashboard":
                    textDashboard = true;
                    break;
                case "--serve-ip":
                    var addressText = ReadValue(args, ref index);
                    if (!IPAddress.TryParse(addressText, out serveAddress) ||
                        serveAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        throw new ArgumentException("--serve-ip must be an IPv4 address reachable by Times Gate.");
                    }
                    break;
                case "--serve-port":
                    servePort = ParseInt(ReadValue(args, ref index), "--serve-port");
                    break;
                case "--duration":
                    durationSeconds = ParseInt(ReadValue(args, ref index), "--duration");
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(servePort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(servePort, 65_535);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationSeconds, 0);

        if (!AllowedUpdateRates.Contains(updateRateMilliseconds))
        {
            throw new ArgumentException("--update-rate must be one of: 500, 1000, 2000, 5000.");
        }

        var selectedModes = new[]
            { renderOnly, hardwareOnce, sensorDump, monitor, textProofOfConcept, textDashboard }
            .Count(value => value);
        if (selectedModes > 1)
        {
            throw new ArgumentException(
                "Choose only one mode: --render-only, --hardware-once, --sensor-dump, --monitor, --text-poc, or --text-dashboard.");
        }

        if ((textProofOfConcept || textDashboard) && serveAddress is null)
        {
            throw new ArgumentException("--serve-ip is required with a network-text mode.");
        }

        if (!renderOnly && !hardwareOnce && !sensorDump && !showHelp)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("--ip is required unless --render-only is used.");
            }

            if (localToken <= 0)
            {
                throw new ArgumentException("--token must be a positive integer.");
            }
        }

        return new DiagnosticArguments(
            host,
            localToken,
            brightness,
            previewDirectory,
            renderOnly,
            hardwareOnce,
            sensorDump,
            monitor,
            updateRateMilliseconds,
            cycles,
            textDashboard,
            textProofOfConcept,
            serveAddress,
            servePort,
            durationSeconds,
            showHelp);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (++index >= args.Length || args[index].StartsWith('-'))
        {
            throw new ArgumentException($"A value is required after {args[index - 1]}.");
        }

        return args[index];
    }

    private static int ParseInt(string value, string argumentName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{argumentName} must be an integer.");
        }

        return result;
    }
}
