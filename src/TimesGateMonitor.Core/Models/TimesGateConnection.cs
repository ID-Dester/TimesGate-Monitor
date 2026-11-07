namespace TimesGateMonitor.Core.Models;

public sealed record TimesGateConnection(string Host, int LocalToken)
{
    public Uri Endpoint
    {
        get
        {
            var host = Host.Trim();

            if (Uri.TryCreate(host, UriKind.Absolute, out var absolute))
            {
                return new Uri(absolute, "/post");
            }

            return new Uri($"http://{host}/post", UriKind.Absolute);
        }
    }
}
