using System.Net.Quic;
using System.Reflection;
using System.Runtime.InteropServices;
using Elements.Core;

namespace ResoQuiccMk2.Quic;

public unsafe class MsQuicHack
{
    private static readonly FieldInfo StreamHandleField = typeof(QuicStream).GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo ConnectionHandleField = typeof(QuicConnection).GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly delegate*unmanaged[Cdecl]<IntPtr, uint, uint, void*, int> SetParamFn;
    private static readonly delegate*unmanaged[Cdecl]<IntPtr, uint, uint*, void*, int> GetParamFn;

    static MsQuicHack()
    {
        try
        {
            var apiType = typeof(QuicConnection).Assembly.GetType("System.Net.Quic.MsQuicApi")!;
            UniLog.Log($"API type: {apiType}");
            var apiProp = apiType.GetProperty("Api", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
            UniLog.Log($"API property: {apiProp}");
            var apiValue = apiProp.GetValue(null, null)!;
            UniLog.Log($"API value: {apiValue}");
            var apiTablePointerField = apiType.GetProperty("ApiTable", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
            UniLog.Log($"API table field: {apiTablePointerField}");
            var tablePtr = (void**)Pointer.Unbox(apiTablePointerField.GetValue(apiValue)!); // QUIC_API_TABLE
            UniLog.Log($"API table ptr: {(IntPtr)tablePtr}");

            SetParamFn = (delegate*unmanaged[Cdecl]<IntPtr, uint, uint, void*, int>)tablePtr[3];
            GetParamFn = (delegate*unmanaged[Cdecl]<IntPtr, uint, uint*, void*, int>)tablePtr[4];
        }
        catch (Exception e)
        {
            UniLog.Error($"Unable to get msquic api table and members: {e.Message} {e.StackTrace}", false);
        }
    }
    
    public static void SetStreamPriority(QuicStream stream, ushort priority)
    {
        if (SetParamFn == null) 
            return;
        
        var handle = (SafeHandle?) StreamHandleField.GetValue(stream);
        if (handle == null || handle.IsClosed || handle.IsInvalid)
            throw new InvalidOperationException("Stream is closed");
        
        SetParamFn(handle.DangerousGetHandle(), QUIC_PARAM_STREAM_PRIORITY, sizeof(ushort), &priority);
    }

    public static (int Mtu, ulong RttUs, ulong PacketsLost) GetConnectionStats(QuicConnection connection)
    {
        if (GetParamFn == null)
            return default;
        
        QUIC_STATISTICS_V2 stats = default;
        var handle = (SafeHandle?) ConnectionHandleField.GetValue(connection);
        if (handle == null || handle.IsClosed || handle.IsInvalid)
            throw new InvalidOperationException("Connection is closed");
        uint size = (uint) sizeof(QUIC_STATISTICS_V2);
        GetParamFn(handle.DangerousGetHandle(), QUIC_PARAM_CONN_STATISTICS_V2, &size, &stats);
        return (stats.SendPathMtu, stats.Rtt, stats.SendSuspectedLostPackets - stats.SendSpuriousLostPackets);
    }
    
    // ReSharper disable InconsistentNaming
    private const uint QUIC_PARAM_STREAM_PRIORITY = 0x08000003;
    internal const uint QUIC_PARAM_CONN_STATISTICS_V2 = 0x05000016;
}