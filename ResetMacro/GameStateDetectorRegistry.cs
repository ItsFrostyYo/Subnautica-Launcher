using System.Collections.Generic;
using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public static class GameStateDetectorRegistry
    {
        private static readonly Dictionary<int, GameStateProfile> Profiles = new()
        {
            [2017] = new GameStateProfile
            {
                MainMenuPixel = new Point(960, 520),
                MainMenuColor = Color.FromArgb(255, 255, 255),

                InGamePixel = new Point(50, 50),
                InGameColor = Color.FromArgb(255, 255, 255),

                BlackPixel = new Point(960, 540)
            },

            [2018] = new GameStateProfile
            {
                MainMenuPixel = new Point(920, 925),
                MainMenuColor = Color.FromArgb(255, 25, 113, 181),

                InGamePixel = new Point(976, 124),
                InGameColor = Color.FromArgb(255, 233, 242, 95),

                BlackPixel = new Point(960, 540)
            },

            [2022] = new GameStateProfile
            {
                MainMenuPixel = new Point(634, 729),
                MainMenuColor = Color.FromArgb(255, 27, 81, 154),
                
                InGamePixel = new Point(1033, 89),
                InGameColor = Color.FromArgb(255, 25, 177, 167),
                
                BlackPixel = new Point(960, 540)
            }
            
        };

        public static GameStateProfile Get(int yearGroup)
            => Profiles[yearGroup];
    }
}