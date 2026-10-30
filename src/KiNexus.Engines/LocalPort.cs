using System.Net;
using System.Net.Sockets;

namespace KiNexus.Engines;

/// <summary>
/// Hilfen für lokale Ports (nur 127.0.0.1).
/// Vor dem Start von llama-server oder Open WebUI prüft die App, ob der Port frei ist.
/// </summary>
public static class LocalPort
{
    /// <summary>True, wenn auf 127.0.0.1 an diesem Port noch niemand hört.</summary>
    public static bool IsFree(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);

        var listener = new TcpListener(IPAddress.Loopback, port)
        {
            // Ohne ExclusiveAddressUse könnte Windows den Port doppelt vergeben.
            ExclusiveAddressUse = true,
        };

        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }
}
