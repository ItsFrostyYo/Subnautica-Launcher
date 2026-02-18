using SubnauticaLauncher.Core;
using System;
using System.IO;

namespace SubnauticaLauncher.Macros
{
    public enum ResetMacroLogChannel
    {
        Subnautica,
        BelowZero,
        Explosion
    }

    public static class ResetMacroLogger
    {
        private static readonly string BaseRelativeDirectory =
            Path.Combine("reset-macro");

        public static void Info(ResetMacroLogChannel channel, string message)
        {
            Logger.LogTo(GetRelativePath(channel), Prefix(channel, message));
        }

        public static void Warn(ResetMacroLogChannel channel, string message)
        {
            Logger.WarnTo(GetRelativePath(channel), Prefix(channel, message));
        }

        public static void Error(ResetMacroLogChannel channel, string message)
        {
            Logger.ErrorTo(GetRelativePath(channel), Prefix(channel, message));
        }

        public static void Exception(ResetMacroLogChannel channel, Exception ex, string? context = null)
        {
            Logger.ExceptionTo(GetRelativePath(channel), ex, Prefix(channel, context ?? "Reset macro exception"));
        }

        private static string GetRelativePath(ResetMacroLogChannel channel)
        {
            string fileName = channel switch
            {
                ResetMacroLogChannel.Subnautica => "subnautica-reset-macro.log",
                ResetMacroLogChannel.BelowZero => "below-zero-reset-macro.log",
                ResetMacroLogChannel.Explosion => "explosion-reset-macro.log",
                _ => "reset-macro.log"
            };

            return Path.Combine(BaseRelativeDirectory, fileName);
        }

        private static string Prefix(ResetMacroLogChannel channel, string message)
        {
            string tag = channel switch
            {
                ResetMacroLogChannel.Subnautica => "SN",
                ResetMacroLogChannel.BelowZero => "BZ",
                ResetMacroLogChannel.Explosion => "EX",
                _ => "RM"
            };

            return $"[{tag}] {message}";
        }
    }
}
