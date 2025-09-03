using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using System.Threading.Channels;
using Elements.Core;
using ResoQuiccMk2.Utils;

namespace ResoQuiccMk2.Quic;

public class ClientConnection : IDisposable
{
    private readonly EventHandler _eventHandler;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _cancellationToken;
    public readonly QuicConnection Connection;
    private readonly QuicStream _mainStream;
    private readonly QuicStream _backgroundStream;

    public ClientConnection(CancellationTokenSource cancellationTokenSource, QuicConnection connection,
        QuicStream mainStream, QuicStream backgroundStream)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _cancellationToken = cancellationTokenSource.Token;
        Connection = connection;
        _mainStream = mainStream;
        _backgroundStream = backgroundStream;
    }

    private readonly Channel<SliceMemoryOwner<byte>> _mainSendQueue = Channel.CreateUnbounded<SliceMemoryOwner<byte>>(new UnboundedChannelOptions()
    {
        SingleReader = true,
    });

    private readonly Channel<SliceMemoryOwner<byte>> _backgroundSendQueue = Channel.CreateUnbounded<SliceMemoryOwner<byte>>(new UnboundedChannelOptions()
    {
        SingleReader = true,
    });

    private readonly Channel<(QuicStream, DateTime)> dgramStreamDisposeQueue = Channel.CreateUnbounded<(QuicStream, DateTime)>(new UnboundedChannelOptions()
    {
        SingleReader = true,
    });

    // These use explicit fields as we can't get Count ont single-reader channels
    private int _mainQueueMessages;
    private int _bgQueueMessages;
    private int _dgramQueueStreams;
    private int _dgramOpenStreams;
    
    public int MainQueueSize => _mainQueueMessages;
    public int BgQueueSize => _bgQueueMessages;
    public int PendingDgramStreams => _dgramQueueStreams;
    public int PendingDgramOpenStreams => _dgramOpenStreams;

    public delegate void EventHandler(EventType eventType, ReadOnlySpan<byte> data);

    public bool IsConnected { get; private set; }
    public string? FailureReason { get; private set; }

    public static async Task<ClientConnection?> Connect(string uri, EventHandler handleEvent, string appId)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        
        var remoteUri = new Uri(uri);
        var expectedServerCertificateFingerprint = remoteUri.Fragment;
        if (expectedServerCertificateFingerprint.StartsWith("#chs="))
            expectedServerCertificateFingerprint = expectedServerCertificateFingerprint[5..];

        try
        {
            var clientOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = IPEndPoint.Parse(remoteUri.Authority),
                MaxInboundBidirectionalStreams = 0,
                MaxInboundUnidirectionalStreams = ushort.MaxValue,
                IdleTimeout = TimeSpan.FromSeconds(15),
                DefaultStreamErrorCode = 1,
                DefaultCloseErrorCode = 1,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.CertificateHashString() == expectedServerCertificateFingerprint,
                    CertificateChainPolicy = CertificateUtil.GetEmptyChainPolicy(),
                    ApplicationProtocols = [ModConstants.MakeProtocol(appId)],
                },
            };

            var connection = await QuicConnection.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);

            // Use a send/receive pair to make sure we create these two streams in order
            var mainStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false);
            MsQuicHack.SetStreamPriority(mainStream, ModConstants.MainStreamPriority);
            await mainStream.WriteUleb128Async(0, cancellationToken).ConfigureAwait(false);
            await mainStream.ReadUleb128Async(cancellationToken).ConfigureAwait(false);
            var backgroundStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false);
            MsQuicHack.SetStreamPriority(backgroundStream, ModConstants.BackgroundStreamPriority);
            await backgroundStream.WriteUleb128Async(0, cancellationToken).ConfigureAwait(false);
            await backgroundStream.ReadUleb128Async(cancellationToken).ConfigureAwait(false);

            return new ClientConnection(cancellationTokenSource, handleEvent, connection, mainStream, backgroundStream);
        }
        catch (Exception ex)
        {
            UniLog.Error($"Connection to {uri} failed: {ex.Message} {ex.StackTrace}", false);
            await cancellationTokenSource.CancelAsync();
            cancellationTokenSource.Dispose();
            return null;
        }
    }

    internal ClientConnection(CancellationTokenSource cancellationTokenSource, EventHandler eventHandler,
        QuicConnection connection, QuicStream mainStream, QuicStream backgroundStream) : this(cancellationTokenSource, connection, mainStream, backgroundStream)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _eventHandler = eventHandler;
    }

    private void HandleIncomingPacket(ReadOnlySpan<byte> buffer, bool reliable, bool background)
    {
        _eventHandler(EventType.Data, buffer);
    }

    private void HandleClientClosed()
    {
        _eventHandler(EventType.Disconnected, Encoding.UTF8.GetBytes(FailureReason ?? ""));
    }

    internal void StartProcessors()
    {
        StreamReader(_mainStream, false).LogFailure(this);
        StreamReader(_backgroundStream, true).LogFailure(this);
        StreamWriter(_mainStream, false).LogFailure(this);
        StreamWriter(_backgroundStream, true).LogFailure(this);

        StreamAcceptor().LogFailure(this);
        DgramStreamDisposer().LogFailure(this);
    }

    private async Task DgramStreamDisposer()
    {
        await foreach (var (stream, deadline) in dgramStreamDisposeQueue.Reader.ReadAllAsync(_cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _dgramQueueStreams);
            var now = DateTime.UtcNow;
            if (deadline > now) 
                await Task.Delay(deadline - now, _cancellationToken).ConfigureAwait(false);

            stream.Abort(QuicAbortDirection.Both, 2);
            await stream.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _dgramOpenStreams);
        }
    }

    private async Task StreamAcceptor()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            var incomingStream = await Connection.AcceptInboundStreamAsync(_cancellationToken).ConfigureAwait(false);
            DatagramReader(incomingStream).LogFailure(this);
        }
    }

    private async Task StreamWriter(QuicStream stream, bool background)
    {
        var queue = background ? _backgroundSendQueue : _mainSendQueue;

        await foreach (var slice in queue.Reader.ReadAllAsync(_cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref background ? ref _bgQueueMessages : ref _mainQueueMessages);
            using var _ = slice;
            await stream.WriteAsync(slice.Memory, _cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamReader(QuicStream stream, bool background)
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            var (owner, data) = await QuicUtilities.ReadFrame(stream, _cancellationToken).ConfigureAwait(false);
            using var _ = owner;
            HandleIncomingPacket(data.Span, true, background);
        }
    }

    private readonly SemaphoreSlim _dgramReceiveSemaphore = new(1, 1);
    private uint _lastSeenFrameId;

    private async ValueTask DatagramReader(QuicStream stream)
    {
        await using var _ = stream;
        try
        {
            var (owner, data) = await QuicUtilities.ReadFrame(stream, _cancellationToken).ConfigureAwait(false);
            using var __ = owner;
            var off = 0;
            var receivedFrameId = (uint) ReadWriteUtils.ReadUleb128(data.Span, ref off);
            await _dgramReceiveSemaphore.WaitAsync(_cancellationToken).ConfigureAwait(false);
            try
            {
                if (receivedFrameId > _lastSeenFrameId || _lastSeenFrameId >= uint.MaxValue - 255)
                {
                    _lastSeenFrameId = receivedFrameId;
                    HandleIncomingPacket(data.Span[off..], false, false);
                }
            }
            finally
            {
                _dgramReceiveSemaphore.Release();
            }
        }
        catch (IOException)
        {
            // ignore: it's a datagram anyway
        }
    }

    private uint _datagramFrameId;
    public void Send(ReadOnlySpan<byte> message, bool background, bool reliable)
    {
        if (reliable)
        {
            var pair = QuicUtilities.BuildFrame(message);
            (background ? _backgroundSendQueue : _mainSendQueue).Writer.TryWrite(pair); // todo: log on error
            Interlocked.Increment(ref background ? ref _bgQueueMessages : ref _mainQueueMessages);
        }
        else
        {
            var pair = QuicUtilities.BuildFrame(message, Interlocked.Increment(ref _datagramFrameId));
            SendAsNewStream(pair).LogFailure(this);
        }
    }

    private async ValueTask SendAsNewStream(SliceMemoryOwner<byte> data)
    {
        using var _ = data;
        Interlocked.Increment(ref _dgramOpenStreams);
        var stream = await Connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, _cancellationToken).ConfigureAwait(false);
        MsQuicHack.SetStreamPriority(stream, ModConstants.DgramStreamPriority);
        await stream.WriteAsync(data.Memory, true, _cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _dgramQueueStreams);
        await dgramStreamDisposeQueue.Writer.WriteAsync((stream, DateTime.UtcNow + TimeSpan.FromSeconds(1))).ConfigureAwait(false);
    }

    public void Dispose()
    {
        FailureReason ??= "Closed";
        IsConnected = false;
        HandleClientClosed();
        _cancellationTokenSource.Dispose();
        _mainStream.Dispose();
        _backgroundStream.Dispose();
        Connection.DisposeAsync().LogFailure();
    }
}