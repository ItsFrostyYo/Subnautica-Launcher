using System.Collections.Generic;
using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public sealed class MacroSteps
    {
        public Point QuitButton { get; init; }
        public Point ConfirmQuit1 { get; init; }
        public Point ConfirmQuit2 { get; init; }

        public Point PlayButton { get; init; }
        public Point StartNewGame { get; init; }
        public Point SelectGameMode { get; init; }

        public int ClickDelayFast { get; init; } = 10;
        public int ClickDelayMedium { get; init; } = 15;
        public int ClickDelaySlow { get; init; } = 25;
    }

    public static class MacroRegistry
    {
        // YearGroup → GameMode → MacroSteps
        private static readonly Dictionary<int, Dictionary<GameMode, MacroSteps>> Registry = new()
        {
            // ================= 2014–2017 =================
            [2017] = new Dictionary<GameMode, MacroSteps>
            {
                [GameMode.Survival] = new MacroSteps
                {
                    QuitButton = new Point(0, 0),
                    ConfirmQuit1 = new Point(0, 0),
                    ConfirmQuit2 = new Point(0, 0),
                    PlayButton = new Point(0, 0),
                    StartNewGame = new Point(0, 0),
                    SelectGameMode = new Point(0, 0)
                }
              
            },

            // ================= 2018–2021 =================
            [2018] = new Dictionary<GameMode, MacroSteps>
            {
                [GameMode.Survival] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1373, 540),
                    SelectGameMode = new Point(1403, 537)
                },
                [GameMode.Hardcore] = new MacroSteps
                {
                    QuitButton = new Point(1052, 662),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1373, 540),
                    SelectGameMode = new Point(1107, 754)
                },
                [GameMode.Creative] = new MacroSteps
                {
                    QuitButton = new Point(1012, 686),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1373, 540),
                    SelectGameMode = new Point(1184, 911)
                },
                [GameMode.SaveSlot1] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1019, 567),
                    SelectGameMode = new Point(1403, 537)
                },
                [GameMode.SaveSlot2] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1019, 567),
                    SelectGameMode = new Point(1403, 537)
                },
                [GameMode.SaveSlot3] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1019, 567),
                    SelectGameMode = new Point(1403, 537)
                }
            },

            // ================= 2022–2025 =================
            [2022] = new Dictionary<GameMode, MacroSteps>
            {
                [GameMode.Survival] = new MacroSteps
                {
                    QuitButton = new Point(0, 0),
                    ConfirmQuit1 = new Point(0, 0),
                    ConfirmQuit2 = new Point(0, 0),
                    PlayButton = new Point(0, 0),
                    StartNewGame = new Point(0, 0),
                    SelectGameMode = new Point(0, 0)
                }
            }
        };

        public static MacroSteps Get(int yearGroup, GameMode mode)
        {
            return Registry[yearGroup][mode];
        }
    }
}