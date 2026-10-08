using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Explicitly last: protect owned mouth channels from Animator and SALSA queue writes.
[DefaultExecutionOrder(30000)]
public sealed class AlignedMouthDriver : MonoBehaviour
{
    public MouthProfile profile;
    public MouthTuning tuning = new MouthTuning();
    private sealed class Target { public SkinnedMeshRenderer renderer; public int index; public float initial, value; }
    private sealed class Binding { public MouthShapeBinding config; public Target target; }
    private readonly List<Target> targets = new List<Target>();
    private readonly List<Binding> bindings = new List<Binding>();
    private sealed class BoneTarget { public Transform bone; public Vector3 neutralPosition, lastPosition; public Quaternion neutralRotation, lastRotation; public readonly List<MouthBoneBinding> maps = new List<MouthBoneBinding>(); }
    private readonly List<BoneTarget> boneTargets = new List<BoneTarget>();
    private readonly List<Behaviour> salsa = new List<Behaviour>();
    private readonly Dictionary<Behaviour, bool> salsaStates = new Dictionary<Behaviour, bool>();
    private readonly List<Tuple<object, FieldInfo, bool>> mutedExpressions = new List<Tuple<object, FieldInfo, bool>>();
    private readonly float[] weights = new float[55];
    private Transform jaw;
    private Quaternion jawRest;
    private bool bound, owned, release;
    private float jawValue;
    private float releaseBlend = 1;
    public bool HasMapping { get { Bind(); return bindings.Count > 0; } }

