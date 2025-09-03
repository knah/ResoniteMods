using System.Buffers;

namespace ResoQuiccMk2.Utils;

public readonly struct SliceMemoryOwner<T>(IMemoryOwner<T> owner, Memory<T> memory) : IMemoryOwner<T>
{
    public void Dispose()
    {
        owner.Dispose();
    }

    public Memory<T> Memory { get; } = memory;
}