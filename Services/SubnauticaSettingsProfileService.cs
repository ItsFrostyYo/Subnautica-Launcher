using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

public sealed record SubnauticaSettingEntry(string Category, string Name, string Value);

public sealed record SubnauticaSettingsProfileInfo(bool Exists, string SourceDisplayName);

public static class SubnauticaSettingsProfileService
{
    private const string OptionsRelativePath = @"SNAppData\SavedGames\options\options.bin";
    private static string ProfileDirectory => Path.Combine(AppPaths.DataPath, "SubnauticaData");
    private static string ProfilePath => Path.Combine(ProfileDirectory, "options.bin");
    private static string MetadataPath => Path.Combine(ProfileDirectory, "profile.json");

    public static string GetVersionOptionsPath(LauncherVersion version) =>
        Path.Combine(version.GameFolder, OptionsRelativePath);

    public static SubnauticaSettingsProfileInfo GetProfileInfo()
    {
        if (!File.Exists(ProfilePath)) return new(false, string.Empty);

        try
        {
            ProfileMetadata? metadata = JsonSerializer.Deserialize<ProfileMetadata>(File.ReadAllText(MetadataPath));
            return new(true, metadata?.SourceDisplayName ?? "Unknown Version");
        }
        catch
        {
            return new(true, "Unknown Version");
        }
    }

