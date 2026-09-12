using System.Collections;
using UnityEngine;

public class SkyboxCubemapCycle : MonoBehaviour
{
    [Header("放入 9 个 Cubemap 贴图")]
    public Cubemap[] cubemaps;

    [Header("旋转速度，单位：度/秒")]
    public float rotationSpeed = 10f;

    [Header("旋转多少度后切换下一个天空盒")]
    public float switchAfterDegrees = 360f;

    [Header("交叉淡入淡出时间")]
    public float blendDuration = 1.5f;

    [Header("亮度")]
    public float exposure = 1f;

    private Material runtimeSkyboxMaterial;

    private int currentIndex = 0;
    private float currentRotation = 0f;
    private bool isBlending = false;

    private readonly int cubeAID = Shader.PropertyToID("_CubeA");
    private readonly int cubeBID = Shader.PropertyToID("_CubeB");
    private readonly int rotationAID = Shader.PropertyToID("_RotationA");
    private readonly int rotationBID = Shader.PropertyToID("_RotationB");
    private readonly int blendID = Shader.PropertyToID("_Blend");
    private readonly int exposureID = Shader.PropertyToID("_Exposure");

    void Start()
    {
        if (cubemaps == null || cubemaps.Length < 2)
        {
            Debug.LogError("请至少放入 2 个 Cubemap。");
            enabled = false;
            return;
        }

        Shader shader = Shader.Find("Custom/SkyboxCubemapBlend");

        if (shader == null)
        {
            Debug.LogError("找不到 Shader：Custom/SkyboxCubemapBlend");
            enabled = false;
            return;
        }

        runtimeSkyboxMaterial = new Material(shader);
        RenderSettings.skybox = runtimeSkyboxMaterial;

        ApplyCurrentAndNextCubemap();

        runtimeSkyboxMaterial.SetFloat(rotationAID, 0f);
        runtimeSkyboxMaterial.SetFloat(rotationBID, 0f);
        runtimeSkyboxMaterial.SetFloat(blendID, 0f);
        runtimeSkyboxMaterial.SetFloat(exposureID, exposure);

        DynamicGI.UpdateEnvironment();
    }

    void Update()
    {
        if (runtimeSkyboxMaterial == null) return;

        // 当前天空盒一直旋转，即使正在渐变也不停
        currentRotation += rotationSpeed * Time.deltaTime;
        runtimeSkyboxMaterial.SetFloat(rotationAID, currentRotation);
        runtimeSkyboxMaterial.SetFloat(exposureID, exposure);

        // 只有没在渐变时，才允许触发下一次切换
        if (!isBlending && currentRotation >= switchAfterDegrees)
        {
            StartCoroutine(BlendToNextCubemap());
        }
    }

    void ApplyCurrentAndNextCubemap()
    {
        int nextIndex = GetNextIndex();

        runtimeSkyboxMaterial.SetTexture(cubeAID, cubemaps[currentIndex]);
        runtimeSkyboxMaterial.SetTexture(cubeBID, cubemaps[nextIndex]);
    }

    int GetNextIndex()
    {
        return (currentIndex + 1) % cubemaps.Length;
    }

    IEnumerator BlendToNextCubemap()
    {
        isBlending = true;

        int nextIndex = GetNextIndex();

        runtimeSkyboxMaterial.SetTexture(cubeAID, cubemaps[currentIndex]);
        runtimeSkyboxMaterial.SetTexture(cubeBID, cubemaps[nextIndex]);

        float nextRotation = 0f;

        runtimeSkyboxMaterial.SetFloat(blendID, 0f);
        runtimeSkyboxMaterial.SetFloat(rotationBID, nextRotation);

        float timer = 0f;

        while (timer < blendDuration)
        {
            float delta = Time.deltaTime;
            timer += delta;

            float t = Mathf.Clamp01(timer / blendDuration);

            // 下一个天空盒也在渐变过程中旋转
            nextRotation += rotationSpeed * delta;

            runtimeSkyboxMaterial.SetFloat(rotationBID, nextRotation);
            runtimeSkyboxMaterial.SetFloat(blendID, t);
            runtimeSkyboxMaterial.SetFloat(exposureID, exposure);

            yield return null;
        }

        currentIndex = nextIndex;

        // 渐变结束后，让新天空盒接着它刚才的旋转角度继续转
        currentRotation = nextRotation;

        ApplyCurrentAndNextCubemap();

        runtimeSkyboxMaterial.SetFloat(rotationAID, currentRotation);
        runtimeSkyboxMaterial.SetFloat(rotationBID, 0f);
        runtimeSkyboxMaterial.SetFloat(blendID, 0f);

        DynamicGI.UpdateEnvironment();

        isBlending = false;
    }
}