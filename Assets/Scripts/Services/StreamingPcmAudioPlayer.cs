using System;
using UnityEngine;
using UnityEngine.Events;

public enum PcmPlaybackState { Idle, Buffering, Playing, Rebuffering, Draining, Completed, Cancelled, Failed }

[RequireComponent(typeof(AudioSource))]
public class StreamingPcmAudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    [Range(5, 120)] public int clipSeconds = 30;
    [Range(0.02f, 0.5f)] public float startBufferSeconds = 0.12f;
    [Range(0.05f, 1f)] public float resumeBufferSeconds = 0.2f;
    [Range(5, 120)] public int maxQueuedSeconds = 120;
    public bool logDiagnostics;
    public UnityEvent onPlaybackStarted = new UnityEvent();
    public UnityEvent onPlaybackStopped = new UnityEvent();
    public event Action PlaybackStarted;
    public event Action PlaybackStopped;
    public event Action<string> PlaybackError;
    public PcmPlaybackState State { get; private set; }
    public int UnderrunCount { get; private set; }
    public bool IsResume { get; private set; }
    public double FirstAudioMilliseconds { get; private set; }
    public double FirstPlaybackMilliseconds { get; private set; }
    public float BufferedSeconds => sampleRate > 0 ? (float)(written - played) / sampleRate : 0;
    public float QueuedSeconds => sampleRate > 0 ? packets.PendingBytes / (sampleRate * 2f) : 0;
    public bool IsPlaying => playbackActive;
    public long StreamId => packets.Generation;
    public int SampleRate => sampleRate;
    public long PlayedSamples => playbackActive
        ? Math.Max(played, Math.Min(scheduledWritten, playBase + (long)(Math.Max(0, AudioSettings.dspTime - playStart) * sampleRate)))
        : played;
    public bool IsAudible => playbackActive && AudioSettings.dspTime >= playStart && AudioSettings.dspTime < playEnd && !AudioListener.pause;
    public int QueueFreeBytes => Math.Max(0, sampleRate * 2 * Mathf.Clamp(maxQueuedSeconds, 5, 120) - packets.PendingBytes);

    private readonly PcmPacketBuffer packets = new PcmPacketBuffer();
    private readonly float[] block = new float[1024];
    private readonly float[] silence = new float[1024];
    private readonly System.Collections.Generic.Dictionary<int, float[]> tails = new System.Collections.Generic.Dictionary<int, float[]>();
    private AudioClip ownedClip;
    private int sampleRate, capacity;
    private long written, played, cleared, playBase, scheduledWritten;
    private double playStart, playEnd, began;
    private bool playbackActive, startedOnce, hasAudio;

    private void Awake() { if (audioSource == null) audioSource = GetComponent<AudioSource>(); }
    private void OnDisable() { StopAndClear(); }
    private void OnDestroy() { StopAndClear(); }

    public long BeginStream(int outputSampleRate, int outputChannels = 1)
    {
        StopAndClear();
        if (outputChannels != 1 || outputSampleRate < 8000 || outputSampleRate > 48000)
            throw new ArgumentException("播放器要求 8–48 kHz 单声道 PCM16。");
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        sampleRate = outputSampleRate;
        capacity = sampleRate * Mathf.Clamp(clipSeconds, 5, 120);
        ownedClip = AudioClip.Create("Streaming TTS PCM", capacity, 1, sampleRate, false);
        audioSource.clip = ownedClip; audioSource.loop = true; audioSource.pitch = 1;
        audioSource.dopplerLevel = 0;
        written = played = cleared = 0; startedOnce = hasAudio = false;
        UnderrunCount = 0; IsResume = false; FirstAudioMilliseconds = FirstPlaybackMilliseconds = -1;
        began = Time.realtimeSinceStartupAsDouble;
        State = PcmPlaybackState.Buffering;
        return packets.Begin(sampleRate * 2 * Mathf.Clamp(maxQueuedSeconds, 5, 120));
    }

    // Safe on network threads; explicit IDs reject old-turn callbacks.
    public bool EnqueuePcm(long streamId, byte[] bytes) => packets.Append(streamId, bytes);
    public void EndStream(long streamId) => packets.End(streamId);
    public void AppendPcm16LittleEndian(byte[] bytes) => packets.Append(packets.Generation, bytes);
    public void EndStream() => packets.End(packets.Generation);

    private void Update() => Tick(AudioSettings.dspTime);

    private void Tick(double now)
    {
        double tickStarted = Time.realtimeSinceStartupAsDouble;
        if (ownedClip == null) return;
        if (packets.Error != null) { Fail(packets.Error); return; }
        if (playbackActive)
        {
            long next = Math.Min(written, playBase + (long)(Math.Max(0, now - playStart) * sampleRate));
            played = Math.Max(played, next);
            if (now >= playEnd)
            {
                played = scheduledWritten;
                audioSource.Stop();
                SetActive(false);
                if (!packets.IsDrained) { ++UnderrunCount; State = PcmPlaybackState.Rebuffering; }
            }
        }
        while (cleared < played)
        {
            int count = (int)Math.Min(1024, played - cleared);
            ownedClip.SetData(count == 1024 ? silence : ZeroTail(count), (int)(cleared % capacity));
            cleared += count;
        }
        // Bound frame work and reserve a guard region; never overwrite unread audio.
        var deadline = Time.realtimeSinceStartupAsDouble + 0.002;
        int budget = 8192;
        while (budget > 0 && Time.realtimeSinceStartupAsDouble < deadline)
        {
            int count = (int)Math.Min(Math.Min(1024, capacity - (written - played) - 1024), packets.PendingBytes / 2);
            if (count <= 0) break;
            var data = count == 1024 ? block : Tail(count);
            count = packets.Read(data, count);
            if (count == 0) break;
            if (!ownedClip.SetData(data, (int)(written % capacity))) { Fail("写入音频缓冲失败。"); return; }
            written += count; budget -= count;
            if (!hasAudio) { hasAudio = true; FirstAudioMilliseconds = (Time.realtimeSinceStartupAsDouble - began) * 1000; }
        }
        // A deadline may expire during conversion/SetData. Only the previous
        // scheduled range was audible; newly written samples still need playback.
        now += Time.realtimeSinceStartupAsDouble - tickStarted;
        if (playbackActive && now >= playEnd)
        {
            played = scheduledWritten;
            audioSource.Stop(); SetActive(false);
            ++UnderrunCount; State = PcmPlaybackState.Rebuffering;
        }
        bool drained = packets.IsDrained;
        if (drained && played == written) { Finish(); return; }
        if (!playbackActive && written > played)
        {
            float threshold = startedOnce ? resumeBufferSeconds : startBufferSeconds;
            if (drained || BufferedSeconds >= Mathf.Clamp(threshold, 0.02f, 1f))
            {
                IsResume = startedOnce; startedOnce = true;
                playBase = played; playStart = now + 0.02;
                audioSource.timeSamples = (int)(played % capacity);
                audioSource.PlayScheduled(playStart);
                if (FirstPlaybackMilliseconds < 0) FirstPlaybackMilliseconds = (Time.realtimeSinceStartupAsDouble - began + 0.02) * 1000;
                SetActive(true);
            }
        }
        if (playbackActive)
        {
            // DSP deadline stops at valid PCM even when Update stalls for a full loop.
            playEnd = playStart + (written - playBase) / (double)sampleRate;
            scheduledWritten = written;
            audioSource.SetScheduledEndTime(playEnd);
            State = drained ? PcmPlaybackState.Draining : PcmPlaybackState.Playing;
        }
    }
    private float[] Tail(int count)
    {
        if (!tails.TryGetValue(count, out var value)) tails[count] = value = new float[count];
        return value;
    }
    private float[] ZeroTail(int count) { var value = Tail(count); Array.Clear(value, 0, value.Length); return value; }
    private void SetActive(bool value)
    {
        if (playbackActive == value) return;
        playbackActive = value;
        if (value) { PlaybackStarted?.Invoke(); onPlaybackStarted?.Invoke(); }
        else { PlaybackStopped?.Invoke(); onPlaybackStopped?.Invoke(); }
    }
    private void ReleaseClip()
    {
        if (audioSource != null) { audioSource.Stop(); if (audioSource.clip == ownedClip) audioSource.clip = null; }
        if (ownedClip != null)
        {
            if (Application.isPlaying) Destroy(ownedClip); else DestroyImmediate(ownedClip);
            ownedClip = null;
        }
        SetActive(false);
    }
    private void Finish()
    {
        State = PcmPlaybackState.Completed;
        if (logDiagnostics) Debug.Log($"TTS playback: firstPCM={FirstAudioMilliseconds:F0}ms firstSound={FirstPlaybackMilliseconds:F0}ms underruns={UnderrunCount} peakQueue={packets.PeakBytes}B");
        ReleaseClip();
    }
    private void Fail(string message)
    {
        packets.Clear(); ReleaseClip(); State = PcmPlaybackState.Failed;
        PlaybackError?.Invoke(message);
    }
    public void StopAndClear()
    {
        double started = Time.realtimeSinceStartupAsDouble;
        bool hadStream = ownedClip != null;
        packets.Clear(); ReleaseClip(); written = played = cleared = 0;
        State = PcmPlaybackState.Cancelled;
        if (logDiagnostics && hadStream) Debug.Log($"TTS playback cancel={(Time.realtimeSinceStartupAsDouble - started) * 1000:F2}ms");
    }
}
