using System.Collections.Generic;
using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public static class GameStateDetectorRegistry
    {
        private static readonly Dictionary<int, GameStateProfile> Profiles = new()
        {
            // ================= 2014–2017 =================
            [2017] = new GameStateProfile
            {
                MainMenuPixel = new Point(788, 793),
                MainMenuColor = Color.FromArgb(255, 43, 169, 197),

                InGamePixel = new Point(1027, 109),
                InGameColor = Color.FromArgb(255, 64, 179, 157),

                BlackPixel = new Point(960, 540),
                ColorTolerance = 8
            },
         
            // ================= 2018–2021 =================
            [2018] = new GameStateProfile
            {
                MainMenuPixel = new Point(764, 793),
                MainMenuColor = Color.FromArgb(255, 28, 83, 157),

                InGamePixel = new Point(976, 124),
                InGameColor = Color.FromArgb(255, 233, 242, 95),

                BlackPixel = new Point(960, 540),
                ColorTolerance = 8
            },

            // ================= 2022–2025 =================
            [2022] = new GameStateProfile
            {
                MainMenuPixel = new Point(634, 729),
                MainMenuColor = Color.FromArgb(255, 27, 81, 154),

                InGamePixel = new Point(1030, 92),
                InGameColor = Color.FromArgb(255, 36, 176, 160),
            
                BlackPixel = new Point(960, 540),
                ColorTolerance = 8
            }
        };

        public static GameStateProfile Get(int yearGroup)
            => Profiles[yearGroup];
    }
}
