using System;
using System.Collections.Generic;

namespace SubnauticaLauncher.Updates;

public sealed record LauncherUpdateFile(string Path, string Status);

public sealed record LauncherUpdate(
    Version Version,
    string Title,
    string Date,
    IReadOnlyList<LauncherUpdateFile> Files,
    IReadOnlyList<string> Changelog);

public sealed record LauncherUpdateProgress(string Status, string Detail, double? Percent);
