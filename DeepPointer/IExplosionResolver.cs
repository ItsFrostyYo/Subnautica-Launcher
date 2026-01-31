using System.Diagnostics;

namespace SubnauticaLauncher.Explosion
{
    public interface IExplosionResolver
    {
        bool TryRead(Process proc, out ExplosionSnapshot snapshot);
    }
}