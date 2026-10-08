using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public interface IAlignedTTSService : ITTSService
{
    event Action<AlignedSpeechSegment> SegmentReady;
    void AcknowledgeSegment(int sequence);
}

public enum MouthAnimationMode { BlendShapes, Visemes, Salsa }

[Serializable]
public sealed class SpeechViseme
{
    public double seconds;
    public int id;
}

// Once published, arrays belong to the segment and must not be modified.
[Serializable]
public sealed class AlignedSpeechSegment
{
    public int sequence;
    public int sampleRate = 24000;
    public byte[] pcm;
    public float[][] frames;
    public SpeechViseme[] visemes;
    public MouthAnimationMode mode;
    public string fallbackReason;
    public double Duration => pcm.Length / (2.0 * sampleRate);
    public long SampleCount => pcm.LongLength / 2;

    public void Sample(double seconds, float[] output)
    {
        Array.Clear(output, 0, output.Length);
        if (mode == MouthAnimationMode.BlendShapes && frames != null && frames.Length > 0)
        {
            double frame = Math.Max(0, seconds * 60);
            int a = (int)Math.Min(frame, frames.Length - 1), b = Math.Min(a + 1, frames.Length - 1);
            float mix = (float)Math.Min(1, frame - a);
            // A short silent audio tail must not freeze the final mouth pose.
            float tail = (float)Math.Max(0, 1 - Math.Max(0, seconds - (frames.Length - 1) / 60.0) / .1);
            for (int i = 0; i < 55; ++i) output[i] = (frames[a][i] + (frames[b][i] - frames[a][i]) * mix) * tail;
        }
        else if (mode == MouthAnimationMode.Visemes && visemes != null)
        {
            int index = -1;
            for (int i = 0; i < visemes.Length && visemes[i].seconds <= seconds; ++i) index = i;
            if (index < 0) return;
            int previous = index > 0 ? visemes[index - 1].id : 0;
            float mix = (float)Math.Min(1, Math.Max(0, seconds - visemes[index].seconds) / .055);
            MouthChannels.AddViseme(previous, 1 - mix, output);
            MouthChannels.AddViseme(visemes[index].id, mix, output);
            float tail = (float)Math.Min(1, Math.Max(0, Duration - seconds) / .06);
            for (int i = 0; i < output.Length; ++i) output[i] *= tail;
        }
    }
}

public static class MouthChannels
{
    // Microsoft's documented 55-channel order is one-based; these indices are zero-based.
    public static readonly string[] Names = {
        "eyeBlinkLeft","eyeLookDownLeft","eyeLookInLeft","eyeLookOutLeft","eyeLookUpLeft","eyeSquintLeft","eyeWideLeft",
        "eyeBlinkRight","eyeLookDownRight","eyeLookInRight","eyeLookOutRight","eyeLookUpRight","eyeSquintRight","eyeWideRight",
        "jawForward","jawLeft","jawRight","jawOpen","mouthClose","mouthFunnel","mouthPucker","mouthLeft","mouthRight",
        "mouthSmileLeft","mouthSmileRight","mouthFrownLeft","mouthFrownRight","mouthDimpleLeft","mouthDimpleRight",
        "mouthStretchLeft","mouthStretchRight","mouthRollLower","mouthRollUpper","mouthShrugLower","mouthShrugUpper",
        "mouthPressLeft","mouthPressRight","mouthLowerDownLeft","mouthLowerDownRight","mouthUpperUpLeft","mouthUpperUpRight",
        "browDownLeft","browDownRight","browInnerUp","browOuterUpLeft","browOuterUpRight","cheekPuff","cheekSquintLeft",
        "cheekSquintRight","noseSneerLeft","noseSneerRight","tongueOut","headRoll","leftEyeRoll","rightEyeRoll"
    };
    public static bool IsMouth(int i) => (i >= 14 && i <= 40) || i == 51;
    public static void AddViseme(int id, float weight, float[] o)
    {
        if (id <= 0 || id > 21) return;
        float jaw = .15f, funnel = 0, pucker = 0, wide = 0, close = 0, tongue = 0, press = 0;
        switch (id)
        {
            case 1: jaw = .45f; break;
            case 2: jaw = .7f; break;
            case 3: jaw = .5f; funnel = .6f; break;
            case 4: jaw = .3f; wide = .25f; break;
            case 5: jaw = .22f; funnel = .3f; break;
            case 6: jaw = .16f; wide = .65f; break;
            case 7: jaw = .15f; pucker = .8f; break;
            case 8: jaw = .4f; funnel = .75f; break;
            case 9: jaw = .4f; pucker = .5f; break;
            case 10: jaw = .35f; funnel = .3f; wide = .2f; break;
            case 11: jaw = .4f; wide = .45f; break;
            case 12: jaw = .3f; break;
            case 13: jaw = .18f; pucker = .3f; break;
            case 14: jaw = .2f; tongue = .12f; break;
            case 15: jaw = .08f; wide = .3f; break;
            case 16: jaw = .15f; funnel = .3f; break;
            case 17: jaw = .13f; tongue = .25f; break;
            case 18: jaw = .06f; press = .55f; break;
            case 19: jaw = .12f; break;
            case 20: jaw = .25f; break;
            case 21: jaw = 0; close = 1; press = .7f; break;
        }
        o[17] += jaw * weight; o[18] += close * weight; o[19] += funnel * weight; o[20] += pucker * weight;
        o[29] += wide * weight; o[30] += wide * weight; o[35] += press * weight; o[36] += press * weight; o[51] += tongue * weight;
    }
}

