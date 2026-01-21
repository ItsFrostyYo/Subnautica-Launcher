using System.Collections.Generic;

namespace SubnauticaLauncher
{
    public static class SteamManifestRegistry
    {
        // BuildID → (WindowsManifest, VCRedistManifest)
        public static readonly Dictionary<long, (long win, long vc)> ManifestMap =
            new Dictionary<long, (long win, long vc)>
            {
                { 20241558, (0L, 0L) }, // Latest Oct 2025
                { 7819049,  (0L, 0L) }, // Legacy Dec 2021
                { 3169139,  (0L, 0L) }, // Speedrun Sep 2018
                { 18810395, (0L, 0L) }, // Aug 2025
                { 10676206, (0L, 0L) }, // Mar 2023
                { 10133851, (0L, 0L) }, // Dec 2022
                { 4556365,  (0L, 0L) }, // Jan 2020
                { 4356520,  (0L, 0L) }, // Nov 2019
                { 2458098,  (0L, 0L) }, // Jan 2018
                { 2359337,  (0L, 0L) }, // EA Dec 2017
                { 2116109,  (0L, 0L) }, // EA Sep 2017
                { 1662587,  (0L, 0L) }, // EA Mar 2017
            };
    }
}