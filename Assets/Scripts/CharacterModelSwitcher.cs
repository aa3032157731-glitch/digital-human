using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public sealed class CharacterModelSwitcher : MonoBehaviour
{
    [Header("Models")]
    public GameObject[] modelRoots;
    public string[] modelDisplayNames;
    public int currentModelIndex;

    [Header("Linked Systems")]
    public VoiceChatController voiceChatController;
    public AudioSource ttsAudioSource;
    public CharacterCameraPositioner[] characterPositioners;

    [Header("Behavior")]
    public bool applyOnStart = true;
    public bool deactivateInactiveModels = true;
    public bool syncSalsaAudioSources = true;

    [Header("UI")]
    public Text switchButtonLabel;
    public string switchButtonTextFormat = "切换到 {0}";

    private const BindingFlags InstanceBindingFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        ResolveMissingReferences();
        currentModelIndex = ClampModelIndex(currentModelIndex);
        UpdateSwitchButtonLabel();
    }

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyCurrentModel();
        }
    }

    public void ToggleModel()
    {
        if (modelRoots == null || modelRoots.Length == 0)
        {
            return;
        }

        SetModel((ClampModelIndex(currentModelIndex) + 1) % modelRoots.Length);
    }

    public void SetModel(int index)
    {
        if (modelRoots == null || modelRoots.Length == 0)
        {
            return;
        }

        currentModelIndex = ClampModelIndex(index);
        ApplyCurrentModel();
    }

    public GameObject GetCurrentModelRoot()
    {
        if (modelRoots == null || modelRoots.Length == 0)
        {
            return null;
        }

        return modelRoots[ClampModelIndex(currentModelIndex)];
    }

    public string GetCurrentModelName()
    {
        return GetModelDisplayName(ClampModelIndex(currentModelIndex));
    }

    public void RefreshButtonLabel()
    {
        UpdateSwitchButtonLabel();
    }

    private void ApplyCurrentModel()
    {
        ResolveMissingReferences();
        currentModelIndex = ClampModelIndex(currentModelIndex);

        var activeRoot = GetCurrentModelRoot();

        if (syncSalsaAudioSources)
        {
            SyncAllSalsaAudioSources();
        }

        if (voiceChatController != null)
        {
            voiceChatController.SetTalkAnimator(null);
        }

        if (voiceChatController != null)
        {
            UpdateModelVisibility();
            var activeAnimator = activeRoot != null ? activeRoot.GetComponentInChildren<Animator>() : null;
            PrepareAnimator(activeAnimator);
            voiceChatController.SetTalkAnimator(activeAnimator);
        }
        else
        {
            UpdateModelVisibility();
        }

        UpdateCharacterPositioners(activeRoot);
        UpdateSwitchButtonLabel();
    }

    private static void PrepareAnimator(Animator animator)
    {
        if (animator == null
            || !animator.isActiveAndEnabled
            || !animator.gameObject.activeInHierarchy
            || animator.runtimeAnimatorController == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
    }

    private void ResolveMissingReferences()
    {
        if (voiceChatController == null)
        {
            voiceChatController = FindObjectOfType<VoiceChatController>(true);
        }

        if (ttsAudioSource == null && voiceChatController != null)
        {
            ttsAudioSource = voiceChatController.ttsAudioSource;
        }

        if ((characterPositioners == null || characterPositioners.Length == 0))
        {
            characterPositioners = FindObjectsOfType<CharacterCameraPositioner>(true);
        }
    }

    private void UpdateModelVisibility()
    {
        if (!deactivateInactiveModels || modelRoots == null)
        {
            return;
        }

        for (var i = 0; i < modelRoots.Length; i++)
        {
            var root = modelRoots[i];
            if (root != null)
            {
                root.SetActive(i == currentModelIndex);
            }
        }
    }

    private void UpdateCharacterPositioners(GameObject activeRoot)
    {
        if (activeRoot == null || characterPositioners == null)
        {
            return;
        }

        foreach (var positioner in characterPositioners)
        {
            if (positioner == null)
            {
                continue;
            }

            positioner.characterRoot = activeRoot.transform;
            positioner.fallbackCharacterName = activeRoot.name;
        }
    }

    private void SyncAllSalsaAudioSources()
    {
        if (ttsAudioSource == null || modelRoots == null)
        {
            return;
        }

        foreach (var root in modelRoots)
        {
            SyncSalsaAudioSources(root);
        }
    }

    private void SyncSalsaAudioSources(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            var field = FindAudioSourceField(behaviour.GetType());
            if (field != null && field.FieldType.IsAssignableFrom(typeof(AudioSource)))
            {
                field.SetValue(behaviour, ttsAudioSource);
            }
        }
    }

    private static FieldInfo FindAudioSourceField(System.Type type)
    {
        while (type != null)
        {
            var field = type.GetField("audioSrc", InstanceBindingFlags);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }

    private int ClampModelIndex(int index)
    {
        if (modelRoots == null || modelRoots.Length == 0)
        {
            return 0;
        }

        if (index < 0)
        {
            return 0;
        }

        if (index >= modelRoots.Length)
        {
            return modelRoots.Length - 1;
        }

        return index;
    }

    private string GetModelDisplayName(int index)
    {
        if (modelDisplayNames != null
            && index >= 0
            && index < modelDisplayNames.Length
            && !string.IsNullOrWhiteSpace(modelDisplayNames[index]))
        {
            return modelDisplayNames[index];
        }

        if (modelRoots != null && index >= 0 && index < modelRoots.Length && modelRoots[index] != null)
        {
            return modelRoots[index].name;
        }

        return "Model";
    }

    private void UpdateSwitchButtonLabel()
    {
        if (switchButtonLabel == null)
        {
            return;
        }

        if (modelRoots == null || modelRoots.Length == 0)
        {
            switchButtonLabel.text = "切换模型";
            return;
        }

        var nextIndex = (ClampModelIndex(currentModelIndex) + 1) % modelRoots.Length;
        switchButtonLabel.text = string.Format(switchButtonTextFormat, GetModelDisplayName(nextIndex));
    }
}
