// A character component that can render the Azure continuous mouth timeline.
// CC characters use AlignedMouthDriver (raw blend shapes); VRM characters use VrmMouthDriver (vowel expressions).
public interface IAlignedMouthTarget
{
    bool HasMapping { get; }
    MouthTuning Tuning { get; }
    void Apply(float[] channels);
    void Release(bool immediate = false);
}
