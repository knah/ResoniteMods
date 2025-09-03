using System.Net;
using FrooxEngine;
using ResoQuiccMk2.Utils;

namespace ResoQuiccMk2;

public class QuicDirectNetworkManager : INetworkManager
{
    private string _appId = "";
    public void SetAppId(string appId) => _appId = appId;

    public bool SupportsScheme(string scheme)
    {
        return ResoQuiccMod.EnableDirectQuicConnect.GetValueOrDefault() && scheme == ModConstants.QuicDirectScheme;
    }

    public void GetSupportedSchemes(List<string> schemes)
    {
        if (!ResoQuiccMod.EnableDirectQuicConnect.GetValueOrDefault())
            return;
        
        schemes.Add(ModConstants.QuicDirectScheme);
    }

    public List<ConnectionConstructor> GetPrioritizedConnectionConstructors(IEnumerable<Uri> uris, World world, out string? expectedSessionId)
    {
        expectedSessionId = null;
        return uris.Where(it => it.Scheme == ModConstants.QuicDirectScheme)
            .Select(it => new ConnectionConstructor(this, it, () => new QuicClientConnection(it, _appId))).ToList();
    }

    public IEnumerable<IListener> CreateListeners(ushort port, World world)
    {
        if (!ResoQuiccMod.EnableDirectQuicHost.GetValueOrDefault())
            return [];
        
        return [new QuicHostListener(new IPEndPoint(IPAddress.Parse(ResoQuiccMod.BindIP.GetValueOrDefault() ?? "0.0.0.0"), port), _appId)];
    }

    public void TransmitData(RawOutMessage data)
    {
        foreach (var dataTarget in data.Targets)
            if (dataTarget is QuicClientConnection conn) 
                conn.Send(data);
        
        data.TransmissionFinished();
    }

    public int Priority => 10;
    public bool UsesPort => true;
}