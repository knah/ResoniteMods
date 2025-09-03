namespace ResoQuiccMk2;

public struct StatCounter
{
    public long BytesReceived;
    public long BytesSent;
    public int PacketsReceived;
    public int PacketsSent;
    
    private DateTime _lastUpdateSubmission;
    public DateTime LastReceivedPacket;

    public bool OnPacketSent(int length)
    {
        Interlocked.Add(ref BytesSent, length);
        Interlocked.Increment(ref PacketsSent);
        return CheckUpdateSubmission();
    }
    
    public bool OnPacketReceived(int length)
    {
        Interlocked.Add(ref BytesReceived, length);
        Interlocked.Increment(ref PacketsReceived);
        LastReceivedPacket = DateTime.Now;
        return CheckUpdateSubmission();
    }

    private bool CheckUpdateSubmission()
    {
        if (DateTime.UtcNow - _lastUpdateSubmission > TimeSpan.FromSeconds(1))
        {
            _lastUpdateSubmission = DateTime.UtcNow;
            return true;
        }
        return false;
    }
}