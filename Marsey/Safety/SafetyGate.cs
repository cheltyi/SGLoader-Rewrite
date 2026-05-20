using System;
using System.Collections.Generic;
using System.IO;
using Marsey.Config;
using Marsey.Misc;

namespace Marsey.Safety;

/// <summary>
/// Enforces the patch safety level inside the loader. If a patch that the current
/// safety level forbids is about to be loaded, the game is terminated instead -
/// the forbidden patch is never loaded or run.
/// </summary>
public static class SafetyGate
{
    /// <summary>
    /// Verifies every patch file is allowed under the current safety level.
    /// Must be called before the assemblies are loaded.
    /// </summary>
    public static void EnforceFiles(IEnumerable<string> patchFiles)
    {
        foreach (string file in patchFiles)
            EnforceFile(file);
    }

    /// <summary>
    /// Verifies a single patch file is allowed under the current safety level.
    /// If it is not, logs a fatal error and terminates the process.
    /// </summary>
    public static void EnforceFile(string patchFile)
    {
        string hash = PatchAssessor.ComputeHash(patchFile);
        PatchVerdict verdict = SafetyCatalog.GetVerdict(hash);

        if (PatchAssessor.IsAllowed(verdict, MarseyConf.PatchSafety))
            return;

        string message =
            $"Dangerous patch '{Path.GetFileName(patchFile)}' (SHA-256 {hash}, verdict {verdict}) " +
            $"was loaded but is forbidden by the {MarseyConf.PatchSafety} Patch Safety Level. " +
            "Terminating the game to prevent it from running.";

        MarseyLogger.Log(MarseyLogger.LogType.FATL, "Safety", message);
        // The fatal message must always reach the logs, even when patch logging is disabled.
        Console.Error.WriteLine($"[MARSEY] [FATL] [Safety] {message}");

        // Never load this patch - take the whole process down.
        Environment.Exit(1);
    }
}
