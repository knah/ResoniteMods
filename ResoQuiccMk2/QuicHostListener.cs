using System.Net;
using System.Net.Quic;
using System.Security.Cryptography.X509Certificates;
using Elements.Core;
using FrooxEngine;
using ResoQuiccMk2.Quic;
using ResoQuiccMk2.Utils;

namespace ResoQuiccMk2;

public class QuicHostListener : IListener
{
    private readonly IPEndPoint _bindAddress;
    private readonly string _appId;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly X509Certificate2 _certificate = CertificateUtil.GenerateSelfSignedCertificate("ResoQuicc Host", false, true);
    
    private int _connectionCounter;


    public QuicHostListener(IPEndPoint bindAddress, string appId)
    {
        _appId = appId;
        _bindAddress = bindAddress;
        Task.Run(DoConnect).LogFailure(this);
    }

    private async Task DoConnect()
    {
        var options = QuicUtilities.GetHostServerOptions(_bindAddress, _certificate, _appId);

        await using var listener = await QuicListener.ListenAsync(options, _cancellationTokenSource.Token).ConfigureAwait(false);

        var externalIp = ResoQuiccMod.FetchExternalIpForHost.GetValueOrDefault()
            ? new IPEndPoint(IPAddress.Parse(await QuicUtilities.FetchExternalAddress()), listener.LocalEndPoint.Port)
            : listener.LocalEndPoint;

        var overrideAddress = ResoQuiccMod.AdvertiseIPOverride.GetValueOrDefault();
        if (!string.IsNullOrWhiteSpace(overrideAddress))
        {
            if (overrideAddress.Contains(':'))
                externalIp = IPEndPoint.Parse(overrideAddress);
            else
                externalIp.Address = IPAddress.Parse(overrideAddress);
        }

        GlobalUris = [new Uri($"{ModConstants.QuicDirectScheme}://{externalIp}/#chs={_certificate.CertificateHashString()}")];
        IsActive = true;
        UniLog.Log($"QUIC listener started on {GlobalUris.First()}");
        
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            var incomingConnection = await listener.AcceptConnectionAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            
            Task.Run(() => ClientConnectionHandler(incomingConnection)).LogFailure(incomingConnection.AsDisposable());
        }
    }
    
    
    private async Task ClientConnectionHandler(QuicConnection connection)
    {
        var newConnectionId = Interlocked.Increment(ref _connectionCounter);
        
        UniLog.Log($"New client with id {newConnectionId} from {connection.RemoteEndPoint}");

        var mainStream = await connection.AcceptInboundStreamAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        var mainEmptyFrame = await mainStream.ReadUleb128Async(_cancellationTokenSource.Token);
        await mainStream.WriteUleb128Async(0, _cancellationTokenSource.Token).ConfigureAwait(false);
        var backgroundStream = await connection.AcceptInboundStreamAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        var bgEmptyFrame = await backgroundStream.ReadUleb128Async(_cancellationTokenSource.Token);
        await backgroundStream.WriteUleb128Async(0, _cancellationTokenSource.Token).ConfigureAwait(false);

        MsQuicHack.SetStreamPriority(mainStream, ModConstants.MainStreamPriority);
        MsQuicHack.SetStreamPriority(backgroundStream, ModConstants.BackgroundStreamPriority);
        
        // todo: handshake?
        
        var clientCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        var qcc = new QuicClientConnection(connection.RemoteEndPoint, _appId);
        qcc.NewData += m => NewData?.Invoke(m);
        qcc.StatsUpdated += (c, s) => StatsUpdated?.Invoke(c, s);
        var client = new ClientConnection(clientCancellationTokenSource, qcc.HandleEvent, connection, mainStream, backgroundStream);
        qcc.AttachConnection(client);
    }

    public bool IsActive { get; private set; }
    public IEnumerable<Uri> LocalUris { get; } = [];
    public IEnumerable<Uri> GlobalUris { get; private set; } = [];
    public bool GlobalUrisReady => IsActive;

    public void GlobalAnnounceRefresh()
    {
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }

    public event DataEvent? NewData;
    public event StatsEvent? StatsUpdated;
}