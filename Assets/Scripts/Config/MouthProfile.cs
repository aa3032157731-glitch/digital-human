using System;
using UnityEngine;

[Serializable]
public sealed class MouthShapeBinding
{
    public int channel;
    public string rendererPath;
    public string shape;
    public float gain = 1;
    public float maxWeight = 100;
    public bool approximate;
    public float neutralWeight;
}

[Serializable]
public sealed class MouthTuning
{
    public float mouthIntensity = .85f;
    public float jawAmplitude = .7f;
    public float lipSyncOffsetMs;
    public MouthTuning Copy() => (MouthTuning)MemberwiseClone();
    public void Sanitize()
    {
        mouthIntensity = Clamp(mouthIntensity, 0, 1.5f, .85f);
        jawAmplitude = Clamp(jawAmplitude, 0, 1.2f, .7f);
        lipSyncOffsetMs = Clamp(lipSyncOffsetMs, -200, 200, 0);
    }
    private static float Clamp(float n, float lo, float hi, float fallback) => float.IsNaN(n) || float.IsInfinity(n) ? fallback : Mathf.Clamp(n, lo, hi);
}

[CreateAssetMenu(menuName = "Voice Chat/Mouth Profile")]
public sealed class MouthProfile : ScriptableObject
{
    public string modelName;
    public MouthShapeBinding[] bindings = Array.Empty<MouthShapeBinding>();
    public MouthBoneBinding[] boneBindings = Array.Empty<MouthBoneBinding>();
    public string jawBonePath;
    public Vector3 jawOpenEuler = new Vector3(0, 0, -12);
    public bool useJawBone;
    public Quaternion jawNeutralRotation = Quaternion.identity;
    [TextArea] public string coverage;
}

[Serializable]
public sealed class MouthBoneBinding
{
    public int channel;
    public string bonePath;
    public Vector3 neutralPosition;
    public Quaternion neutralRotation;
    public Vector3 translation;
    public Quaternion rotation;
    public float gain = 1;
}
