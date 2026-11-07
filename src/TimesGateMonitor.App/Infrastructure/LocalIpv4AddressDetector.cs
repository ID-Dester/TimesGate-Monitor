using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TimesGateMonitor.App.Infrastructure;

internal static class LocalIpv4AddressDetector
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "amnezia",
        "openvpn",
        "tailscale",
        "tap-",
        "tunnel",
        "vpn",
        "wireguard",
        "wintun"
    ];

    public static string? TryGetPreferredAddress(string? deviceAddress = null)
    {
        try
        {
            var deviceIp = IPAddress.TryParse(deviceAddress, out var parsedDeviceIp) &&
                           parsedDeviceIp.AddressFamily == AddressFamily.InterNetwork
                ? parsedDeviceIp
                : null;

            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsCandidateInterface)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new Candidate(
                        address.Address,
                        Score(network, address, deviceIp))))
                .Where(candidate => IsPrivate(candidate.Address))
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Address.ToString())
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public static bool IsAddressAssigned(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsCandidateInterface)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsCandidateInterface(NetworkInterface network) =>
        network.OperationalStatus == OperationalStatus.Up &&
        network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
            not NetworkInterfaceType.Tunnel;

    private static int Score(
        NetworkInterface network,
        UnicastIPAddressInformation address,
        IPAddress? deviceAddress)
    {
        var text = $"{network.Name} {network.Description}";
        var virtualPenalty = VirtualAdapterMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? -100
            : 0;
        var gatewayBonus = network.GetIPProperties().GatewayAddresses.Any(gateway =>
            gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
            !gateway.Address.Equals(IPAddress.Any))
            ? 20
            : 0;
        var interfaceBonus = network.NetworkInterfaceType is
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
            ? 10
            : 0;
        var privateRangeBonus = address.Address.GetAddressBytes()[0] == 192 ? 2 : 0;
        var deviceSubnetBonus = deviceAddress is not null &&
                                IsSameSubnet(address.Address, deviceAddress, address.IPv4Mask)
            ? 1_000
            : 0;
        return 100 + virtualPenalty + gatewayBonus + interfaceBonus + privateRangeBonus + deviceSubnetBonus;
    }

    internal static bool IsSameSubnet(IPAddress address, IPAddress peer, IPAddress? mask)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(peer);

        if (address.AddressFamily != AddressFamily.InterNetwork ||
            peer.AddressFamily != AddressFamily.InterNetwork ||
            mask is null ||
            mask.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var peerBytes = peer.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        for (var index = 0; index < addressBytes.Length; index++)
        {
            if ((addressBytes[index] & maskBytes[index]) != (peerBytes[index] & maskBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    private sealed record Candidate(IPAddress Address, int Score);
}
