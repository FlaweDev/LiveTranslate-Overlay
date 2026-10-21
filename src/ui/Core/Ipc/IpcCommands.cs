using System.Text.Json.Serialization;

namespace LiveTranslateOverlay.Core.Models.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ConfirmDownloadCommandDto), "confirm_download")]
[JsonDerivedType(typeof(CancelCommandDto), "cancel")]
public abstract record IpcCommandDto;

public sealed record ConfirmDownloadCommandDto : IpcCommandDto;

public sealed record CancelCommandDto : IpcCommandDto;