using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveTranslateOverlay.Core.Configuration;

public sealed class AppSettings
{
    public GeneralSettings General { get; init; } = new();
    public ModelsSettings Models { get; init; } = new();
    public TranslationSettings Translation { get; init; } = new();
    public UiSettings Ui { get; init; } = new();

    public static AppSettings Load(string baseConfigPath, string? overrideConfigPath = null)
    {
        if (!File.Exists(baseConfigPath))
        {
            throw new FileNotFoundException($"Base configuration file not found: {baseConfigPath}");
        }

        var docOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        JsonNode baseNode = JsonNode.Parse(File.ReadAllText(baseConfigPath), documentOptions: docOptions)
                            ?? new JsonObject();

        if (!string.IsNullOrWhiteSpace(overrideConfigPath) && File.Exists(overrideConfigPath))
        {
            JsonNode? overrideNode = JsonNode.Parse(File.ReadAllText(overrideConfigPath), documentOptions: docOptions);
            if (overrideNode is JsonObject overrideObj && baseNode is JsonObject baseObj)
            {
                MergeJsonObjects(baseObj, overrideObj);
            }
        }

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return baseNode.Deserialize<AppSettings>(serializerOptions) ?? new AppSettings();
    }

    private static void MergeJsonObjects(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is JsonObject sourceChildObj &&
                target[property.Key] is JsonObject targetChildObj)
            {
                MergeJsonObjects(targetChildObj, sourceChildObj);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }
}

public sealed class GeneralSettings
{
    public string PythonScriptPath { get; init; } = "engine/ipc/bridge.py";
    public PyTorchSettings Pytorch { get; init; } = new();
}

public sealed class PyTorchSettings
{
    public bool AutoInstall { get; init; } = true;
    public string CudaVersion { get; init; } = "cu126";
    public string IndexBaseUrl { get; init; } = "https://download.pytorch.org/whl";
    public string CustomIndexUrl { get; init; } = string.Empty;
    public List<string> Packages { get; init; } = new() { "torch", "torchaudio" };

    public string GetResolvedIndexUrl()
    {
        if (!string.IsNullOrWhiteSpace(CustomIndexUrl))
        {
            return CustomIndexUrl.Trim();
        }
        return $"{IndexBaseUrl.TrimEnd('/')}/{CudaVersion.Trim()}";
    }
}

public sealed class ModelsSettings
{
    public string DefaultDirectory { get; init; } = "models";
    public bool RequireDownloadConfirmation { get; init; } = true;
    public ModelItemSettings Asr { get; init; } = new();
    public ModelItemSettings Diarization { get; init; } = new();
    public ModelItemSettings Llm { get; init; } = new();
}

public sealed class ModelItemSettings
{
    public string Name { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string OverridePath { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
}

public sealed class TranslationSettings
{
    public string Provider { get; init; } = "LlamaCpp";
    public string SourceLanguage { get; init; } = "English";
    public string TargetLanguage { get; init; } = "Russian";
    public string PromptTemplate { get; init; } = string.Empty;
}

public sealed class UiSettings
{
    public OverlaySettings Overlay { get; init; } = new();
    public AnimationSettings Animation { get; init; } = new();
}

public sealed class OverlaySettings
{
    public double FontSizeMultiplier { get; init; } = 0.014;
    public double WindowHeightDivisor { get; init; } = 3.0;
    public int MaxCharactersPerSubtitle { get; init; } = 60;
    public int MaxCharactersPerTranslation { get; init; } = 100;
    public List<string> SpeakerColors { get; init; } = new()
    {
        "#FDEEAA", "#B3FCE4", "#FFC8C8", "#DAB5FF"
    };
}

public sealed class AnimationSettings
{
    public int SubtitleAppearDurationMs { get; init; } = 600;
    public int SubtitleDisappearDurationMs { get; init; } = 400;
    public int VerticalShiftDurationMs { get; init; } = 650;
    public int WordFadeInDurationMs { get; init; } = 300;
    public int SentenceFadeOutDurationMs { get; init; } = 750;
    public int SubtitleTimeoutMs { get; init; } = 3500;
    public double HorizontalShiftPixelsPerSecond { get; init; } = 220.0;
    public int MinHorizontalShiftDurationMs { get; init; } = 1000;
    public int MaxHorizontalShiftDurationMs { get; init; } = 3200;
}