using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiveTranslateOverlay.Core.Models.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(NewTextEventDto), "new_text")]
[JsonDerivedType(typeof(AddTextEventDto), "add_text")]
[JsonDerivedType(typeof(TranslationEventDto), "translation")]
[JsonDerivedType(typeof(DownloadRequiredEventDto), "download_required")]
[JsonDerivedType(typeof(DownloadProgressEventDto), "download_progress")]
[JsonDerivedType(typeof(DownloadCompletedEventDto), "download_completed")]
[JsonDerivedType(typeof(StatusEventDto), "status")]
[JsonDerivedType(typeof(ErrorEventDto), "error")]
public abstract record IpcEventDto;

public sealed record NewTextEventDto(
    [property: JsonPropertyName("phrase_id")] int PhraseId,
    [property: JsonPropertyName("spk_idx")] int SpeakerIndex,
    [property: JsonPropertyName("start_time")] double StartTime,
    [property: JsonPropertyName("text")] string Text
) : IpcEventDto;

public sealed record AddTextEventDto(
    [property: JsonPropertyName("phrase_id")] int PhraseId,
    [property: JsonPropertyName("text")] string Text
) : IpcEventDto;

public sealed record TranslationEventDto(
    [property: JsonPropertyName("phrase_id")] int PhraseId,
    [property: JsonPropertyName("translation")] string Translation
) : IpcEventDto;

public sealed record MissingModelItemDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("target_path")] string TargetPath,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("size_bytes")] long SizeBytes
);

public sealed record DownloadRequiredEventDto(
    [property: JsonPropertyName("models")] IReadOnlyList<MissingModelItemDto> Models
) : IpcEventDto;

public sealed record DownloadProgressEventDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("downloaded_bytes")] long DownloadedBytes,
    [property: JsonPropertyName("total_bytes")] long TotalBytes,
    [property: JsonPropertyName("speed_mbps")] double SpeedMbps,
    [property: JsonPropertyName("percentage")] double Percentage
) : IpcEventDto;

public sealed record DownloadCompletedEventDto : IpcEventDto;

public sealed record StatusEventDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message
) : IpcEventDto;

public sealed record ErrorEventDto(
    [property: JsonPropertyName("message")] string Message
) : IpcEventDto;