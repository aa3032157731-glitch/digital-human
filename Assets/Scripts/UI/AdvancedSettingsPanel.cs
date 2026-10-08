using UnityEngine;
using UnityEngine.UI;

// Scene-compatible entry point. The new hub owns draft state and all runtime UI.
public sealed class AdvancedSettingsPanel : MonoBehaviour
{
    public VoiceChatController controller;
    public GameObject panelRoot;
    public RectTransform interfaceRoot;
    public Button settingsButton;
    public Button switchModelButton;
    public Text statusText;
    public CharacterModelSwitcher modelSwitcher;
    public bool startVisible;
    public string defaultPageName = "Page_ApiKeys";
    private AssistantSettingsHub hub;
    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (settingsButton != null) settingsButton.gameObject.SetActive(false);
    }
    private void Start() { EnsureHub(); if (startVisible) hub.Open(); }
    private void EnsureHub()
    {
        if (hub != null) return;
        if (controller == null) controller = FindObjectOfType<VoiceChatController>();
        hub = gameObject.AddComponent<AssistantSettingsHub>();
        hub.Build(this);
    }
    public void TogglePanel() { EnsureHub(); if (hub.IsOpen) hub.RequestClose(); else hub.Open(); }
    public void OpenPanel() { EnsureHub(); hub.Open(); }
    public void ClosePanel() { EnsureHub(); hub.RequestClose(); }
    public void ShowPageByName(string pageName) { EnsureHub(); hub.NavigateLegacy(pageName); }
    public void SaveAndApply() { EnsureHub(); hub.Save(); }
    public void ReloadSaved() { EnsureHub(); hub.Reload(); }
    public void RebuildConfigFileFromCurrentValues() { EnsureHub(); hub.Reload(); }
    public void LoadFromController() { EnsureHub(); if (!hub.IsOpen) hub.Open(); }
    public void ToggleModel()
    {
        if (modelSwitcher == null) modelSwitcher = FindObjectOfType<CharacterModelSwitcher>(true);
        modelSwitcher?.ToggleModel();
    }
}
