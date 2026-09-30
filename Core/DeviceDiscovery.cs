using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DLC_PRO.Core;

public sealed record DiscoveredDevice(string Address, string Description)
{
    public string Display => Address + "  ·  " + Description;
}

public static class DeviceDiscovery
{
    public const int Port = 60010;
    // DLC pro Command Reference 4.4.2: send laserfinder and listen on UDP 60010.
    public static async Task<IReadOnlyList<DiscoveredDevice>> FindAsync(CancellationToken token = default)
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null) continue;
                byte[] ip = address.Address.GetAddressBytes(), mask = address.IPv4Mask.GetAddressBytes();
                targets.Add(new IPAddress(ip.Select((b, i) => (byte)(b | ~mask[i])).ToArray()));
            }
        }
        return await FindAtAsync(targets.Select(ip => new IPEndPoint(ip, Port)), Port, token);
    }

    internal static async Task<IReadOnlyList<DiscoveredDevice>> FindAtAsync(IEnumerable<IPEndPoint> targets, int localPort, CancellationToken token = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ExclusiveAddressUse = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
        udp.EnableBroadcast = true;
        byte[] query = Encoding.ASCII.GetBytes("laserfinder");
        int sent = 0;
        foreach (var target in targets)
        {
            try { await udp.SendAsync(query, target, token); sent++; }
            catch (SocketException) { }
        }
        if (sent == 0) throw new System.IO.IOException("네트워크 어댑터에서 검색 요청을 보낼 수 없습니다.");
        var found = new Dictionary<string, DiscoveredDevice>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(deadline.Token);
                string text = Encoding.UTF8.GetString(response.Buffer).Trim('\0', '\r', '\n', ' ');
                if (text.Length == 0 || text == "laserfinder") continue;
                string ip = response.RemoteEndPoint.Address.ToString();
                found[ip] = new DiscoveredDevice(ip, text.Replace('\n', ' ').Replace('\r', ' '));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
        }
        return found.Values.OrderBy(x => x.Address, StringComparer.Ordinal).ToArray();
    }
}
