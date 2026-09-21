using System;
using System.IO;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class SessionPathResolver {
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "QualitySessionMeter", "Sessions");

    public static string Resolve(int mode, string customDirectory, string imagePath) {
        if (mode == 0) return DefaultDirectory;
        if (mode == 1) {
            if (string.IsNullOrWhiteSpace(customDirectory) || !Path.IsPathFullyQualified(customDirectory))
                throw new ArgumentException("Choose an absolute session folder.");
            return Path.Combine(Path.GetFullPath(customDirectory), "QSM");
        }
        if (mode != 2) throw new ArgumentOutOfRangeException(nameof(mode));
        if (string.IsNullOrWhiteSpace(imagePath) || !Path.IsPathFullyQualified(imagePath))
            throw new ArgumentException("The saved LIGHT path is unavailable.");
        var imageFolder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(imagePath)));
        for (var folder = imageFolder; folder.Parent != null; folder = folder.Parent) {
            if (folder.Name.Equals("LIGHT", StringComparison.OrdinalIgnoreCase) || folder.Name.Equals("LIGHTS", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(folder.Parent.FullName, "QSM");
        }
        return Path.Combine(imageFolder.FullName, "QSM");
    }
}
