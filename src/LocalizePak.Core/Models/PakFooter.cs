namespace LocalizePak.Core.Models;

/// <summary>
/// Unreal Engine .pak arşiv dosyasının sonundaki (footer) meta verilerini temsil eder.
/// </summary>
public sealed record PakFooter(
    int Version,
    long IndexOffset,
    long IndexSize,
    bool IsEncrypted,
    string MountPoint);
