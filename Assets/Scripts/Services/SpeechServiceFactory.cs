using UnityEngine;

// Builds the TTS service of one provider from a resolved settings snapshot (speech preview, connection test).
public static class SpeechServiceFactory
{
    // `config` receives the temporary ScriptableObject some services need; destroy it after disposing the service.
    public static ITTSService Create(RuntimeVoiceChatSettings snapshot, string provider, bool allowContinuousLipSync, out ScriptableObject config)
    {
        config = null;
        switch (provider)
        {
            case "AzureSpeech":
                return allowContinuousLipSync && snapshot.azureTts.continuousLipSync
                    ? (ITTSService)new AzureFacialTTSService(snapshot.azureTts) : new AzureTTSService(snapshot.azureTts);
            case "Doubao":
                var doubao = ScriptableObject.CreateInstance<DoubaoTTSConfig>(); config = doubao;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(snapshot.doubaoTts), doubao);
                return new DoubaoTTSService(doubao, snapshot.doubaoTts.enableVoiceInstruction ? snapshot.doubaoTts.voiceInstruction : "", "");
            case "XunfeiOnline":
                var online = ScriptableObject.CreateInstance<XFOnlineTTSConfig>(); config = online;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(snapshot.xunfeiOnlineTts), online);
                return new XFOnlineTTSService(online);
            default:
                var smart = ScriptableObject.CreateInstance<XFTTSConfig>(); config = smart;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(snapshot.xunfeiSuperSmartTts), smart);
                return new XFTTSService(smart);
        }
    }

    public static void Release(ScriptableObject config)
    {
        if (config == null) return;
        if (Application.isPlaying) Object.Destroy(config); else Object.DestroyImmediate(config);
    }
}
