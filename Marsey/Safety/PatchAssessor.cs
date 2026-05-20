using System;
using System.IO;
using System.Security.Cryptography;
using Marsey.Misc;

namespace Marsey.Safety;

/// <summary>
/// Computes patch hashes and decides whether a patch may run under a safety level.
/// </summary>
public static class PatchAssessor
{
    /// <summary>
    /// Computes the SHA-256 hash of a file, as a lowercase hex string.
    /// Returns an empty string if the file cannot be read.
    /// </summary>
    public static string ComputeHash(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception e)
        {
            MarseyLogger.Log(MarseyLogger.LogType.WARN, "Safety", $"Failed to hash {filePath}: {e.Message}");
            return "";
        }
    }

    /// <summary>
    /// Whether a patch with the given verdict may run under the given safety level.
    /// </summary>
    public static bool IsAllowed(PatchVerdict verdict, PatchSafetyLevel level)
    {
        return level switch
        {
            PatchSafetyLevel.Pass => true,
            PatchSafetyLevel.Warn => verdict != PatchVerdict.Rejected,
            PatchSafetyLevel.Block => verdict == PatchVerdict.Approved,
            _ => true
        };
    }
}