    public static bool TrySaveFromVersion(LauncherVersion version, out string error)
    {
        string source = GetVersionOptionsPath(version);
        if (!File.Exists(source))
        {
            error = $"{version.DisplayName} does not have a saved Subnautica options.bin file yet.";
            return false;
        }

        try
        {
            byte[] data = ReadWithRetry(source);
            if (Parse(data).Count == 0)
                throw new InvalidDataException("The selected options.bin did not contain any readable Subnautica settings.");

            Directory.CreateDirectory(ProfileDirectory);
            WriteAtomic(ProfilePath, data);
            WriteAtomic(MetadataPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new ProfileMetadata(version.DisplayName, version.GameFolder, DateTime.UtcNow),
                new JsonSerializerOptions { WriteIndented = true })));
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = "The settings could not be saved: " + exception.Message;
            return false;
        }
    }

    public static bool TryCopyToVersion(LauncherVersion version, out string error)
    {
        if (!File.Exists(ProfilePath))
        {
            error = "Save Subnautica settings before copying them to a version.";
            return false;
        }

        try
        {
            byte[] data = ReadWithRetry(ProfilePath);
            if (Parse(data).Count == 0)
                throw new InvalidDataException("The saved options.bin profile is not a readable Subnautica settings file.");
            WriteToGameOptionsDirectory(version.GameFolder, data);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"The settings could not be copied to {version.DisplayName}: {exception.Message}";
            return false;
        }
    }

    public static bool TryApplyToNewInstall(string gameFolder, out string error)
    {
        if (!File.Exists(ProfilePath))
        {
            error = string.Empty;
            return true;
        }

        try
        {
            byte[] data = ReadWithRetry(ProfilePath);
            if (Parse(data).Count == 0)
                throw new InvalidDataException("The saved options.bin profile is not a readable Subnautica settings file.");
            WriteToGameOptionsDirectory(gameFolder, data);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = "Saved Subnautica settings could not be applied to the new install: " + exception.Message;
            return false;
        }
    }

    public static IReadOnlyList<SubnauticaSettingEntry> LoadSavedEntries()
    {
        if (!File.Exists(ProfilePath)) return [];
        return Parse(ReadWithRetry(ProfilePath));
    }

    private static byte[] ReadWithRetry(string path)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                byte[] data = new byte[stream.Length];
                stream.ReadExactly(data);
                return data;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                Thread.Sleep(100);
            }
        }

        throw lastError ?? new IOException("The settings file could not be read.");
    }

    private static void WriteAtomic(string destination, byte[] data)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    private static void WriteToGameOptionsDirectory(string gameFolder, byte[] data)
    {
        string optionsDirectory = Path.Combine(gameFolder, @"SNAppData\SavedGames\options");
        Directory.CreateDirectory(optionsDirectory);

        // Subnautica reads its device-specific file before options.bin when one exists.
        // Keep both copies synchronized so applying a profile works on previously played versions too.
        foreach (string machineOptions in Directory.EnumerateFiles(optionsDirectory, "*-options.bin", SearchOption.TopDirectoryOnly))
            WriteAtomic(machineOptions, data);
        WriteAtomic(Path.Combine(optionsDirectory, "options.bin"), data);
    }

    private static IReadOnlyList<SubnauticaSettingEntry> Parse(byte[] data)
    {
        int offset = 0;
        int payloadOffset = 0;
        if (TryReadVarint(data, ref offset, out ulong declaredLength) && declaredLength == (ulong)(data.Length - offset))
            payloadOffset = offset;

        var rawSettings = new List<RawSetting>();
        offset = payloadOffset;
        while (offset < data.Length)
        {
            if (!TryReadVarint(data, ref offset, out ulong tag)) break;
            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 7);
            if (wireType != 2 || field is < 1 or > 4 ||
                !TryReadVarint(data, ref offset, out ulong entryLength) || entryLength > (ulong)(data.Length - offset))
            {
                if (!SkipValue(data, ref offset, wireType)) break;
                continue;
            }

            int end = offset + (int)entryLength;
            string? key = null;
            string? value = null;
            while (offset < end)
            {
                if (!TryReadVarint(data, ref offset, out ulong entryTag)) break;
                int entryField = (int)(entryTag >> 3);
                int entryWire = (int)(entryTag & 7);
                if (entryField == 1 && entryWire == 2 && TryReadString(data, ref offset, end, out string parsedKey))
                    key = parsedKey;
                else if (entryField == 2)
                    value = ReadTypedValue(data, ref offset, end, field, entryWire);
                else if (!SkipValue(data, ref offset, entryWire, end))
                    break;
            }
            offset = end;
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
                rawSettings.Add(new(key, value, (SettingValueKind)field));
        }

        return FormatSettings(rawSettings);
    }

    private static string? ReadTypedValue(byte[] data, ref int offset, int end, int collection, int wireType)
    {
        if (collection == 1 && wireType == 5 && offset + 4 <= end)
        {
            float number = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
            offset += 4;
            return number.ToString("0.######", CultureInfo.InvariantCulture);
        }
        if (collection == 2 && wireType == 2 && TryReadString(data, ref offset, end, out string text))
            return string.IsNullOrEmpty(text) ? "(Not set)" : text;
        if (collection is 3 or 4 && wireType == 0 && TryReadVarint(data, ref offset, out ulong integer))
            return integer.ToString(CultureInfo.InvariantCulture);

        SkipValue(data, ref offset, wireType, end);
        return null;
    }

    private static bool TryReadString(byte[] data, ref int offset, int end, out string value)
    {
        value = string.Empty;
        if (!TryReadVarint(data, ref offset, out ulong length) || length > (ulong)(end - offset)) return false;
        value = Encoding.UTF8.GetString(data, offset, (int)length);
        offset += (int)length;
        return true;
    }

    private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && offset < data.Length; shift += 7)
        {
            byte current = data[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return true;
        }
        return false;
    }

    private static bool SkipValue(byte[] data, ref int offset, int wireType, int? limit = null)
    {
        int end = limit ?? data.Length;
        switch (wireType)
        {
            case 0: return TryReadVarint(data, ref offset, out _);
            case 1 when offset + 8 <= end: offset += 8; return true;
            case 2 when TryReadVarint(data, ref offset, out ulong length) && length <= (ulong)(end - offset): offset += (int)length; return true;
            case 5 when offset + 4 <= end: offset += 4; return true;
            default: return false;
        }
    }

    private static IReadOnlyList<SubnauticaSettingEntry> FormatSettings(IReadOnlyList<RawSetting> rawSettings)
    {
        var byKey = rawSettings
            .GroupBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var entries = new List<SubnauticaSettingEntry>();

        if (TryCalculateGraphicsPreset(byKey, out string preset))
            entries.Add(new("Graphics", "Preset", preset));

        foreach (RawSetting setting in rawSettings)
        {
            SubnauticaSettingEntry? entry = FormatSetting(setting);
            if (entry is not null) entries.Add(entry);
        }

        string[] categoryOrder = ["General", "Graphics", "Sound", "Keyboard", "Controller", "Accessibility", "Other"];
        return entries
            .OrderBy(entry => Array.IndexOf(categoryOrder, entry.Category) is int index && index >= 0 ? index : categoryOrder.Length)
            .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name.Equals("Preset", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SubnauticaSettingEntry? FormatSetting(RawSetting setting)
    {
        string key = setting.Key;
        string value = setting.Value;
        if (key is "Version" or "Misc/ConsoleHistory" or "Misc/Email" or "Misc/RememberEmail" or "Misc/HideEmailBox" or "Input/InputBackend" or "InputBackend" ||
            key.StartsWith("Misc/NewsfeedPopup/", StringComparison.OrdinalIgnoreCase))
            return null;

        if (key.StartsWith("Input/Binding/", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("InputSystem/Binding/", StringComparison.OrdinalIgnoreCase))
            return FormatBinding(setting);

        return key switch
        {
            "Locale/Language" => Entry("General", "Language", value),
            "Locale/Subtitles" => Toggle("General", "Subtitles", value),
            "Locale/SubtitlesSpeed" => Number("General", "Subtitle speed (chars/sec)", value),
            "Misc/RunInBackground" => Toggle("General", "Run in background", value),
            "Misc/NewsEnabled" => Toggle("General", "Show news", value),
            "Misc/FieldOfView" => Number("General", "Field of view", value),
            "Graphics/FOV" => Number("General", "Field of view", value),
            "VR/RenderScale" => Percent("General", "VR render scale", value),
            "VR/GazeBasedCursor" => Toggle("Controller", "Gaze based cursor", value),

            "Graphics/VSync" => Toggle("Graphics", "Vsync", value),
            "Graphics/FrameRate" => Number("Graphics", "FPS cap", value),
            "Graphics/Gamma" => Decimal("Graphics", "Gamma", value, "0.00"),
            "Graphics/ColorGrading" => Choice("Graphics", "Color grading", value, ["Off", "Neutral", "Filmic"]),
            "Graphics/Quality" => Choice("Graphics", "Detail", value, ["Low", "Medium", "High"]),
            "Graphics/WaterQuality" => Entry("Graphics", "Water quality", NormalizeChoice(value)),
            "Graphics/WaterSurfaceQuality" => Entry("Graphics", "Water quality", NormalizeChoice(value)),
            "Graphics/AntiAliasingMode" => Choice("Graphics", "Anti-aliasing", value, ["FXAA", "TAA"]),
            "Graphics/AntiAliasingQuality" => Choice("Graphics", "Anti-aliasing quality", value, ["Off", "Low", "Medium", "High"]),
            "Graphics/Bloom" => Toggle("Graphics", "Bloom", value),
            "Graphics/BloomLensDirt" => Toggle("Graphics", "Lens dirt", value),
            "Graphics/DepthOfField" => Toggle("Graphics", "Depth of field", value),
            "Graphics/MotionBlurQuality" => Choice("Graphics", "Motion blur quality", value, ["Off", "Low", "Medium", "High"]),
            "Graphics/AmbientOcclusionQuality" => Choice("Graphics", "Ambient occlusion", value, ["Off", "Low", "Medium", "High"]),
            "Graphics/ScreenSpaceReflectionsQuality" => Choice("Graphics", "Screen-space reflections", value, ["Off", "Low", "Medium", "High"]),
            "Graphics/Dithering" => Toggle("Graphics", "Dithering", value),

            "Audio/MasterVolume" => Percent("Sound", "Master volume", value),
            "Audio/MusicVolume" => Percent("Sound", "Music volume", value),
            "Audio/VoiceVolume" => Percent("Sound", "Voice volume", value),
            "Audio/AmbientVolume" => Percent("Sound", "Ambient volume", value),
            "Sound/MasterVolume" => Percent("Sound", "Master volume", value),
            "Sound/MusicVolume" => Percent("Sound", "Music volume", value),
            "Sound/VoiceVolume" => Percent("Sound", "Voice volume", value),
            "Sound/AmbientVolume" => Percent("Sound", "Ambient volume", value),
            "MasterVolume" => Percent("Sound", "Master volume", value),
            "MusicVolume" => Percent("Sound", "Music volume", value),
            "VoiceVolume" => Percent("Sound", "Voice volume", value),
            "AmbientVolume" => Percent("Sound", "Ambient volume", value),

            "Input/InvertMouse" => Toggle("Keyboard", "Invert look", value),
            "InputSystem/InvertMouse" => Toggle("Keyboard", "Invert look", value),
            "Input/MouseSensitivity" => Percent("Keyboard", "Mouse sensitivity", value),
            "InputSystem/MouseSensitivity" => Percent("Keyboard", "Mouse sensitivity", value),
            "Input/RunMode" => Choice("Keyboard", "Run mode", value, ["Hold to run", "Hold to walk", "Press to toggle"]),
            "InputSystem/RunMode" => Choice("Keyboard", "Run mode", value, ["Hold to run", "Hold to walk", "Press to toggle"]),
            "Input/ControllerEnabled" => Toggle("Controller", "Enable controller", value),
            "InputSystem/ControllerEnabled" => Toggle("Controller", "Enable controller", value),
            "Input/ControllerLayout" => Entry("Controller", "Layout", FormatControllerLayout(value)),
            "InputSystem/ControllerLayout" => Entry("Controller", "Layout", FormatControllerLayout(value)),
            "Input/InvertController" => Toggle("Controller", "Invert look", value),
            "InputSystem/InvertController" => Toggle("Controller", "Invert look", value),
            "Input/ControllerSensitivityX" => Percent("Controller", "Horizontal sensitivity", value),
            "InputSystem/ControllerSensitivityX" => Percent("Controller", "Horizontal sensitivity", value),
            "Input/ControllerSensitivityY" => Percent("Controller", "Vertical sensitivity", value),
            "InputSystem/ControllerSensitivityY" => Percent("Controller", "Vertical sensitivity", value),
            "Input/DeadzoneMin" => Percent("Controller", "Deadzone minimum", value),
            "InputSystem/DeadzoneMin" => Percent("Controller", "Deadzone minimum", value),
            "Input/DeadzoneMax" => Percent("Controller", "Deadzone maximum", value),
            "InputSystem/DeadzoneMax" => Percent("Controller", "Deadzone maximum", value),

            "Misc/UIScale" => Decimal("Accessibility", "UI scale", value, "0.00"),
            "Misc/UIScaleHandheld" => Decimal("Accessibility", "UI scale (handheld)", value, "0.00"),
            "Misc/PDAPause" => Toggle("Accessibility", "PDA pause", value),
            "Misc/Flashes" => Toggle("Accessibility", "Lightning flashes", value),
            "Misc/CameraBobbing" => Toggle("Accessibility", "Camera bobbing", value),
            _ => FormatUnknown(setting)
        };
    }

    private static SubnauticaSettingEntry FormatBinding(RawSetting setting)
    {
        string[] parts = setting.Key.Split('/');
        string device = parts.Length > 2 ? parts[2] : "Keyboard";
        string action = parts.Length > 3 ? FormatAction(parts[3]) : "Binding";
        string slot = parts.Length > 4 ? NormalizeChoice(parts[4]) : string.Empty;
        return Entry(device.Equals("Controller", StringComparison.OrdinalIgnoreCase) ? "Controller" : "Keyboard",
            string.IsNullOrEmpty(slot) ? action : $"{action} ({slot})", FormatBindingValue(setting.Value));
    }

    private static SubnauticaSettingEntry? FormatUnknown(RawSetting setting)
    {
        string root = setting.Key.Split('/')[0];
        string category = root switch
        {
            "Graphics" => "Graphics",
            "Audio" => "Sound",
            "Sound" => "Sound",
            "Input" or "InputSystem" => "Keyboard",
            "Locale" or "Misc" or "VR" => "General",
            _ => "Other"
        };
        string name = Humanize(setting.Key.Split('/').Last());
        string value = setting.Kind switch
        {
            SettingValueKind.Boolean => FormatToggle(setting.Value),
            SettingValueKind.Float => FormatDecimal(setting.Value, "0.######"),
            _ => NormalizeChoice(setting.Value)
        };
        return Entry(category, name, value);
    }

    private static bool TryCalculateGraphicsPreset(IReadOnlyDictionary<string, RawSetting> values, out string preset)
    {
        preset = string.Empty;
        GraphicsPreset[] presets =
        [
            new("Low", 0, "Medium", 0, 1, 0, 0, true, false, 0, false, true),
            new("Medium", 1, "Medium", 0, 2, 0, 1, true, false, 1, true, true),
            new("High", 2, "High", 0, 3, 3, 3, true, true, 3, true, true)
        ];

        if (!TryInt(values, "Graphics/Quality", out int detail) ||
            !TryText(values, "Graphics/WaterQuality", out string water) ||
            !TryInt(values, "Graphics/AntiAliasingMode", out int aaMode) ||
            !TryInt(values, "Graphics/AntiAliasingQuality", out int aaQuality) ||
            !TryInt(values, "Graphics/ScreenSpaceReflectionsQuality", out int reflections) ||
            !TryInt(values, "Graphics/AmbientOcclusionQuality", out int ambientOcclusion) ||
            !TryBool(values, "Graphics/Bloom", out bool bloom) ||
            !TryBool(values, "Graphics/BloomLensDirt", out bool lensDirt) ||
            !TryInt(values, "Graphics/MotionBlurQuality", out int motionBlur) ||
            !TryBool(values, "Graphics/DepthOfField", out bool depthOfField) ||
            !TryBool(values, "Graphics/Dithering", out bool dithering))
            return false;

        preset = presets.FirstOrDefault(candidate =>
            candidate.Detail == detail && candidate.Water.Equals(water, StringComparison.OrdinalIgnoreCase) &&
            candidate.AntiAliasingMode == aaMode && candidate.AntiAliasingQuality == aaQuality &&
            candidate.Reflections == reflections && candidate.AmbientOcclusion == ambientOcclusion &&
            candidate.Bloom == bloom && candidate.LensDirt == lensDirt && candidate.MotionBlur == motionBlur &&
            candidate.DepthOfField == depthOfField && candidate.Dithering == dithering)?.Name ?? "Custom";
        return true;
    }

    private static bool TryInt(IReadOnlyDictionary<string, RawSetting> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out RawSetting? setting) &&
            int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryBool(IReadOnlyDictionary<string, RawSetting> values, string key, out bool result)
    {
        result = false;
        if (!values.TryGetValue(key, out RawSetting? setting) || !ulong.TryParse(setting.Value, out ulong number)) return false;
        result = number != 0;
        return true;
    }

    private static bool TryText(IReadOnlyDictionary<string, RawSetting> values, string key, out string result)
    {
        result = string.Empty;
        if (!values.TryGetValue(key, out RawSetting? setting)) return false;
        result = setting.Value;
        return true;
    }

    private static SubnauticaSettingEntry Entry(string category, string name, string value) => new(category, name, value);
    private static SubnauticaSettingEntry Toggle(string category, string name, string value) => Entry(category, name, FormatToggle(value));
    private static SubnauticaSettingEntry Percent(string category, string name, string value) => Entry(category, name, FormatPercent(value));
    private static SubnauticaSettingEntry Decimal(string category, string name, string value, string format) => Entry(category, name, FormatDecimal(value, format));
    private static SubnauticaSettingEntry Number(string category, string name, string value) => Entry(category, name, FormatNumber(value));
    private static SubnauticaSettingEntry Choice(string category, string name, string value, string[] choices) =>
        Entry(category, name, int.TryParse(value, out int index) && index >= 0 && index < choices.Length ? choices[index] : NormalizeChoice(value));

    private static string FormatToggle(string value) => ulong.TryParse(value, out ulong number) && number != 0 ? "Enabled" : "Disabled";
    private static string FormatPercent(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
        ? (number * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : value;
    private static string FormatDecimal(string value, string format) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
        ? number.ToString(format, CultureInfo.InvariantCulture) : value;
    private static string FormatNumber(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
        ? number.ToString("0", CultureInfo.InvariantCulture) : value;

    private static string FormatControllerLayout(string value) => value switch
    {
        "Automatic" or "0" => "Automatic",
        "Xbox360" or "1" => "Xbox 360",
        "XboxOne" or "2" => "Xbox One",
        "PS4" or "3" => "PS4",
        "Switch" or "4" => "Nintendo Switch",
        "Scarlett" or "5" => "Xbox Series X",
        "PS5" or "6" => "PS5",
        _ => NormalizeChoice(value)
    };

    private static string FormatBindingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase) || value == "(Not set)") return "Unbound";
        Match inputSystemBinding = Regex.Match(value, "^<(?<device>[^>]+)>/(?<control>.+)$");
        if (inputSystemBinding.Success)
        {
            string control = Humanize(inputSystemBinding.Groups["control"].Value.Replace('/', ' '));
            return inputSystemBinding.Groups["device"].Value.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
                ? control : $"{Humanize(inputSystemBinding.Groups["device"].Value)} {control}";
        }
        return value switch
        {
            "MouseButtonLeft" => "Left mouse button",
            "MouseButtonRight" => "Right mouse button",
            "MouseButtonMiddle" => "Middle mouse button",
            "MouseWheelUp" => "Mouse wheel up",
            "MouseWheelDown" => "Mouse wheel down",
            "ControllerButtonA" => "Controller A",
            "ControllerButtonB" => "Controller B",
            "ControllerButtonX" => "Controller X",
            "ControllerButtonY" => "Controller Y",
            "ControllerButtonLeftBumper" => "Left bumper",
            "ControllerButtonRightBumper" => "Right bumper",
            "ControllerButtonLeftStick" => "Left stick button",
            "ControllerButtonRightStick" => "Right stick button",
            "ControllerButtonBack" => "Back",
            "ControllerButtonStart" => "Start",
            "ControllerLeftBumper" => "Left bumper",
            "ControllerRightBumper" => "Right bumper",
            "ControllerLeftTrigger" => "Left trigger",
            "ControllerRightTrigger" => "Right trigger",
            "ControllerLeftStick" => "Left stick",
            "ControllerRightStick" => "Right stick",
            "ControllerDPadUp" => "D-pad up",
            "ControllerDPadDown" => "D-pad down",
            "ControllerDPadLeft" => "D-pad left",
            "ControllerDPadRight" => "D-pad right",
            "ControllerLeftStickUp" => "Left stick up",
            "ControllerLeftStickDown" => "Left stick down",
            "ControllerLeftStickLeft" => "Left stick left",
            "ControllerLeftStickRight" => "Left stick right",
            "ControllerRightStickUp" => "Right stick up",
            "ControllerRightStickDown" => "Right stick down",
            "ControllerRightStickLeft" => "Right stick left",
            "ControllerRightStickRight" => "Right stick right",
            _ => Humanize(value.Replace("KeyCode.", string.Empty, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static string FormatAction(string value) => value switch
    {
        "Exit" => "Exit / Holster",
        "LeftHand" => "Left hand / Use",
        "RightHand" => "Right hand",
        "CycleNext" => "Cycle next",
        "CyclePrev" => "Cycle previous",
        "AltTool" => "Tool alternate use",
        "TakePicture" => "Take picture",
        "MoveUp" => "Move up",
        "MoveDown" => "Move down",
        "MoveForward" => "Move forward",
        "MoveBackward" => "Move backward",
        "MoveLeft" => "Move left",
        "MoveRight" => "Move right",
        "AutoMove" => "Auto move",
        "LookUp" => "Look up",
        "LookDown" => "Look down",
        "LookLeft" => "Look left",
        "LookRight" => "Look right",
        "PDA" => "PDA",
        _ => Humanize(value)
    };

    private static string NormalizeChoice(string value) => string.IsNullOrWhiteSpace(value) || value == "(Not set)" ? "Not set" : Humanize(value);

    private static string Humanize(string value)
    {
        string spaced = Regex.Replace(value.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = Regex.Replace(spaced, "(?<=[A-Za-z])(?=[0-9])", " ");
        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private enum SettingValueKind { Float = 1, Text = 2, Boolean = 3, Integer = 4 }
    private sealed record RawSetting(string Key, string Value, SettingValueKind Kind);
    private sealed record GraphicsPreset(string Name, int Detail, string Water, int AntiAliasingMode, int AntiAliasingQuality,
        int Reflections, int AmbientOcclusion, bool Bloom, bool LensDirt, int MotionBlur, bool DepthOfField, bool Dithering);

    private sealed record ProfileMetadata(string SourceDisplayName, string SourceGameFolder, DateTime SavedAtUtc);
}
