using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public sealed class LipSyncCalibrationWindow : EditorWindow
{
    private AlignedMouthDriver driver;
    private AlignedSpeechSegment sample;
    private int channel = 17;
    private float amount, seconds;
    private string status = "选择角色，逐通道检查或载入口型样本。";
    private readonly float[] weights = new float[55];
    private CancellationTokenSource cancellation;
    [MenuItem("Tools/Voice Chat/Mouth Calibration")]
    public static void Open() { GetWindow<LipSyncCalibrationWindow>("口型校准"); }
    private void OnGUI()
    {
        var next = (AlignedMouthDriver)EditorGUILayout.ObjectField("角色口型组件", driver, typeof(AlignedMouthDriver), true);
        if (next != driver) { driver?.Release(true); driver = next; }
        channel = EditorGUILayout.Popup("微软通道", channel, MouthChannels.Names);
        amount = EditorGUILayout.Slider("通道权重", amount, 0, 1);
        if (GUILayout.Button("预览当前嘴部通道") && driver != null)
        {
            Array.Clear(weights, 0, 55); if (MouthChannels.IsMouth(channel)) weights[channel] = amount;
            driver.Apply(weights); driver.Render(0); SceneView.RepaintAll();
        }
        if (GUILayout.Button("恢复中性姿态")) { driver?.Release(true); SceneView.RepaintAll(); }
        if (GUILayout.Button("载入本地样本"))
        {
            string path = EditorUtility.OpenFilePanel("载入口型样本", LipSyncProjectTools.Output, "json");
            if (!string.IsNullOrEmpty(path)) try { sample = JsonConvert.DeserializeObject<AlignedSpeechSegment>(File.ReadAllText(path)); status = "已载入 " + sample.mode; }
                catch { status = "样本格式无效"; sample = null; }
        }
        if (GUILayout.Button("获取最近一次试听的小句样本"))
        { sample = FindObjectOfType<AlignedMouthPlayback>()?.LastSegment; status = sample == null ? "先在播放模式试听语音" : "已获取 " + sample.mode; }
        if (sample != null)
        {
            seconds = EditorGUILayout.Slider("音频时间（秒）", seconds, 0, (float)sample.Duration);
            if (GUILayout.Button("预览时间轴姿态") && driver != null)
            { sample.Sample(seconds, weights); driver.Apply(weights); driver.Render(0); SceneView.RepaintAll(); }
            if (GUILayout.Button("保存音频与动画样本"))
            {
                string path = EditorUtility.SaveFilePanel("保存样本", LipSyncProjectTools.Output, "mouth-sample", "json");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, JsonConvert.SerializeObject(sample));
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("A：同音频 · SALSA")) Replay(true);
                if (GUILayout.Button("B：同音频 · 连续口型")) Replay(false);
            }
        }
        if (GUILayout.Button("停止回放")) cancellation?.Cancel();
        EditorGUILayout.HelpBox(status, MessageType.Info);
    }
    private async void Replay(bool salsa)
    {
        cancellation?.Cancel(); var current = cancellation = new CancellationTokenSource();
        try
        {
            driver?.Release(true);
            var controller = FindObjectOfType<VoiceChatController>();
            if (controller == null) return;
            await controller.ReplayMouthSampleAsync(sample, salsa, current.Token); status = "回放完成 · " + controller.MouthStatus;
        }
        catch (OperationCanceledException) { status = "已停止"; }
        catch (Exception e) { status = e.Message; }
        finally { current.Dispose(); if (ReferenceEquals(cancellation, current)) cancellation = null; Repaint(); }
    }
    private void OnDisable() { driver?.Release(true); cancellation?.Cancel(); }
}
