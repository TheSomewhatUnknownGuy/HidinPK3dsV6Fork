using System;
using System.Diagnostics;
using System.IO;

namespace pk3DS.Core.CTR
{
    public static class XDeltaUtil
    {
        public static bool ApplyPatch(string sourceFile, string patchFile, string outputFile)
        {
            if (!File.Exists(sourceFile))
                throw new FileNotFoundException("Source file not found", sourceFile);
            if (!File.Exists(patchFile))
                throw new FileNotFoundException("Patch file not found", patchFile);

            // 1. Check if xdelta3 / xdelta executable is available on path or in local tool dir
            string exe = FindXDeltaExecutable();
            if (!string.IsNullOrEmpty(exe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-d -f -s \"{sourceFile}\" \"{patchFile}\" \"{outputFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                    return proc.ExitCode == 0 && File.Exists(outputFile);
                }
            }

            // Fallback: Copy source file or alert user if external xdelta is required for massive 680MB patch
            return false;
        }

        private static string FindXDeltaExecutable()
        {
            string localTool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xdelta3.exe");
            if (File.Exists(localTool))
                return localTool;

            string rootTool = Path.Combine(Directory.GetCurrentDirectory(), "xdelta3.exe");
            if (File.Exists(rootTool))
                return rootTool;

            return null;
        }
    }
}
