using System.Buffers;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ResoQuiccMk2.Utils;

public static class QuicUtilities
{
    public static SliceMemoryOwner<byte> BuildFrame(ReadOnlySpan<byte> data, uint? frameId = null)
    {
        if (data.Length > ModConstants.MaxFrameLength)
            throw new InvalidDataException($"Trying to build a frame of length {data.Length} exceeding the limit of {ModConstants.MaxFrameLength}");
        
        var owner = MemoryPool<byte>.Shared.Rent(data.Length + 16 + (frameId.HasValue ? 16 : 0));
        var buffer = owner.Memory;
        var headerLength = ReadWriteUtils.FormatUleb128(data.Length, buffer.Span);
        if (frameId.HasValue)
            headerLength += ReadWriteUtils.FormatUleb128(frameId.Value, buffer.Span[headerLength..]);
        data.CopyTo(buffer.Span[headerLength..]);
        
        return owner.AsSlice(headerLength + data.Length);
    }
    
    public static async ValueTask<(IMemoryOwner<byte>, Memory<byte>)> ReadFrame(QuicStream stream, CancellationToken token)
    {
        var length = (int) await stream.ReadUleb128Async(token).ConfigureAwait(false);
        if (length > ModConstants.MaxFrameLength)
            throw new InvalidDataException($"Remote sent a frame of length {length} exceeding the limit of {ModConstants.MaxFrameLength}");
        
        var buffer = MemoryPool<byte>.Shared.Rent(length);
        var dataSpace = buffer.Memory[..length];
        await stream.ReadExactlyAsync(dataSpace, token).ConfigureAwait(false);
        return (buffer, dataSpace);
    }

    public static ValueTask WriteFrame(ReadOnlySpan<byte> data, QuicStream stream, CancellationToken token)
    {
        if (data.Length > ModConstants.MaxFrameLength)
            throw new InvalidDataException($"Trying to send a frame of length {data.Length} exceeding the limit of {ModConstants.MaxFrameLength}");
        
        var slice = BuildFrame(data);

        return WriteFrameImpl(slice.Memory, stream, slice, token);
    }
    
    public static ValueTask WriteFrame(ReadOnlyMemory<byte> data, QuicStream stream, CancellationToken token)
    {
        return WriteFrameImpl<IDisposable>(data, stream, null, token);
    }

    public static (IMemoryOwner<byte>, Memory<byte>) CopyAsMemory(ReadOnlySpan<byte> data)
    {
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        var buffer = owner.Memory;
        data.CopyTo(buffer.Span);
        return (owner, buffer[..data.Length]);
    }
    
    private static async ValueTask WriteFrameImpl<T>(ReadOnlyMemory<byte> data, QuicStream stream, T? owner, CancellationToken token) where T: IDisposable
    {
        using var _ = owner;
        await stream.WriteUleb128Async(data.Length, token);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }

    public static QuicListenerOptions GetHostServerOptions(IPEndPoint endPoint, X509Certificate serverCertificate, string appId)
    {
        List<SslApplicationProtocol> sslApplicationProtocols = [new($"mod-resoquicc/v1/{appId}")];
        return new QuicListenerOptions
        {
            ApplicationProtocols = sslApplicationProtocols,
            ListenBacklog = 10,
            ListenEndPoint = endPoint,
            ConnectionOptionsCallback = async (_, _, _) => new QuicServerConnectionOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(15),
                MaxInboundBidirectionalStreams = 2,
                MaxInboundUnidirectionalStreams = ushort.MaxValue,
                DefaultCloseErrorCode = 1,
                DefaultStreamErrorCode = 1,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ApplicationProtocols = sslApplicationProtocols,
                }
            }
        };
    }

    public static async Task<string> FetchExternalAddress()
    {
        using var client = new HttpClient();
        return await client.GetStringAsync("https://checkip.amazonaws.com/");
    }
}