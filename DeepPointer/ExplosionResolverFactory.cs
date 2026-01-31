using SubnauticaLauncher.Memory;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResolverFactory
    {
        public static IExplosionResolver Get(int yearGroup)
        {
            if (yearGroup <= 2021)
            {
                return new ExplosionResolver2018(
                    // Explosion
                    new DeepPointer("mono.dll", 0x266188, 0x90, 0x648, 0x0, 0x40, 0x118, 0x1A0, 0x18, 0x7C),
                    new DeepPointer("mono.dll", 0x266188, 0x90, 0x648, 0x0, 0x40, 0x118, 0x1A0, 0x18, 0x88),

                    // Position
                    new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C0),
                    new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C4),
                    new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C8)
                );
            }

            // 2023+
            return new ExplosionResolver2023(
                new DeepPointer("UnityPlayer.dll", 0x017FBC48, 0x280, 0x220, 0x4E0, 0x1940, 0xA8, 0xD0, 0x48, 0x60, 0x10, 0x84),
                new DeepPointer("UnityPlayer.dll", 0x017FBC48, 0x280, 0x220, 0x4E0, 0x1940, 0xA8, 0xD0, 0x48, 0x60, 0x10, 0x90),

                new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA58),
                new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA5C),
                new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA60)
            );
        }
    }
}