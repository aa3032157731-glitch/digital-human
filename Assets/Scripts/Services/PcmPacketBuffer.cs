using System;
using System.Collections.Generic;

// Network threads enqueue immutable packets; the main thread drains PCM.
public sealed class PcmPacketBuffer
{
    private readonly object gate = new object();
    private readonly Queue<byte[]> packets = new Queue<byte[]>();
    private byte[] current;
    private int offset, bytes, limit;
    private long generation;
    private bool ended, open;
    private string error;
    public int PeakBytes { get; private set; }
    public long Generation { get { lock (gate) return generation; } }
    public int PendingBytes { get { lock (gate) return bytes; } }
    public bool IsDrained { get { lock (gate) return ended && bytes == 0; } }
    public string Error { get { lock (gate) return error; } }
    public long Begin(int capacityBytes)
    {
        lock (gate)
        {
            ++generation;
            packets.Clear(); current = null; offset = bytes = PeakBytes = 0;
            limit = Math.Max(2, capacityBytes); ended = false; open = true; error = null;
            return generation;
        }
    }
    public void Clear()
    {
        lock (gate)
        {
            ++generation; open = false; ended = true;
            packets.Clear(); current = null; offset = bytes = 0;
        }
    }
    public bool Append(long id, byte[] packet)
    {
        lock (gate)
        {
            if (id != generation || !open || packet == null || packet.Length == 0) return false;
            if (packet.Length > limit - bytes)
            {
                error = "音频积压超过缓冲上限，本轮朗读已停止。";
                open = false;
                return false;
            }
            packets.Enqueue(packet); bytes += packet.Length;
            PeakBytes = Math.Max(PeakBytes, bytes);
            return true;
        }
    }
    public void End(long id)
    {
        lock (gate)
        {
            if (id != generation || !open) return;
            open = false; ended = true;
            if ((bytes & 1) != 0) error = "音频流以不完整的 PCM 样本结束。";
        }
    }
    public int Read(float[] destination, int count)
    {
        lock (gate)
        {
            count = Math.Min(Math.Min(count, destination.Length), bytes / 2);
            for (int i = 0; i < count; ++i)
            {
                int lo = ReadByte();
                destination[i] = (short)(lo | (ReadByte() << 8)) / 32768f;
            }
            return count;
        }
    }
    private int ReadByte()
    {
        if (current == null) { current = packets.Dequeue(); offset = 0; }
        int result = current[offset++]; --bytes;
        if (offset == current.Length) current = null;
        return result;
    }
}
