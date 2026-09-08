using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.IO;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class RejectedFileService {
    public async Task<string> ApplyAsync(string path, QualitySettings settings, bool sourceAuthorized) {
        if (settings.MonitorOnly || settings.RejectedFileAction == RejectedFileAction.KeepInPlace) return path;
        if (!sourceAuthorized) {
            throw new InvalidOperationException("Rejected-file action blocked because the frame source is not an eligible sequencer source.");
        }
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Rejected frame path is empty.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Rejected frame file does not exist when file action was requested.", path);

        IOException lastIo = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                return settings.RejectedFileAction switch {
                    RejectedFileAction.PrefixBad => PrefixBad(path),
                    RejectedFileAction.MoveToRejectedFolder => MoveRejected(path),
                    _ => path
                };
            } catch (IOException ex) {
                lastIo = ex;
                if (attempt < 2) await Task.Delay(250 * (attempt + 1));
            }
        }

        throw new IOException("Could not apply the configured rejected-file action after three attempts.", lastIo);
    }


    public Task<string> RestoreAsync(FrameQualityResult frame) {
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        if (frame.Status != FrameStatus.Rejected) throw new InvalidOperationException("Only rejected frames can have their BAD file action undone.");
        if (frame.IsSyntheticFile) throw new InvalidOperationException("Synthetic frames do not have a real image file to restore.");

        var current = !string.IsNullOrWhiteSpace(frame.FinalPath) ? frame.FinalPath : frame.OriginalPath;
        if (string.IsNullOrWhiteSpace(current)) throw new InvalidOperationException("The rejected frame has no file path.");
        if (!File.Exists(current)) throw new FileNotFoundException("The rejected image file no longer exists.", current);

        string target = null;
        if (!string.IsNullOrWhiteSpace(frame.OriginalPath) && !string.Equals(current, frame.OriginalPath, StringComparison.OrdinalIgnoreCase)) {
            target = frame.OriginalPath;
        } else {
            var directory = Path.GetDirectoryName(current) ?? "";
            var file = Path.GetFileName(current);
            if (file.StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) {
                target = Path.Combine(directory, file.Substring(4));
            } else if (string.Equals(Path.GetFileName(directory), "Rejected", StringComparison.OrdinalIgnoreCase)) {
                target = Path.Combine(Path.GetDirectoryName(directory) ?? directory, file);
            }
        }

        if (string.IsNullOrWhiteSpace(target) || string.Equals(target, current, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(current);
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? "");
        if (File.Exists(target)) throw new IOException($"Cannot undo BAD because the original filename already exists: {target}");
        File.Move(current, target);
        return Task.FromResult(target);
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
