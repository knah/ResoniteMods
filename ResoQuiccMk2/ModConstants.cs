using System.Net.Security;
using Elements.Core;

namespace ResoQuiccMk2;

public class ModConstants
{
    public const string QuicDirectScheme = "mod-resoquicc+quic";
    public const int MaxFrameLength = 16 * 1024 * 1024;

    public static SslApplicationProtocol MakeProtocol(string appId) => new($"mod-resoquicc/v1/{appId}");

    public const ushort MainStreamPriority = 0xffff;
    public const ushort DgramStreamPriority = 0x7000;
    public const ushort BackgroundStreamPriority = 1;
    
    public static readonly ArrayPool<byte> MessagesPool = new(1024, 2048, 4096, 16384, 65536);
}