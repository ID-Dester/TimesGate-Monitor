using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.TimesGate.Api;

namespace TimesGateMonitor.TimesGate.Services;

public sealed partial class TimesGateService : ITimesGateService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TimesGateService> _logger;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Dictionary<int, string> _lastFrameHashes = [];
    private int _pictureId;
    private bool _disposed;

    public TimesGateService(HttpClient httpClient, ILogger<TimesGateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<TimesGateCommandResult> HealthCheckAsync(
        TimesGateConnection connection,
        CancellationToken cancellationToken) =>
        SendCommandAsync(
            connection,
            new Dictionary<string, object>
            {
                ["Command"] = TimesGateProtocol.GetAllConfiguration,
                ["LocalToken"] = connection.LocalToken
            },
            cancellationToken);

    public async Task<TimesGateCommandResult> InitializeFrameSequenceAsync(
        TimesGateConnection connection,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await SendCommandCoreAsync(
                connection,
                new Dictionary<string, object>
                {
                    ["Command"] = TimesGateProtocol.ResetHttpGifId,
                    ["LocalToken"] = connection.LocalToken
                },
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _pictureId = 0;
                _lastFrameHashes.Clear();
                LogFrameSequenceInitialized(_logger, connection.Host);
            }

            return result;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public Task<TimesGateCommandResult> SetBrightnessAsync(
        TimesGateConnection connection,
        int brightness,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, 100);

        return SendCommandAsync(
            connection,
            new Dictionary<string, object>
            {
                ["Command"] = TimesGateProtocol.SetBrightness,
                ["Brightness"] = brightness,
                ["LocalToken"] = connection.LocalToken
            },
            cancellationToken);
    }

    public async Task<TimesGateCommandResult> SendFrameAsync(
        TimesGateConnection connection,
        DisplayFrame frame,
        TimeSpan displayDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frameHash = Convert.ToHexString(SHA256.HashData(frame.JpegData.Span));
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_lastFrameHashes.TryGetValue(frame.ScreenIndex, out var previousHash) &&
                string.Equals(previousHash, frameHash, StringComparison.Ordinal))
            {
                return TimesGateCommandResult.Skipped();
            }

            var lcdArray = new int[DisplayFrame.ScreenCount];
            lcdArray[frame.ScreenIndex - 1] = 1;

            var command = new Dictionary<string, object>
            {
                ["Command"] = TimesGateProtocol.SendHttpGif,
                ["LcdArray"] = lcdArray,
                ["PicNum"] = 1,
                ["PicWidth"] = DisplayFrame.PixelSize,
                ["PicOffset"] = 0,
                ["PicID"] = checked(++_pictureId),
                ["PicSpeed"] = Math.Clamp((int)displayDuration.TotalMilliseconds, 100, 60_000),
                ["PicData"] = Convert.ToBase64String(frame.JpegData.Span),
                ["LocalToken"] = connection.LocalToken
            };

            var result = await SendCommandCoreAsync(connection, command, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _lastFrameHashes[frame.ScreenIndex] = frameHash;
            }

            return result;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public Task<TimesGateCommandResult> ConfigureNetworkTextScreenAsync(
        TimesGateConnection connection,
        int screenIndex,
        Uri backgroundImageUrl,
        IReadOnlyCollection<NetworkTextItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(screenIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screenIndex, DisplayFrame.ScreenCount);
        ArgumentNullException.ThrowIfNull(backgroundImageUrl);
        ArgumentNullException.ThrowIfNull(items);

        if (!backgroundImageUrl.IsAbsoluteUri || backgroundImageUrl.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("The Times Gate background must use an absolute HTTP URL.", nameof(backgroundImageUrl));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("At least one network text item is required.", nameof(items));
        }

        var itemPayloads = items.Select(item =>
        {
            ValidateNetworkTextItem(item);

            return new Dictionary<string, object>
            {
                ["TextId"] = item.TextId,
                ["type"] = item.ItemType,
                ["x"] = item.X,
                ["y"] = item.Y,
                ["dir"] = 0,
                ["font"] = item.Font,
                ["TextWidth"] = item.TextWidth,
                ["Textheight"] = item.TextHeight,
                ["TextString"] = item.TextString ?? item.ValueUrl.AbsoluteUri,
                ["speed"] = 100,
                ["color"] = item.Color,
                ["update_time"] = item.UpdateSeconds,
                ["align"] = item.Alignment
            };
        }).ToArray();

        return SendCommandAsync(
            connection,
            new Dictionary<string, object>
            {
                ["Command"] = "Draw/SendHttpItemList",
                ["LcdIndex"] = screenIndex - 1,
                ["NewFlag"] = 1,
                // The misspelling is part of the device protocol.
                ["BackgroudGif"] = backgroundImageUrl.AbsoluteUri,
                ["ItemList"] = itemPayloads,
                ["LocalToken"] = connection.LocalToken
            },
            cancellationToken);
    }

    private static void ValidateNetworkTextItem(NetworkTextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.TextId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.X, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item.X, DisplayFrame.PixelSize - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.Y, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item.Y, DisplayFrame.PixelSize - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.TextWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item.TextWidth, DisplayFrame.PixelSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.TextHeight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item.TextHeight, DisplayFrame.PixelSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.UpdateSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(item.Alignment, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item.Alignment, 3);

        if (item.ItemType is not (5 or 6 or 23))
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                "Only HourMinute (5), HourMinuteSecond (6), and NetText (23) item types are supported.");
        }

        if (item.RequiresNetworkValue &&
            (!item.ValueUrl.IsAbsoluteUri || item.ValueUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Network text values must use an absolute HTTP URL.", nameof(item));
        }

        if (item.Color.Length != 7 || item.Color[0] != '#' ||
            !int.TryParse(item.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("Network text color must use #RRGGBB format.", nameof(item));
        }
    }

    private async Task<TimesGateCommandResult> SendCommandAsync(
        TimesGateConnection connection,
        Dictionary<string, object> command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await SendCommandCoreAsync(connection, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<TimesGateCommandResult> SendCommandCoreAsync(
        TimesGateConnection connection,
        Dictionary<string, object> command,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestBody = JsonSerializer.SerializeToUtf8Bytes(command);
            using var content = new ByteArrayContent(requestBody);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _httpClient.PostAsync(
                connection.Endpoint,
                content,
                cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = $"Times Gate returned HTTP {(int)response.StatusCode}.";
                LogHttpError(_logger, connection.Host, (int)response.StatusCode);
                return TimesGateCommandResult.Failure(message);
            }

            return ParseResponse(body, connection.Host);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "Times Gate request timed out.";
            LogTimeout(_logger, connection.Host);
            return TimesGateCommandResult.Failure(message);
        }
        catch (HttpRequestException exception)
        {
            var message = $"Times Gate is unreachable: {exception.Message}";
            LogNetworkError(_logger, connection.Host, exception);
            return TimesGateCommandResult.Failure(message);
        }
        catch (JsonException exception)
        {
            var message = $"Times Gate returned invalid JSON: {exception.Message}";
            LogInvalidResponse(_logger, connection.Host, exception);
            return TimesGateCommandResult.Failure(message);
        }
    }

    private TimesGateCommandResult ParseResponse(string body, string host)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (!root.TryGetProperty("error_code", out var errorElement))
        {
            return TimesGateCommandResult.Failure("Times Gate response has no error_code field.");
        }

        if (errorElement.ValueKind == JsonValueKind.Number && errorElement.TryGetInt32(out var numericCode))
        {
            if (numericCode == 0)
            {
                return TimesGateCommandResult.Success();
            }

            LogApiError(_logger, host, numericCode);
            return TimesGateCommandResult.Failure($"Times Gate error {numericCode}.", numericCode);
        }

        var textCode = errorElement.ToString();
        if (string.Equals(textCode, "0", StringComparison.Ordinal))
        {
            return TimesGateCommandResult.Success();
        }

        LogApiTextError(_logger, host, textCode);
        return TimesGateCommandResult.Failure(textCode);
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Times Gate frame sequence initialized for {Host}.")]
    private static partial void LogFrameSequenceInitialized(ILogger logger, string host);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Times Gate returned HTTP {StatusCode} from {Host}.")]
    private static partial void LogHttpError(ILogger logger, string host, int statusCode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Times Gate request timed out for {Host}.")]
    private static partial void LogTimeout(ILogger logger, string host);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Times Gate network error for {Host}.")]
    private static partial void LogNetworkError(ILogger logger, string host, Exception exception);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Invalid Times Gate response from {Host}.")]
    private static partial void LogInvalidResponse(ILogger logger, string host, Exception exception);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Times Gate command failed with error {ErrorCode} on {Host}.")]
    private static partial void LogApiError(ILogger logger, string host, int errorCode);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Times Gate command failed: {ErrorMessage} on {Host}.")]
    private static partial void LogApiTextError(ILogger logger, string host, string errorMessage);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _commandLock.Dispose();
        _disposed = true;
    }
}
