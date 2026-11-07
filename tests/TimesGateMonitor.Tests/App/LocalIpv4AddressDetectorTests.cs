using System.Net;
using TimesGateMonitor.App.Infrastructure;

namespace TimesGateMonitor.Tests.App;

internal static class LocalIpv4AddressDetectorTests
{
    public static void Run()
    {
        TestAssert.True(
            LocalIpv4AddressDetector.IsSameSubnet(
                IPAddress.Parse("192.168.50.10"),
                IPAddress.Parse("192.168.50.20"),
                IPAddress.Parse("255.255.255.0")),
            "The PC address on the Times Gate subnet must be recognized.");

        TestAssert.True(
            !LocalIpv4AddressDetector.IsSameSubnet(
                IPAddress.Parse("192.168.60.10"),
                IPAddress.Parse("192.168.50.20"),
                IPAddress.Parse("255.255.255.0")),
            "An address on another subnet must not receive the device-subnet preference.");

        TestAssert.True(
            !LocalIpv4AddressDetector.IsSameSubnet(
                IPAddress.IPv6Loopback,
                IPAddress.Parse("192.168.50.20"),
                IPAddress.Parse("255.255.255.0")),
            "IPv6 addresses must not be treated as IPv4 LAN candidates.");
    }
}
