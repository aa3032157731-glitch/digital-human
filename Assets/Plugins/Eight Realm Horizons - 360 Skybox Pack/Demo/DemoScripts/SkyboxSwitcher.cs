using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EightRealmHorizons
{
    public class SkyboxSwitcher : MonoBehaviour
    {
        // Switches through skyboxes when the button is clicked and blends between them.
        public Material[] skyboxes;
        public Button switchButton;
        public Text skyboxNameText;

        [Header("Blend")]
        public float blendDuration = 1.5f;
        public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        int currentIndex = 0;
        bool isBlending = false;
        Material runtimeSkyboxMaterial;

        readonly int cubeAID = Shader.PropertyToID("_CubeA");
        readonly int cubeBID = Shader.PropertyToID("_CubeB");
        readonly int rotationAID = Shader.PropertyToID("_RotationA");
        readonly int rotationBID = Shader.PropertyToID("_RotationB");
        readonly int blendID = Shader.PropertyToID("_Blend");
        readonly int tintID = Shader.PropertyToID("_Tint");
        readonly int exposureID = Shader.PropertyToID("_Exposure");

        void Start()
        {
            if (switchButton != null)
            {
                switchButton.onClick.AddListener(SwitchSkybox);
            }

            if (!CreateRuntimeSkyboxMaterial())
            {
                ApplySkyboxImmediately();
            }
        }

        void OnDestroy()
        {
            if (switchButton != null)
            {
                switchButton.onClick.RemoveListener(SwitchSkybox);
            }

            if (runtimeSkyboxMaterial != null)
            {
                Destroy(runtimeSkyboxMaterial);
            }
        }

        public void SwitchSkybox()
        {
            if (skyboxes == null || skyboxes.Length == 0 || isBlending)
            {
                return;
            }

            int nextIndex = GetNextIndex();

            if (runtimeSkyboxMaterial == null && !CreateRuntimeSkyboxMaterial())
            {
                currentIndex = nextIndex;
                ApplySkyboxImmediately();
                return;
            }

            StartCoroutine(BlendToSkybox(nextIndex));
        }

        bool CreateRuntimeSkyboxMaterial()
        {
            if (skyboxes == null || skyboxes.Length == 0)
            {
                Debug.LogError("Please assign at least one skybox material.");
                return false;
            }

            if (GetSkyboxTexture(skyboxes[currentIndex]) == null)
            {
                Debug.LogError("The current skybox material does not have a cubemap texture.");
                return false;
            }

            Shader shader = Shader.Find("Custom/SkyboxCubemapBlend");
            if (shader == null)
            {
                Debug.LogError("Could not find shader: Custom/SkyboxCubemapBlend");
                return false;
            }

            runtimeSkyboxMaterial = new Material(shader)
            {
                name = "Runtime Skybox Blend"
            };

            RenderSettings.skybox = runtimeSkyboxMaterial;
            if (!ApplyBlendMaterialState(currentIndex, GetNextIndex(), 0f))
            {
                Destroy(runtimeSkyboxMaterial);
                runtimeSkyboxMaterial = null;
                return false;
            }

            UpdateSkyboxName();
            DynamicGI.UpdateEnvironment();

            return true;
        }

        IEnumerator BlendToSkybox(int nextIndex)
        {
            isBlending = true;

            bool restoreButtonInteractable = switchButton != null && switchButton.interactable;
            if (restoreButtonInteractable)
            {
                switchButton.interactable = false;
            }

            float duration = Mathf.Max(0.01f, blendDuration);
            float timer = 0f;

            if (!ApplyBlendMaterialState(currentIndex, nextIndex, 0f))
            {
                currentIndex = nextIndex;
                ApplySkyboxImmediately();
                RestoreButtonInteractable(restoreButtonInteractable);
                isBlending = false;
                yield break;
            }

            Material fromSkybox = skyboxes[currentIndex];
            Material toSkybox = skyboxes[nextIndex];
            Color fromTint = GetSkyboxTint(fromSkybox);
            Color toTint = GetSkyboxTint(toSkybox);
            float fromExposure = GetSkyboxFloat(fromSkybox, "_Exposure", 1f);
            float toExposure = GetSkyboxFloat(toSkybox, "_Exposure", 1f);

            while (timer < duration)
            {
                timer += Time.deltaTime;

                float t = Mathf.Clamp01(timer / duration);
                float curvedT = blendCurve != null ? blendCurve.Evaluate(t) : t;

                runtimeSkyboxMaterial.SetFloat(blendID, curvedT);
                runtimeSkyboxMaterial.SetColor(tintID, Color.Lerp(fromTint, toTint, curvedT));
                runtimeSkyboxMaterial.SetFloat(exposureID, Mathf.Lerp(fromExposure, toExposure, curvedT));

                yield return null;
            }

            currentIndex = nextIndex;
            ApplyBlendMaterialState(currentIndex, GetNextIndex(), 0f);
            UpdateSkyboxName();
            DynamicGI.UpdateEnvironment();

            RestoreButtonInteractable(restoreButtonInteractable);
            isBlending = false;
        }

        void RestoreButtonInteractable(bool restoreButtonInteractable)
        {
            if (restoreButtonInteractable && switchButton != null)
            {
                switchButton.interactable = true;
            }
        }

        bool ApplyBlendMaterialState(int fromIndex, int toIndex, float blend)
        {
            if (runtimeSkyboxMaterial == null)
            {
                return false;
            }

            Material fromSkybox = skyboxes[fromIndex];
            Material toSkybox = skyboxes[toIndex];
            Texture fromTexture = GetSkyboxTexture(fromSkybox);
            Texture toTexture = GetSkyboxTexture(toSkybox);

            if (fromTexture == null || toTexture == null)
            {
                Debug.LogError("Skybox material is missing a cubemap texture.");
                return false;
            }

            runtimeSkyboxMaterial.SetTexture(cubeAID, fromTexture);
            runtimeSkyboxMaterial.SetTexture(cubeBID, toTexture);
            runtimeSkyboxMaterial.SetFloat(rotationAID, GetSkyboxFloat(fromSkybox, "_Rotation", 0f));
            runtimeSkyboxMaterial.SetFloat(rotationBID, GetSkyboxFloat(toSkybox, "_Rotation", 0f));
            runtimeSkyboxMaterial.SetFloat(blendID, blend);
            runtimeSkyboxMaterial.SetColor(tintID, GetSkyboxTint(fromSkybox));
            runtimeSkyboxMaterial.SetFloat(exposureID, GetSkyboxFloat(fromSkybox, "_Exposure", 1f));

            return true;
        }

        void ApplySkyboxImmediately()
        {
            if (skyboxes == null || skyboxes.Length == 0)
            {
                return;
            }

            RenderSettings.skybox = skyboxes[currentIndex];
            UpdateSkyboxName();
            DynamicGI.UpdateEnvironment();
        }

        void UpdateSkyboxName()
        {
            if (skyboxNameText != null)
            {
                skyboxNameText.text = skyboxes[currentIndex].name;
            }
        }

        int GetNextIndex()
        {
            return (currentIndex + 1) % skyboxes.Length;
        }

        static Texture GetSkyboxTexture(Material skybox)
        {
            if (skybox == null)
            {
                return null;
            }

            if (skybox.HasProperty("_Tex") && skybox.GetTexture("_Tex") != null)
            {
                return skybox.GetTexture("_Tex");
            }

            if (skybox.HasProperty("_MainTex") && skybox.GetTexture("_MainTex") != null)
            {
                return skybox.GetTexture("_MainTex");
            }

            if (skybox.HasProperty("_BaseMap") && skybox.GetTexture("_BaseMap") != null)
            {
                return skybox.GetTexture("_BaseMap");
            }

            return null;
        }

        static float GetSkyboxFloat(Material skybox, string propertyName, float fallback)
        {
            return skybox != null && skybox.HasProperty(propertyName) ? skybox.GetFloat(propertyName) : fallback;
        }

        static Color GetSkyboxTint(Material skybox)
        {
            if (skybox == null || !skybox.HasProperty("_Tint"))
            {
                return Color.white;
            }

            Color tint = skybox.GetColor("_Tint");
            if (Mathf.Approximately(tint.r, 0.5f) &&
                Mathf.Approximately(tint.g, 0.5f) &&
                Mathf.Approximately(tint.b, 0.5f))
            {
                return Color.white;
            }

            return new Color(tint.r, tint.g, tint.b, 1f);
        }
    }
}
