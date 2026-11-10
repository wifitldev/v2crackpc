using System.Net;
using System.Net.Sockets;
using UiProto;

namespace ServiceLib.Tests;

public class LocalPortProbeTests
{
    [Test]
    public async Task DetectsLocalListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await (await LocalPortProbe.IsListeningAsync(port)).Should().BeTrue();
            await (await LocalPortProbe.WaitAsync(port, 1000)).Should().BeTrue();
        }
        finally { listener.Stop(); }

    }

    [Test]
    public async Task BoundButNonListeningSocketIsNotReachable()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.ExclusiveAddressUse = true;
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        await (await LocalPortProbe.WaitAsync(port, 300)).Should().BeFalse();
    }

    [Test]
    public async Task RejectsInvalidPort()
    {
        await (await LocalPortProbe.IsListeningAsync(65536)).Should().BeFalse();
    }
}
