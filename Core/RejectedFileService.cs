using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.IO;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class RejectedFileService {
    public async Task<string> ApplyAsync(string path, QualitySettings settings) {
        if (settings.MonitorOnly || settings.RejectedFileAction == RejectedFileAction.KeepInPlace) return path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return path;

        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                return settings.RejectedFileAction switch {
                    RejectedFileAction.PrefixBad => PrefixBad(path),
                    RejectedFileAction.MoveToRejectedFolder => MoveRejected(path),
                    _ => path
                };
            } catch (IOException) when (attempt < 2) {
                await Task.Delay(250 * (attempt + 1));
            }
        }

        return path;
    }

    private static string PrefixBad(string path) {
        var directory = Path.GetDirectoryName(path) ?? "";
        var file = Path.GetFileName(path);
        if (file.StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) return path;
        return MoveCollisionSafe(path, Path.Combine(directory, "BAD_" + file));
    }

    private static string MoveRejected(string path) {
        var directory = Path.GetDirectoryName(path) ?? "";
        var rejected = Path.Combine(directory, "Rejected");
        Directory.CreateDirectory(rejected);
        return MoveCollisionSafe(path, Path.Combine(rejected, Path.GetFileName(path)));
    }

    private static string MoveCollisionSafe(string source, string desired) {
        var target = desired;
        var directory = Path.GetDirectoryName(desired) ?? "";
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        int suffix = 1;
        while (File.Exists(target)) {
            target = Path.Combine(directory, $"{name}_{suffix++}{ext}");
        }
        File.Move(source, target);
        return target;
    }
}
