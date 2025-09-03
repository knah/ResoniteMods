using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Elements.Core;
using ResoniteModLoader;

namespace ResoQuiccMk2.Utils;

public static class Extensions
{
    public static async ValueTask<T> ReadValueAsync<T>(this Stream stream, CancellationToken cancellationToken = default) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        using var memHandle = MemoryPool<byte>.Shared.Rent(size);
        var target = memHandle.Memory[..size];
        await stream.ReadExactlyAsync(target, cancellationToken);
        return MemoryMarshal.Cast<byte, T>(target.Span)[0];
    }
    
    public static async ValueTask WriteValueAsync<T>(this Stream stream, T value, CancellationToken cancellationToken = default) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        using var memHandle = MemoryPool<byte>.Shared.Rent(size);
        var target = memHandle.Memory[..size];
        MemoryMarshal.Cast<byte, T>(target.Span)[0] = value;
        await stream.WriteAsync(target, cancellationToken);
    }

    public static async ValueTask<long> ReadUleb128Async(this Stream stream, CancellationToken cancellationToken = default)
    {
        long result = 0;
        var position = 0;
        while (true)
        {
            var b = (long) await stream.ReadValueAsync<byte>(cancellationToken).ConfigureAwait(false);
            result |= (b & 0x7Fu) << position;
            if ((b & 0x80) == 0)
                return result;
            
            position += 7;
        }
    }

    public static async ValueTask WriteUleb128Async(this Stream stream, long value, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        
        using var bufferHandle = MemoryPool<byte>.Shared.Rent(16);
        var memory = bufferHandle.Memory;
        var length = ReadWriteUtils.FormatUleb128(value, memory.Span);
        
        await stream.WriteAsync(memory[..length], cancellationToken).ConfigureAwait(false);
    }
    
    public static async ValueTask WriteStringAsync(this Stream stream, string value, CancellationToken cancellationToken = default)
    {
        var needBytes = Encoding.UTF8.GetByteCount(value);
        using var bufferHandle = MemoryPool<byte>.Shared.Rent(needBytes);
        var actualByteCount = Encoding.UTF8.GetBytes(value, bufferHandle.Memory.Span);
        await stream.WriteUleb128Async(actualByteCount, cancellationToken);
        await stream.WriteAsync(bufferHandle.Memory[..actualByteCount], cancellationToken);
    }
    
    public static async ValueTask<string> ReadStringAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        var needBytes = (int) await stream.ReadUleb128Async(cancellationToken);
        using var bufferHandle = MemoryPool<byte>.Shared.Rent(needBytes);
        await stream.ReadExactlyAsync(bufferHandle.Memory[..needBytes], cancellationToken);
        return Encoding.UTF8.GetString(bufferHandle.Memory.Span[..needBytes]);
    }
    
    public static async ValueTask<T[]> ReadArrayAsync<T>(this Stream stream, Func<Stream, CancellationToken, ValueTask<T>> elementReader, CancellationToken cancellationToken = default)
    {
        var numElements = await stream.ReadUleb128Async(cancellationToken);
        var result = new T[numElements];
        for (var i = 0; i < numElements; i++) 
            result[i] = await elementReader(stream, cancellationToken);
        
        return result;
    }

    public static void LogFailure(this Task task, IDisposable? toDispose = null, [CallerArgumentExpression(nameof(task))] string? taskName = null)
    {
        task.ContinueWith(_ =>
        {
            if (!task.IsFaulted) return;
            
            UniLog.Error($"Task {taskName} failed: {task.Exception.Message} {task.Exception.StackTrace}");
            toDispose?.Dispose();
        });
    }
    
    public static void LogFailure(this ValueTask task, IDisposable? toDispose = null, [CallerArgumentExpression(nameof(task))] string? taskName = null)
    {
        if (task.IsCompleted)
        {
            if (!task.IsFaulted) return;
            
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                UniLog.Error($"Task {taskName} failed: {ex.Message} {ex.StackTrace}");
                toDispose?.Dispose();
            }
            
            return;
        }
        
        task.AsTask().ContinueWith(innerTask =>
        {
            if (!innerTask.IsFaulted) return;
            
            UniLog.Error($"Task {taskName} failed: {innerTask.Exception.Message} {innerTask.Exception.StackTrace}");
            toDispose?.Dispose();
        });
    }

    public static IDisposable AsDisposable(this IAsyncDisposable disposable,
        [CallerArgumentExpression(nameof(disposable))] string? taskName = null) =>
        new AsyncDisposableAsDisposableWrapper(disposable, taskName);

    public static SliceMemoryOwner<T> AsSlice<T>(this IMemoryOwner<T> memoryOwner, int length) =>
        new(memoryOwner, memoryOwner.Memory[..length]);

    public static SliceMemoryOwner<T> ToOwnedSlice<T>(this ReadOnlySpan<T> data)
    {
        var owner = MemoryPool<T>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);
        return owner.AsSlice(data.Length);
    }

    public static T? GetValueOrDefault<T>(this ModConfigurationKey<T> key)
    {
        try
        {
            return key.Value ?? (key.TryComputeDefaultTyped(out var d) ? d : default);
        } catch(NullReferenceException)
        {
            return key.TryComputeDefaultTyped(out var d) ? d : default;
        }
    }
}