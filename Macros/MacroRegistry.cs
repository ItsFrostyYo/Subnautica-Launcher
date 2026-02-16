using SubnauticaLauncher.Enums;
using System.Collections.Generic;
using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public sealed class MacroSteps
    {        

        public Point QuitButton { get; init; }
        public Point QuitButton2 { get; init; }
        public Point ConfirmQuit1 { get; init; }
        public Point ConfirmQuit2 { get; init; }

        public Point PlayButton { get; init; }
        public Point StartNewGame { get; init; }
        public Point SelectGameMode { get; init; }

        public int ClickDelayFast { get; init; } = 10;
        public int ClickDelayMedium { get; init; } = 20;
        public int ClickDelaySlow { get; init; } = 30;
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
                    QuitButton = new Point(960, 703),
                    ConfirmQuit1 = new Point(1058, 565),
                    ConfirmQuit2 = new Point(1058, 565),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1172, 554),
                    SelectGameMode = new Point(1172, 554)
                },

                [GameMode.Hardcore] = new MacroSteps
                {
                    QuitButton = new Point(951, 706),
                    ConfirmQuit1 = new Point(1062, 562),
                    ConfirmQuit2 = new Point(1062, 562),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1172, 554),
                    SelectGameMode = new Point(1175, 788)
                },

                [GameMode.Creative] = new MacroSteps
                {
                    QuitButton = new Point(960, 703),
                    ConfirmQuit1 = new Point(1058, 565),
                    ConfirmQuit2 = new Point(1058, 565),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1172, 554),
                    SelectGameMode = new Point(1184, 905)
                },

                [GameMode.Freedom] = new MacroSteps
                {
                    QuitButton = new Point(960, 703),
                    ConfirmQuit1 = new Point(1058, 565),
                    ConfirmQuit2 = new Point(1058, 565),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1172, 554),
                    SelectGameMode = new Point(1184, 681)
                },

                [GameMode.SaveSlot1] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1175, 674),
                    SelectGameMode = new Point(1175, 674)
                },

                [GameMode.SaveSlot2] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1178, 790),
                    SelectGameMode = new Point(1178, 790)
                },

                [GameMode.SaveSlot3] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(788, 793),
                    StartNewGame = new Point(1187, 911),
                    SelectGameMode = new Point(1187, 911)
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

                [GameMode.Freedom] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1373, 540),
                    SelectGameMode = new Point(1157, 662)
                },

                [GameMode.SaveSlot1] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1182, 674),
                    SelectGameMode = new Point(1170, 674)
                },

                [GameMode.SaveSlot2] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1160, 788),
                    SelectGameMode = new Point(1160, 788)
                },

                [GameMode.SaveSlot3] = new MacroSteps
                {
                    QuitButton = new Point(1020, 689),
                    ConfirmQuit1 = new Point(893, 553),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 795),
                    StartNewGame = new Point(1180, 917),
                    SelectGameMode = new Point(1170, 917)
                }
            },

            // ================= 2022–2025 =================
            [2022] = new Dictionary<GameMode, MacroSteps>
            {
                [GameMode.Survival] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(986, 552)
                },
                [GameMode.Hardcore] = new MacroSteps
                {
                    QuitButton = new Point(1067, 691),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1093, 781)
                },
                [GameMode.Creative] = new MacroSteps
                {
                    QuitButton = new Point(1021, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1125, 878)
                },

                [GameMode.Freedom] = new MacroSteps
                {
                    QuitButton = new Point(1021, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1154, 674)
                },

                [GameMode.SaveSlot1] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1190, 692),
                    SelectGameMode = new Point(1180, 692)
                },
                [GameMode.SaveSlot2] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1167, 812),
                    SelectGameMode = new Point(1150, 812)
                },
                [GameMode.SaveSlot3] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1176, 932),
                    SelectGameMode = new Point(1160, 932)
                }
            },

            // ================= BELOW ZERO =================
            [BuildYearResolver.BELOW_ZERO_GROUP] = new Dictionary<GameMode, MacroSteps>
            {
                [GameMode.Survival] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    QuitButton2 = new Point(961, 689),                   
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(986, 552)
                },
                [GameMode.Hardcore] = new MacroSteps
                {
                    QuitButton = new Point(1067, 691),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1093, 781)
                },
                [GameMode.Creative] = new MacroSteps
                {
                    QuitButton = new Point(1021, 728),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1125, 878)
                },

                [GameMode.Freedom] = new MacroSteps
                {
                    QuitButton = new Point(1021, 728),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1300, 569),
                    SelectGameMode = new Point(1154, 674)
                },

                [GameMode.SaveSlot1] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1190, 692),
                    SelectGameMode = new Point(1180, 692)
                },
                [GameMode.SaveSlot2] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1167, 812),
                    SelectGameMode = new Point(1150, 812)
                },
                [GameMode.SaveSlot3] = new MacroSteps
                {
                    QuitButton = new Point(1005, 728),
                    QuitButton2 = new Point(961, 689),
                    ConfirmQuit1 = new Point(903, 568),
                    ConfirmQuit2 = new Point(830, 623),
                    PlayButton = new Point(635, 728),
                    StartNewGame = new Point(1176, 932),
                    SelectGameMode = new Point(1160, 932)
                }           
              }
            };

        public static MacroSteps Get(int yearGroup, GameMode mode)
        {
            return Registry[yearGroup][mode];
        }
    }
}