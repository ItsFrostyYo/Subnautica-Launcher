using SubnauticaLauncher.Memory;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResolverFactory
    {
        public static IExplosionResolver Get(int yearGroup)
        {
            var legacyCountdown = new DeepPointer("mono.dll", 0x266188, 0x90, 0x648, 0x0, 0x40, 0x118, 0x1A0, 0x18, 0x7C);
            var legacyWarning = new DeepPointer("mono.dll", 0x266188, 0x90, 0x648, 0x0, 0x40, 0x118, 0x1A0, 0x18, 0x88);
            var legacyX = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C0);
            var legacyY = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C4);
            var legacyZ = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C8);

            var modernCountdown = new DeepPointer("UnityPlayer.dll", 0x017FBC48, 0x280, 0x220, 0x4E0, 0x1940, 0xA8, 0xD0, 0x48, 0x60, 0x10, 0x84);
            var modernWarning = new DeepPointer("UnityPlayer.dll", 0x017FBC48, 0x280, 0x220, 0x4E0, 0x1940, 0xA8, 0xD0, 0x48, 0x60, 0x10, 0x90);
            var modernX = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA58);
            var modernY = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA5C);
            var modernZ = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA60);

            if (yearGroup <= 2021)
            {
                var legacyFallback = new ExplosionResolver(
                    legacyCountdown,
                    legacyWarning,
                    legacyX,
                    legacyY,
                    legacyZ);

                return new DynamicMonoExplosionResolver(
                    legacyFallback,
                    legacyX,
                    legacyY,
                    legacyZ);
            }

            // Static 2023 pointers stay as fallback.
            var modernFallback = new ExplosionResolver(
                modernCountdown,
                modernWarning,
                modernX,
                modernY,
                modernZ);

            return new DynamicMonoExplosionResolver(
                modernFallback,
                modernX,
                modernY,
                modernZ);
        }
    }
}