public sealed class SpeechAnimationCollector
{
    private readonly SortedDictionary<int, float[]> frames = new SortedDictionary<int, float[]>();
    private readonly List<SpeechViseme> visemes = new List<SpeechViseme>();
    private string error;
    public void Add(ulong ticks, uint id, string json)
    {
        if (id <= 21 && ticks <= 1200000000UL) visemes.Add(new SpeechViseme { seconds = ticks / 10000000.0, id = (int)id });
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var obj = JObject.Parse(json);
            int start = obj.Value<int>("FrameIndex");
            var rows = obj["BlendShapes"] as JArray;
            if (rows == null || start < 0 || start + rows.Count > 7200) throw new FormatException();
            for (int i = 0; i < rows.Count; ++i)
            {
                var row = rows[i] as JArray;
                if (row == null || row.Count != 55) throw new FormatException();
                var values = new float[55];
                for (int j = 0; j < 55; ++j)
                {
                    float v = row[j].Value<float>();
                    // Live zh-CN-XiaoxiaoNeural output contains small negative mouth
                    // coefficients (e.g. -0.046) and signed eye rolls. Clamp mild
                    // mouth overshoot; unused head/eye channels must not reject speech.
                    if (float.IsNaN(v) || float.IsInfinity(v) || (MouthChannels.IsMouth(j) && (v < -.1f || v > 1.1f))) throw new FormatException();
                    values[j] = MouthChannels.IsMouth(j) ? Math.Max(0, Math.Min(1, v)) : 0;
                }
                if (frames.TryGetValue(start + i, out var existing))
                    for (int j = 0; j < 55; ++j) if (existing[j] != values[j]) throw new FormatException();
                frames[start + i] = values;
            }
        }
        catch { error = "连续动画格式不完整"; }
    }
    public AlignedSpeechSegment Complete(byte[] pcm, int sequence)
    {
        if (pcm == null || pcm.Length == 0 || (pcm.Length & 1) != 0 || pcm.Length > 24000 * 2 * 120)
            throw new InvalidOperationException("Azure 小句 PCM 无效或超过两分钟。");
        var segment = new AlignedSpeechSegment { pcm = pcm, sequence = sequence };
        var rows = new List<float[]>();
        foreach (var pair in frames)
        {
            if (pair.Key != rows.Count) { if (error == null) error = "连续动画存在缺帧"; break; }
            rows.Add(pair.Value);
        }
        visemes.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        visemes.RemoveAll(v => v.seconds > segment.Duration);
        segment.visemes = visemes.ToArray();
        if (rows.Count > 0 && error == null && rows.Count / 60.0 >= segment.Duration - .5)
        { segment.mode = MouthAnimationMode.BlendShapes; segment.frames = rows.ToArray(); }
        else
        {
            segment.fallbackReason = error ?? "音色未返回完整连续动画";
            segment.mode = visemes.Exists(v => v.id != 0) ? MouthAnimationMode.Visemes : MouthAnimationMode.Salsa;
        }
        return segment;
    }
}