    private void Bind()
    {
        if (bound || profile == null) return;
        bound = true;
        foreach (var map in profile.bindings)
        {
            var t = string.IsNullOrEmpty(map.rendererPath) ? transform : transform.Find(map.rendererPath);
            var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            int index = smr != null && smr.sharedMesh != null ? smr.sharedMesh.GetBlendShapeIndex(map.shape) : -1;
            if (index < 0 || !MouthChannels.IsMouth(map.channel)) continue;
            var target = targets.Find(x => x.renderer == smr && x.index == index);
            if (target == null) { target = new Target { renderer = smr, index = index, initial = map.neutralWeight }; targets.Add(target); }
            bindings.Add(new Binding { config = map, target = target });
        }
        if (!string.IsNullOrEmpty(profile.jawBonePath)) jaw = transform.Find(profile.jawBonePath);
        if (jaw != null) jawRest = profile.jawNeutralRotation;
        foreach (var map in profile.boneBindings)
        {
            var bone = transform.Find(map.bonePath); if (bone == null) continue;
            var target = boneTargets.Find(t => t.bone == bone);
            if (target == null)
            {
                target = new BoneTarget { bone = bone, neutralPosition = map.neutralPosition, neutralRotation = map.neutralRotation,
                    lastPosition = map.neutralPosition, lastRotation = map.neutralRotation };
                boneTargets.Add(target);
            }
            target.maps.Add(map);
        }
        foreach (var b in GetComponentsInChildren<MonoBehaviour>(true))
            if (b != null && b.GetType().FullName == "CrazyMinnow.SALSA.Salsa") salsa.Add(b);
    }
    public void Apply(float[] channels)
    {
        Bind();
        if (!owned)
        {
            owned = true;
            foreach (var b in salsa) { salsaStates[b] = b.enabled; b.enabled = false; }
            MuteMouthExpressions();
            foreach (var b in GetComponentsInChildren<MonoBehaviour>(true))
                if (b != null && b.GetType().FullName == "Reallusion.Runtime.BoneDriver")
                    foreach (var name in new[] { "driveJaw", "driveTongue", "driveTeeth" })
                    {
                        var field = b.GetType().GetField(name);
                        if (field != null) { mutedExpressions.Add(Tuple.Create((object)b, field, (bool)field.GetValue(b))); field.SetValue(b, false); }
                    }
        }
        release = false; releaseBlend = 1;
        Array.Copy(channels, weights, 55);
    }
    public void Release(bool immediate = false)
    {
        if (!owned) return;
        Array.Clear(weights, 0, weights.Length); release = true;
        if (immediate) Restore();
    }
    private void LateUpdate() { Render(Time.unscaledDeltaTime); }
    public void Render(float delta)
    {
        if (!owned) return;
        tuning.Sanitize();
        foreach (var target in targets) target.value = 0;
        foreach (var b in bindings)
        {
            float v = weights[b.config.channel];
            if (b.config.channel == 17) v *= tuning.jawAmplitude;
            if (b.config.channel == 51) v *= .45f;
            b.target.value += Mathf.Clamp(v * b.config.gain * tuning.mouthIntensity * 100, 0, b.config.maxWeight);
        }
        float maxRemaining = 0;
        foreach (var t in targets)
        {
            float value = Mathf.Clamp(t.value, 0, 100);
            // Continuous data is already coarticulated; do not low-pass it and introduce latency.
            if (release) value = Mathf.MoveTowards(t.renderer.GetBlendShapeWeight(t.index), t.initial, Mathf.Max(0, delta) * 1000);
            t.renderer.SetBlendShapeWeight(t.index, value);
            maxRemaining = Mathf.Max(maxRemaining, Mathf.Abs(value - t.initial));
        }
        if (release) releaseBlend = Mathf.Max(0, releaseBlend - delta * 10);
        foreach (var target in boneTargets)
        {
            if (release)
            {
                target.bone.localPosition = Vector3.Lerp(target.neutralPosition, target.lastPosition, releaseBlend);
                target.bone.localRotation = Quaternion.Slerp(target.neutralRotation, target.lastRotation, releaseBlend);
                maxRemaining = Mathf.Max(maxRemaining, releaseBlend);
            }
            else
            {
                var position = target.neutralPosition; var rotation = target.neutralRotation;
                foreach (var map in target.maps)
                {
                    float weight = Mathf.Clamp01(weights[map.channel] * map.gain * tuning.mouthIntensity * (map.channel == 17 ? tuning.jawAmplitude : 1));
                    position += map.translation * weight;
                    rotation *= Quaternion.Lerp(Quaternion.identity, map.rotation, weight);
                }
                target.bone.localPosition = target.lastPosition = position;
                target.bone.localRotation = target.lastRotation = rotation;
            }
        }
        if (jaw != null && boneTargets.Count == 0)
        {
            jawValue = release ? Mathf.MoveTowards(jawValue, 0, delta * 10) : profile.useJawBone ? weights[17] * tuning.jawAmplitude : 0;
            jaw.localRotation = jawRest * Quaternion.Euler(profile.jawOpenEuler * jawValue);
            maxRemaining = Mathf.Max(maxRemaining, jawValue * 100);
        }
        if (release && maxRemaining < .01f) Restore();
    }
    private void Restore()
    {
        foreach (var target in targets) if (target.renderer != null) target.renderer.SetBlendShapeWeight(target.index, target.initial);
        if (jaw != null) jaw.localRotation = jawRest;
        foreach (var target in boneTargets) if (target.bone != null) { target.bone.localPosition = target.neutralPosition; target.bone.localRotation = target.neutralRotation; }
        foreach (var item in mutedExpressions) item.Item2.SetValue(item.Item1, item.Item3);
        mutedExpressions.Clear();
        foreach (var pair in salsaStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        salsaStates.Clear(); owned = release = false; jawValue = 0;
    }
    private void OnDisable() { Release(true); }

    // SALSA stores expression components as ordinary serialized objects. Muting only
    // matching controllers leaves eye/brow emotes and Eyes running.
    private void MuteMouthExpressions()
    {
        foreach (var b in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (b == null || b.GetType().FullName != "CrazyMinnow.SALSA.Emoter") continue;
            var field = b.GetType().GetField("emotes");
            if (!(field?.GetValue(b) is IEnumerable emotes)) continue;
            foreach (var emote in emotes)
            {
                var exp = emote?.GetType().GetField("expData")?.GetValue(emote);
                if (exp == null) continue;
                var components = exp.GetType().GetField("components")?.GetValue(exp) as IList;
                var controllers = exp.GetType().GetField("controllerVars")?.GetValue(exp) as IList;
                if (components == null || controllers == null) continue;
                for (int i = 0; i < Math.Min(components.Count, controllers.Count); ++i)
                {
                    var control = controllers[i]; var type = control.GetType();
                    var smr = type.GetField("smr")?.GetValue(control) as SkinnedMeshRenderer;
                    var index = type.GetField("blendIndex")?.GetValue(control);
                    var bone = type.GetField("bone")?.GetValue(control) as Transform;
                    if (!(smr != null && index is int n && targets.Exists(t => t.renderer == smr && t.index == n)) && !(bone != null && (bone == jaw || boneTargets.Exists(t => t.bone == bone)))) continue;
                    var component = components[i]; var enabled = component.GetType().GetField("enabled");
                    if (enabled?.FieldType == typeof(bool))
                    { mutedExpressions.Add(Tuple.Create(component, enabled, (bool)enabled.GetValue(component))); enabled.SetValue(component, false); }
                }
            }
        }
    }
}
