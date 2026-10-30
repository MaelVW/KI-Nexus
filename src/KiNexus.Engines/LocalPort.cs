using System.Net;
using System.Net.NetworkInformation;

namespace KiNexus.Engines;

/// <summary>
/// Hilfen für lokale Ports.
/// Vor dem Start von llama-server oder Open WebUI prüft die App, ob der Port frei ist.
/// </summary>
public static class LocalPort
{
    /// <summary>
    /// True, wenn kein Programm auf diesem Port auf Verbindungen wartet (TCP "Listen").
    /// </summary>
    /// <remarks>
    /// Wir fragen Windows nach den wartenden Programmen, statt den Port selbst zu belegen.
    /// Grund: Nach dem Beenden eines Servers bleiben alte Verbindungen kurz im Zustand TIME_WAIT.
    /// Ein exklusives Belegen würde dann scheitern, obwohl ein neuer Server problemlos starten kann.
    /// </remarks>
    public static bool IsFree(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);

        return !IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port);
    }
}
