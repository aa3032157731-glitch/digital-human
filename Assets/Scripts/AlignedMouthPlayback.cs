using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(20000)]
public sealed class AlignedMouthPlayback : MonoBehaviour
{
    public StreamingPcmAudioPlayer player;
    public CharacterModelSwitcher modelSwitcher;
    public AlignedMouthDriver[] drivers;
    public string Status { get; private set; } = "SALSA";
    public AlignedSpeechSegment LastSegment { get; private set; }
    private sealed class Entry { public AlignedSpeechSegment data; public long start; public int submitted; }
    private sealed class Inbox
    {
        public readonly ConcurrentQueue<AlignedSpeechSegment> queue = new ConcurrentQueue<AlignedSpeechSegment>();
        public volatile bool complete;
    }
    private Inbox incoming = new Inbox();
    private Action<AlignedSpeechSegment> receiveHandler;
    private Action completeHandler;
    private readonly List<Entry> timeline = new List<Entry>();
    private readonly float[] channels = new float[55];
    private IAlignedTTSService source;
    private IAlignedMouthTarget active;
    private long streamId, acceptedSamples;
    private int expectedSequence;
    private bool ended;

    public void Begin(IAlignedTTSService service, long id)
    {
        Clear(); source = service; streamId = id; Status = "正在准备连续口型";
        var queue = incoming;
        receiveHandler = segment => queue.queue.Enqueue(segment);
        completeHandler = () => queue.complete = true;
        service.SegmentReady += receiveHandler;
        service.Completed += completeHandler;
    }
    public void Clear()
    {
        if (source != null) { source.SegmentReady -= receiveHandler; source.Completed -= completeHandler; }
        source = null; ended = false; expectedSequence = 0; acceptedSamples = 0;
        incoming = new Inbox();
        timeline.Clear(); active?.Release(); active = null;
    }
    public void UseLegacy() { Clear(); Status = "SALSA"; }
    private void Update()
    {
        if (source == null) return;
        if (player == null || player.StreamId != streamId || player.State == PcmPlaybackState.Failed || player.State == PcmPlaybackState.Cancelled)
        { Clear(); return; }
        while (incoming.queue.TryDequeue(out var data))
        {
            if (data.sequence != expectedSequence++ || data.sampleRate != player.SampleRate)
            { Status = "口型小句顺序或采样率错误"; _ = source.CancelAsync(); player.StopAndClear(); Clear(); return; }
            timeline.Add(new Entry { data = data, start = acceptedSamples }); acceptedSamples += data.SampleCount; LastSegment = data;
        }
        int budget = 32768;
        foreach (var entry in timeline)
        {
            int count = Math.Min(Math.Min(entry.data.pcm.Length - entry.submitted, player.QueueFreeBytes), budget) & ~1;
            if (count > 0)
            {
                var bytes = new byte[count]; Buffer.BlockCopy(entry.data.pcm, entry.submitted, bytes, 0, count);
                if (!player.EnqueuePcm(streamId, bytes)) break;
                entry.submitted += count; budget -= count;
            }
            if (entry.submitted < entry.data.pcm.Length || budget <= 0) break;
        }
        if (incoming.complete && incoming.queue.IsEmpty && !ended && timeline.TrueForAll(e => e.submitted == e.data.pcm.Length))
        { player.EndStream(streamId); ended = true; }

        long played = player.PlayedSamples;
        while (timeline.Count > 0 && played >= timeline[0].start + timeline[0].data.SampleCount)
        { source.AcknowledgeSegment(timeline[0].data.sequence); timeline.RemoveAt(0); }
        var root = modelSwitcher != null ? modelSwitcher.GetCurrentModelRoot() : null;
        var next = root != null ? root.GetComponent<IAlignedMouthTarget>() : null;
        if (next != active) { active?.Release(true); active = next; }
        if (player.State == PcmPlaybackState.Completed)
        { active?.Release(); Clear(); return; }
        if (timeline.Count == 0 || !player.IsAudible) { active?.Release(); return; }
        var current = timeline[0];
        if (current.data.mode == MouthAnimationMode.Salsa || active == null || !active.HasMapping)
        {
            active?.Release(true);
            Status = "SALSA · " + (active == null || !active.HasMapping ? "当前角色缺少映射" : current.data.fallbackReason);
            return;
        }
        double seconds = (played - current.start) / (double)player.SampleRate + active.Tuning.lipSyncOffsetMs / 1000.0;
        current.data.Sample(Math.Max(0, seconds), channels); active.Apply(channels);
        Status = current.data.mode == MouthAnimationMode.BlendShapes ? "连续口型" : "Viseme · " + current.data.fallbackReason;
    }
    private void OnDisable() { Clear(); }
}
