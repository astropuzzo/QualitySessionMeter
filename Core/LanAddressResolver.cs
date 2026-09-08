using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NINA.Plugin.QualitySessionMeter.Core;

internal static class LanAddressResolver {
    private static readonly string[] VirtualAdapterMarkers = {
        "vpn", "tailscale", "wireguard", "wintun", "tap", "tun ", "zerotier", "hamachi",
        "openvpn", "mullvad", "nordvpn", "protonvpn", "cloudflare warp", "warp adapter",
        "hyper-v", "vmware", "virtualbox", "docker", "loopback"
    };

    public static string BuildDashboardUrl(int port) {
        var address = FindPreferredLanIpv4();
        return address == null
            ? $"http://<NINA-PC-LAN-IP>:{port}/"
            : $"http://{address}:{port}/";
    }

    public static IPAddress FindPreferredLanIpv4() {
        try {
            var candidates = new List<Candidate>();

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                if (adapter == null || adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
                if (LooksVirtualOrVpn(adapter)) continue;

                IPInterfaceProperties properties;
                try { properties = adapter.GetIPProperties(); }
                catch { continue; }

                var hasIpv4Gateway = properties.GatewayAddresses.Any(g =>
                    g?.Address?.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(g.Address) &&
                    !IPAddress.None.Equals(g.Address) &&
                    !IPAddress.IsLoopback(g.Address));

                foreach (var unicast in properties.UnicastAddresses) {
                    var address = unicast?.Address;
                    if (address == null || address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(address) || !IsPrivateLan(address)) continue;

                    var score = 0;
                    if (hasIpv4Gateway) score += 100;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 35;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 30;
                    if (adapter.Speed >= 100_000_000) score += 5;

                    try {
                        if (adapter.GetPhysicalAddress()?.GetAddressBytes()?.Length == 6) score += 10;
                    } catch { }

                    // Home/small-office LANs most commonly use 192.168/16 or 10/8.
                    // This is only a tie-breaker; 172.16/12 remains fully supported.
                    var bytes = address.GetAddressBytes();
                    if (bytes[0] == 192 && bytes[1] == 168) score += 3;
                    else if (bytes[0] == 10) score += 2;

                    candidates.Add(new Candidate(address, score, adapter.Id));
                }
            }

            return candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.AdapterId, StringComparer.Ordinal)
                .Select(c => c.Address)
                .FirstOrDefault();
        } catch {
            return null;
        }
    }

    private static bool LooksVirtualOrVpn(NetworkInterface adapter) {
        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return VirtualAdapterMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsPrivateLan(IPAddress address) {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return false;
        if (bytes[0] == 10) return true;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        return bytes[0] == 192 && bytes[1] == 168;
    }

    private sealed record Candidate(IPAddress Address, int Score, string AdapterId);
}
