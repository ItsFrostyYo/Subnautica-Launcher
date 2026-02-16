using SubnauticaLauncher.Enums;
using System;

namespace SubnauticaLauncher.Gameplay
{
    public sealed class GameplayEvent
    {
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public string Game { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public GameplayEventType Type { get; init; }
        public string Key { get; init; } = string.Empty;
        public int Delta { get; init; }
        public string Source { get; init; } = "dynamic-mono";
    }
}
