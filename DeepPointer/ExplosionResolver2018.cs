using System.Diagnostics;
using SubnauticaLauncher.Memory;

namespace SubnauticaLauncher.Explosion
{
    public sealed class ExplosionResolver2018 : IExplosionResolver
    {
        private readonly DeepPointer _countdown;
        private readonly DeepPointer _warning;
        private readonly DeepPointer _x;
        private readonly DeepPointer _y;
        private readonly DeepPointer _z;

        public ExplosionResolver2018(
            DeepPointer countdown,
            DeepPointer warning,
            DeepPointer x,
            DeepPointer y,
            DeepPointer z)
        {
            _countdown = countdown;
            _warning = warning;
            _x = x;
            _y = y;
            _z = z;
        }

        public bool TryRead(Process proc, out ExplosionSnapshot s)
        {
            s = new ExplosionSnapshot();

            float x = 0f, y = 0f, z = 0f;
            _x.TryReadFloat(proc, out x);
            _y.TryReadFloat(proc, out y);
            _z.TryReadFloat(proc, out z);

            s.PosX = x;
            s.PosY = y;
            s.PosZ = z;

            float c = 0f;
            float w = 0f;

            bool hasExplosion =
                _countdown.TryReadFloat(proc, out c) &&
                _warning.TryReadFloat(proc, out w) &&
                c > 0f && w > 0f;

            s.ExplosionTime = hasExplosion ? (c - w) : -1f;
            return true;
        }
    }
}