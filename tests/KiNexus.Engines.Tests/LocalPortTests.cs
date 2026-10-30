using System.Net;
using System.Net.Sockets;

namespace KiNexus.Engines.Tests;

public class LocalPortTests
{
    [Fact]
    public void IsFree_ReturnsFalse_WhenSomeoneListens()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Assert.False(LocalPort.IsFree(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void IsFree_ReturnsTrue_AfterListenerStopped()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.True(LocalPort.IsFree(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void IsFree_Throws_ForInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalPort.IsFree(port));
    }
}
