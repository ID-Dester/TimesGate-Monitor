using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.TimesGate.Services;

namespace TimesGateMonitor.Tests.TimesGate;

internal static class TimesGateServiceTests
{
    private static readonly int[] ScreenThreeSelector = [0, 0, 1, 0, 0];

    public static async Task RunAsync()
    {
        await UsesJpegProtocolAndSkipsUnchangedFrameAsync();
        await FailedSendIsNotRememberedAsync();
        await ConfiguresNetworkTextScreenAsync();
        await ConfiguresNativeHourMinuteScreenAsync();
    }

    private static async Task UsesJpegProtocolAndSkipsUnchangedFrameAsync()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new TimesGateService(httpClient, NullLogger<TimesGateService>.Instance);
        var connection = new TimesGateConnection("192.168.1.42", 654321);
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var frame = new DisplayFrame(3, jpeg);

        var initialization = await service.InitializeFrameSequenceAsync(connection, CancellationToken.None);
        var firstSend = await service.SendFrameAsync(
            connection,
            frame,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        var duplicateSend = await service.SendFrameAsync(
            connection,
            frame,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        TestAssert.True(initialization.IsSuccess, "Initialization should succeed.");
        TestAssert.True(firstSend.IsSuccess, "First send should succeed.");
        TestAssert.True(duplicateSend.WasSkipped, "Duplicate frame should be skipped.");
        TestAssert.Equal(2, handler.Bodies.Count, "Duplicate frame caused an HTTP request.");
        TestAssert.Equal(
            "application/json",
            handler.ContentTypes[0] ?? "<null>",
            "Content-Type must be plain application/json.");
        TestAssert.True(
            handler.ContentLengths[0].GetValueOrDefault() > 0,
            "Request must have an explicit Content-Length.");

        using var resetDocument = JsonDocument.Parse(handler.Bodies[0]);
        TestAssert.Equal(
            "Draw/ResetHttpGifId",
            resetDocument.RootElement.GetProperty("Command").GetString()!,
            "Reset command is incorrect.");
        TestAssert.Equal(
            654321,
            resetDocument.RootElement.GetProperty("LocalToken").GetInt32(),
            "LocalToken is missing from reset.");

        using var frameDocument = JsonDocument.Parse(handler.Bodies[1]);
        var root = frameDocument.RootElement;
        TestAssert.Equal("Draw/SendHttpGif", root.GetProperty("Command").GetString()!, "Frame command is incorrect.");
        TestAssert.Equal(128, root.GetProperty("PicWidth").GetInt32(), "PicWidth is incorrect.");
        TestAssert.Equal(1, root.GetProperty("PicNum").GetInt32(), "PicNum is incorrect.");
        TestAssert.Equal(1, root.GetProperty("PicID").GetInt32(), "PicID is incorrect.");
        TestAssert.Equal(1000, root.GetProperty("PicSpeed").GetInt32(), "PicSpeed is incorrect.");
        TestAssert.Equal(
            Convert.ToBase64String(jpeg),
            root.GetProperty("PicData").GetString()!,
            "PicData must be Base64 JPEG.");
        TestAssert.SequenceEqual(
            ScreenThreeSelector,
            root.GetProperty("LcdArray").EnumerateArray().Select(item => item.GetInt32()),
            "LcdArray did not target screen 3.");
    }

    private static async Task FailedSendIsNotRememberedAsync()
    {
        var handler = new RecordingHandler(
            "{\"error_code\":0}",
            "{\"error_code\":3}",
            "{\"error_code\":0}");
        using var httpClient = new HttpClient(handler);
        using var service = new TimesGateService(httpClient, NullLogger<TimesGateService>.Instance);
        var connection = new TimesGateConnection("192.168.1.42", 654321);
        var frame = new DisplayFrame(1, [0xFF, 0xD8, 0xFF, 0xD9]);

        await service.InitializeFrameSequenceAsync(connection, CancellationToken.None);
        var failed = await service.SendFrameAsync(
            connection,
            frame,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        var retried = await service.SendFrameAsync(
            connection,
            frame,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        TestAssert.False(failed.IsSuccess, "The simulated API error should fail.");
        TestAssert.True(retried.IsSuccess, "A failed frame must be retried.");
        TestAssert.False(retried.WasSkipped, "A failed frame must not enter the hash cache.");
        TestAssert.Equal(3, handler.Bodies.Count, "Retry request was not sent.");
    }

    private static async Task ConfiguresNetworkTextScreenAsync()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new TimesGateService(httpClient, NullLogger<TimesGateService>.Instance);
        var connection = new TimesGateConnection("192.168.1.42", 654321);
        NetworkTextItem[] items =
        [
            new(101, 7, 52, 4, 116, 16, new Uri("http://192.168.1.10:8765/value/cpu-bar"))
        ];

        var result = await service.ConfigureNetworkTextScreenAsync(
            connection,
            screenIndex: 1,
            new Uri("http://192.168.1.10:8765/background/cpu.gif"),
            items,
            CancellationToken.None);

        TestAssert.True(result.IsSuccess, "Network-text configuration should succeed.");
        TestAssert.Equal(1, handler.Bodies.Count, "Configuration must use one HTTP request.");

        using var document = JsonDocument.Parse(handler.Bodies[0]);
        var root = document.RootElement;
        TestAssert.Equal(
            "Draw/SendHttpItemList",
            root.GetProperty("Command").GetString()!,
            "Network-text command is incorrect.");
        TestAssert.Equal(0, root.GetProperty("LcdIndex").GetInt32(), "LCD index must be zero-based.");
        TestAssert.Equal(1, root.GetProperty("NewFlag").GetInt32(), "Initial background must be loaded.");
        TestAssert.Equal(
            "http://192.168.1.10:8765/background/cpu.gif",
            root.GetProperty("BackgroudGif").GetString()!,
            "Protocol-specific BackgroudGif field is incorrect.");
        TestAssert.Equal(654321, root.GetProperty("LocalToken").GetInt32(), "LocalToken is missing.");

        var item = root.GetProperty("ItemList")[0];
        TestAssert.Equal(23, item.GetProperty("type").GetInt32(), "Item must use network-text type 23.");
        TestAssert.Equal(16, item.GetProperty("Textheight").GetInt32(), "Textheight field is incorrect.");
        TestAssert.Equal(1, item.GetProperty("align").GetInt32(), "Default text alignment must be left.");
        TestAssert.Equal(
            "http://192.168.1.10:8765/value/cpu-bar",
            item.GetProperty("TextString").GetString()!,
            "Network-text value URL is incorrect.");
    }

    private static async Task ConfiguresNativeHourMinuteScreenAsync()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new TimesGateService(httpClient, NullLogger<TimesGateService>.Instance);
        var connection = new TimesGateConnection("192.168.1.42", 654321);
        NetworkTextItem[] items =
        [
            new(201, 0, 42, 44, 128, 40, new Uri("http://192.168.1.10:8765/value/blank"),
                ItemType: 5, TextString: string.Empty, Alignment: 2, UpdateSeconds: 60)
        ];

        var result = await service.ConfigureNetworkTextScreenAsync(
            connection,
            screenIndex: 5,
            new Uri("http://192.168.1.10:8765/background/clock.gif"),
            items,
            CancellationToken.None);

        TestAssert.True(result.IsSuccess, "Native clock configuration should succeed.");
        using var document = JsonDocument.Parse(handler.Bodies[0]);
        var item = document.RootElement.GetProperty("ItemList")[0];
        TestAssert.Equal(5, item.GetProperty("type").GetInt32(), "Native clock must use HourMinute type 5.");
        TestAssert.Equal(string.Empty, item.GetProperty("TextString").GetString()!, "Native clock must not contain a URL.");
        TestAssert.Equal(128, item.GetProperty("TextWidth").GetInt32(), "Native clock must use the full LCD width.");

        items[0] = items[0] with { ItemType = 6, UpdateSeconds = 1 };
        result = await service.ConfigureNetworkTextScreenAsync(
            connection,
            screenIndex: 5,
            new Uri("http://192.168.1.10:8765/background/clock.gif"),
            items,
            CancellationToken.None);
        TestAssert.True(result.IsSuccess, "Native clock with seconds should succeed.");
        using var secondsDocument = JsonDocument.Parse(handler.Bodies[1]);
        TestAssert.Equal(
            6,
            secondsDocument.RootElement.GetProperty("ItemList")[0].GetProperty("type").GetInt32(),
            "Seconds mode must use HourMinuteSecond type 6.");
    }

    private sealed class RecordingHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public List<string> Bodies { get; } = [];

        public List<string?> ContentTypes { get; } = [];

        public List<long?> ContentLengths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            ContentTypes.Add(request.Content.Headers.ContentType?.ToString());
            ContentLengths.Add(request.Content.Headers.ContentLength);
            var body = _responses.Count > 0 ? _responses.Dequeue() : "{\"error_code\":0}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }
    }
}
