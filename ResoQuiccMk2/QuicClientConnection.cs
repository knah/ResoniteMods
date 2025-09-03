using System.Net;
using System.Text;
using Elements.Core;
using FrooxEngine;
using ResoQuiccMk2.Quic;
using ResoQuiccMk2.Utils;

namespace ResoQuiccMk2;

public class QuicClientConnection : IConnection
{
    private readonly string _appId;
    private ClientConnection? _connection;
    public StatCounter Stats;
    
    public QuicClientConnection(Uri remoteUri, string appId)
    {
        _appId = appId;
        Address = remoteUri;
        Identifier = remoteUri.ToString();
        IP = IPAddress.Parse(remoteUri.Host);
    }

    public QuicClientConnection(IPEndPoint remoteEndPoint, string appId)
    {
        _appId = appId;
        Address = new Uri($"{ModConstants.QuicDirectScheme}://{remoteEndPoint}/");
        Identifier = Address.ToString();
        IP = remoteEndPoint.Address;
    }

    internal void AttachConnection(ClientConnection? connection)
    {
        _connection = connection;
        if (connection == null)
        {
            ConnectionFailed?.Invoke(this);
        }
        else
        {
            IsOpen = true;
            Connected?.Invoke(this);
            connection.StartProcessors();
        }
    }

    internal void HandleEvent(EventType eventType, ReadOnlySpan<byte> data)
    {
        try
        {
            if (eventType == EventType.Disconnected)
            {
                _failReason = Encoding.UTF8.GetString(data);
                if (string.IsNullOrEmpty(FailReason)) _failReason = "Unspecified";
                UniLog.Log($"QuicClientConnection {Identifier} disconnected, reason: {FailReason}");
                IsOpen = false;
                Closed?.Invoke(this);
                return;
            }

            if (Stats.OnPacketReceived(data.Length)) 
                StatsUpdated?.Invoke(this, UpdateUserStats);
            var array = ModConstants.MessagesPool.GetArray(data.Length);
            data.CopyTo(array);
            var message = new RawInMessage(this, array, 0, data.Length, static m => ModConstants.MessagesPool.ReturnArray(m.Data), null);
            NewData?.Invoke(message);
        } catch (Exception ex)
        {
            UniLog.Error($"Exception in native event handler of {Identifier}: {ex.Message}", stackTrace: false);
            UniLog.Error(ex.StackTrace, stackTrace: false);
        }
    }

    internal void Send(RawOutMessage message)
    {
        if (Stats.OnPacketSent((int)message.Data.Length)) 
            StatsUpdated?.Invoke(this, UpdateUserStats);
        _connection!.Send(message.Data.GetBuffer().AsSpan(0, (int) message.Data.Length), message.Background, message.UseReliable);
    }

    public void Connect(Action<LocaleString> statusCallback)
    {
        Task.Run(DoConnect).LogFailure(this);
    }
    
    private async Task DoConnect()
    {
        try
        {
            var connection = await ClientConnection.Connect(Address.ToString(), HandleEvent, _appId).ConfigureAwait(false);
            AttachConnection(connection);
        }
        catch (Exception ex)
        {
            _failReason = ex.Message;
            ConnectionFailed?.Invoke(this);
        }
    }
    
    public void UpdateUserStats(User user)
    {
        var mainQueue = _connection?.MainQueueSize ?? 0;
        if (user.World.IsAuthority)
        {
            user.QueuedMessages = mainQueue;
            user.UploadedBytes = (ulong) Stats.BytesReceived;
            user.SetNetworkStatistic<string>("Protocol", ModConstants.QuicDirectScheme);
            user.SetNetworkStatistic("TransmittedMessages", Stats.PacketsSent);
            user.SetNetworkStatistic("ReceivedMessages", Stats.PacketsReceived);
            user.SetNetworkStatistic("BytesSent", Stats.BytesSent);
            user.SetNetworkStatistic("BytesReceived", Stats.BytesReceived);
            user.SetNetworkStatistic("PrimaryChannelQueue", mainQueue);
            user.SetNetworkStatistic("BackgroundChannelQueue", _connection?.BgQueueSize ?? 0);
            user.SetNetworkStatistic("PendingDgramCount", _connection?.PendingDgramStreams ?? 0);
            user.SetNetworkStatistic("PendingDgramOpenCount", _connection?.PendingDgramOpenStreams ?? 0);
            
            var quicStats = MsQuicHack.GetConnectionStats(_connection!.Connection);
            user.Ping = (int) (quicStats.RttUs / 1_000_000);
            user.PacketLoss = quicStats.PacketsLost / (float)Stats.PacketsSent;
            user.SetNetworkStatistic("MTU", quicStats.Mtu);
            // todo: more stats?
        }
        else
        {
            user.DownloadedBytes = (ulong)Stats.BytesReceived;
            user.SetNetworkStatistic("RemotePrimaryChannelQueue", mainQueue);
            user.SetNetworkStatistic("RemoteBackgroundChannelQueue", _connection?.BgQueueSize ?? 0);
            user.SetNetworkStatistic("RemotePendingDgramCount", _connection?.PendingDgramStreams ?? 0);
            user.SetNetworkStatistic("RemotePendingDgramOpenCount", _connection?.PendingDgramOpenStreams ?? 0);
        }
    }

    public void Close()
    {
        UniLog.Log($"Requested close on QuicClientConnection {Identifier}");
        if (IsOpen)
        {
            Closed?.Invoke(this);
            IsOpen = false;
        }
        
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose()
    {
        Close();
        _failReason ??= "Disposed";
    }

    public bool IsOpen { get; private set; }
    public string? FailReason => _connection?.FailureReason ?? _failReason;
    private string? _failReason;
    public IPAddress IP { get; }
    public Uri Address { get; }
    public string Identifier { get; }
    
    public event DataEvent? NewData;
    public event StatsEvent? StatsUpdated;
    public event ConnectionEvent? Connected;
    public event ConnectionEvent? ConnectionFailed;
    public event ConnectionEvent? Closed;
}