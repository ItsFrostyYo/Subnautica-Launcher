using SubnauticaLauncher.Enums;

public static class ExplosionPresetRanges
{
    public static (float min, float max) Get(ExplosionResetPreset preset)
    {
        return preset switch
        {
            ExplosionResetPreset.Min46_To_4630 => (2760f, 2790f),
            ExplosionResetPreset.Min46_To_47 => (2760f, 2820f),
            ExplosionResetPreset.Min46_To_48 => (2760f, 2880f),
            ExplosionResetPreset.Min46_To_50 => (2760f, 3000f),
            ExplosionResetPreset.Under1Hour => (0f, 3600f),
            ExplosionResetPreset.Over1Hour => (3600f, float.MaxValue),
            _ => (0f, float.MaxValue)
        };
    }
}