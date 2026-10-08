using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Materializes the new controls into main.unity; not a runtime UI replacement.
public static class AzureTtsSceneSetup
{
    [MenuItem("Tools/Voice Chat/Install Azure TTS Controls")]
    public static void Install()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
        var panel = UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>(true);
        if (panel == null) throw new InvalidOperationException("Settings panel missing");
        Transform Find(string name) => panel.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        var templatePage = Find("Page_TtsDoubao");
        var rowTemplate = Find("Row_DoubaoTTS_ApiKey");
        var tabTemplate = Find("Tab_TtsDoubao");
        foreach (var name in new[] { "Page_TtsAzure", "Page_AudioBuffer", "Tab_TtsAzure", "Tab_AudioBuffer", "Button_TTSProvider_AzureSpeech" })
        {
            var existing = panel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }
        var azure = UnityEngine.Object.Instantiate(templatePage.gameObject, templatePage.parent).transform;
        azure.name = "Page_TtsAzure";
        foreach (Transform child in azure.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
        var playback = UnityEngine.Object.Instantiate(azure.gameObject, azure.parent).transform;
        playback.name = "Page_AudioBuffer";
        var definitions = new[] {
            "ApiKey|Azure Speech Key", "Region|资源区域，例如 eastasia", "VoiceName|音色名称", "Language|语言，例如 zh-CN",
            "Style|语气风格（留空为自然）", "StyleDegree|风格强度（0.01–2）", "RatePercent|语速变化 %（-50–100）",
            "PitchPercent|音调变化 %（-50–50）", "VolumePercent|音量（0–100）"
        };
        for (int i = 0; i < definitions.Length; i++)
        {
            var parts = definitions[i].Split('|');
            AddRow(rowTemplate, azure, "AzureTTS_" + parts[0], parts[1], i);
        }
        AddRow(rowTemplate, playback, "Playback_StartBufferSeconds", "起播缓冲（秒）", 0);
        AddRow(rowTemplate, playback, "Playback_ResumeBufferSeconds", "补缓冲恢复门槛（秒）", 1);
        AddRow(rowTemplate, playback, "Playback_MaxQueuedSeconds", "待写队列上限（5–120 秒）", 2);
        var toggleTemplate = Find("Row_DoubaoTTS_EnableVoiceInstruction");
        var toggleRow = UnityEngine.Object.Instantiate(toggleTemplate.gameObject, playback);
        toggleRow.name = "Row_Playback_LogDiagnostics";
        ((RectTransform)toggleRow.transform).anchoredPosition = new Vector2(0, -114);
        toggleRow.transform.Find("Label").GetComponent<Text>().text = "输出音频性能诊断";
        toggleRow.GetComponentInChildren<Toggle>(true).name = "Toggle_Playback_LogDiagnostics";
        AddHint(rowTemplate, azure, "填写区域和密钥后可试听。点击“选择 Azure 朗读”，再“应用并保存”即可启用。", -420);
        AddHint(rowTemplate, playback, "默认起播 0.12 秒、补缓冲 0.20 秒。网络较差时可适当增加。", -170);
        AddHint(rowTemplate, playback, "公共缓冲设置适用于四种 TTS；超限会停止朗读并保留文字。", -208);
        var preview = AddButton(tabTemplate, azure, "Button_AzurePreview", "试听 Azure", new Vector2(0, -364));
        AddButton(tabTemplate, azure, "Button_AzureStop", "停止试听", new Vector2(132, -364));
        AddButton(tabTemplate, azure, "Button_AzureSelect", "选择 Azure 朗读", new Vector2(264, -364));
        AddButton(tabTemplate, tabTemplate.parent, "Tab_TtsAzure", "Azure TTS", Vector2.zero);
        AddButton(tabTemplate, tabTemplate.parent, "Tab_AudioBuffer", "音频缓冲", Vector2.zero);
        var tabs = tabTemplate.parent.Cast<Transform>().Where(t => t.name.StartsWith("Tab_")).ToArray();
        var width = ((RectTransform)tabTemplate.parent).rect.width;
        float tabWidth = Mathf.Max(78, (width - 48) / 7 - 6);
        for (int i = 0; i < tabs.Length; i++)
        {
            var rect = (RectTransform)tabs[i];
            rect.anchoredPosition = new Vector2(20 + (i % 7) * (tabWidth + 6), -50 - (i / 7) * 34);
            rect.sizeDelta = new Vector2(tabWidth, 30);
        }
        foreach (var page in tabTemplate.parent.Cast<Transform>().Where(t => t.name.StartsWith("Page_")))
        {
            var rect = (RectTransform)page;
            rect.anchoredPosition = new Vector2(0, -40);
            rect.sizeDelta = new Vector2(-40, -192);
        }
        var provider = Find("Button_TTSProvider_Doubao");
        var added = UnityEngine.Object.Instantiate(provider.gameObject, provider.parent);
        added.name = "Button_TTSProvider_AzureSpeech";
        added.GetComponentInChildren<Text>(true).text = "Azure";
        var providers = provider.parent.Cast<Transform>().Where(t => t.name.StartsWith("Button_TTSProvider_")).ToArray();
        for (int i = 0; i < providers.Length; i++)
        {
            var rect = (RectTransform)providers[i]; rect.anchoredPosition = new Vector2(280 + i * 100, 0); rect.sizeDelta = new Vector2(94, 26);
        }
        var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
        const string assetPath = "Assets/MyCharacter/Config/AzureTTSConfig.asset";
        var asset = AssetDatabase.LoadAssetAtPath<AzureTTSConfig>(assetPath);
        if (asset == null) { asset = ScriptableObject.CreateInstance<AzureTTSConfig>(); AssetDatabase.CreateAsset(asset, assetPath); }
        controller.azureTtsConfig = asset;
        controller.streamingPlayer.resumeBufferSeconds = 0.2f;
        controller.streamingPlayer.maxQueuedSeconds = 120;
        azure.gameObject.SetActive(false); playback.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("AZURE_SCENE_SETUP_PASS tabs=" + tabs.Length + " panelWidth=" + width);
    }
    private static void AddRow(Transform template, Transform page, string key, string label, int index)
    {
        var row = UnityEngine.Object.Instantiate(template.gameObject, page);
        row.name = "Row_" + key;
        ((RectTransform)row.transform).anchoredPosition = new Vector2(0, -index * 38);
        row.transform.Find("Label").GetComponent<Text>().text = label;
        var field = row.GetComponentInChildren<InputField>(true);
        field.name = "Input_" + key; field.text = "";
        field.contentType = key.EndsWith("ApiKey") ? InputField.ContentType.Password : InputField.ContentType.Standard;
        field.characterValidation = InputField.CharacterValidation.None;
        field.lineType = InputField.LineType.SingleLine;
        if (field.placeholder is Text placeholder) placeholder.text = "";
    }
    private static void AddHint(Transform template, Transform page, string message, float y)
    {
        var go = UnityEngine.Object.Instantiate(template.Find("Label").gameObject, page);
        go.name = "Hint";
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(0, 34);
        var text = go.GetComponent<Text>(); text.text = message; text.horizontalOverflow = HorizontalWrapMode.Wrap;
    }
    private static GameObject AddButton(Transform template, Transform parent, string name, string label, Vector2 position)
    {
        var go = UnityEngine.Object.Instantiate(template.gameObject, parent); go.name = name;
        var rect = (RectTransform)go.transform; rect.anchoredPosition = position; rect.sizeDelta = new Vector2(120, 30);
        go.GetComponentInChildren<Text>(true).text = label;
        return go;
    }
}
