using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PhotoPick.Core.Services;

public class LightroomService
{
    private static readonly string[] KnownLightroomPaths =
    [
        @"C:\Program Files\Adobe\Adobe Lightroom Classic\Lightroom.exe",
        @"C:\Program Files\Adobe\Adobe Lightroom CC\lightroom.exe",
        @"C:\Program Files\Adobe\Lightroom\Lightroom.exe"
    ];

    public string? FindLightroomExecutable()
    {
        // 1. Caminhos conhecidos em C:\Program Files\Adobe
        foreach (var path in KnownLightroomPaths)
        {
            if (File.Exists(path)) return path;
        }

        // 2. Registro do Windows (App Paths)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Lightroom.exe");
                var regPath = key?.GetValue("") as string;
                if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                {
                    return regPath;
                }
            }
            catch { }
        }

        return null;
    }

    public bool LaunchLightroom(string? targetFolderOrFile = null)
    {
        string? lrPath = FindLightroomExecutable();
        if (lrPath == null) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = lrPath,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(targetFolderOrFile) && (Directory.Exists(targetFolderOrFile) || File.Exists(targetFolderOrFile)))
            {
                psi.Arguments = $"\"{targetFolderOrFile}\"";
            }

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
