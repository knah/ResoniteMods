namespace ResoQuiccMk2.Quic;

internal struct QUIC_STATISTICS_V2
{
    internal ulong CorrelationId;

    internal uint _bitfield;

    internal uint VersionNegotiation
    {
        get { return _bitfield & 0x1u; }

        set { _bitfield = (_bitfield & ~0x1u) | (value & 0x1u); }
    }

    internal uint StatelessRetry
    {
        get { return (_bitfield >> 1) & 0x1u; }

        set { _bitfield = (_bitfield & ~(0x1u << 1)) | ((value & 0x1u) << 1); }
    }

    internal uint ResumptionAttempted
    {
        get { return (_bitfield >> 2) & 0x1u; }

        set { _bitfield = (_bitfield & ~(0x1u << 2)) | ((value & 0x1u) << 2); }
    }

    internal uint ResumptionSucceeded
    {
        get { return (_bitfield >> 3) & 0x1u; }

        set { _bitfield = (_bitfield & ~(0x1u << 3)) | ((value & 0x1u) << 3); }
    }

    internal uint GreaseBitNegotiated
    {
        get { return (_bitfield >> 4) & 0x1u; }

        set { _bitfield = (_bitfield & ~(0x1u << 4)) | ((value & 0x1u) << 4); }
    }

    internal uint EcnCapable
    {
        get { return (_bitfield >> 5) & 0x1u; }

        set { _bitfield = (_bitfield & ~(0x1u << 5)) | ((value & 0x1u) << 5); }
    }

    internal uint RESERVED
    {
        get { return (_bitfield >> 6) & 0x3FFFFFFu; }

        set { _bitfield = (_bitfield & ~(0x3FFFFFFu << 6)) | ((value & 0x3FFFFFFu) << 6); }
    }

    internal uint Rtt;
    internal uint MinRtt;
    internal uint MaxRtt;
    internal ulong TimingStart;
    internal ulong TimingInitialFlightEnd;
    internal ulong TimingHandshakeFlightEnd;
    internal uint HandshakeClientFlight1Bytes;
    internal uint HandshakeServerFlight1Bytes;
    internal uint HandshakeClientFlight2Bytes;
    internal ushort SendPathMtu;
    internal ulong SendTotalPackets;
    internal ulong SendRetransmittablePackets;
    internal ulong SendSuspectedLostPackets;
    internal ulong SendSpuriousLostPackets;
    internal ulong SendTotalBytes;
    internal ulong SendTotalStreamBytes;
    internal uint SendCongestionCount;
    internal uint SendPersistentCongestionCount;
    internal ulong RecvTotalPackets;
    internal ulong RecvReorderedPackets;
    internal ulong RecvDroppedPackets;
    internal ulong RecvDuplicatePackets;
    internal ulong RecvTotalBytes;
    internal ulong RecvTotalStreamBytes;
    internal ulong RecvDecryptionFailures;
    internal ulong RecvValidAckFrames;
    internal uint KeyUpdateCount;
    internal uint SendCongestionWindow;
    internal uint DestCidUpdateCount;
    internal uint SendEcnCongestionCount;
}