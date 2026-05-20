using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Marsey.Misc;

namespace Marsey.Safety;

/// <summary>
/// Holds the approved and rejected patch hash sets and resolves verdicts.
/// </summary>
/// <remarks>
/// The two lists are read live from their configured URLs every time - nothing is cached
/// to disk. Each file is a JSON object mapping a (human-readable) patch name to an array
/// of hashes: <code>{ "ExamplePatch": ["hash", "hash2", ...], ... }</code>
/// The name is purely for readability; only the hashes are used for lookups.
/// </remarks>
public static class SafetyCatalog
{
    private static readonly HashSet<string> Approved = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Rejected = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// Reads the approved and rejected lists live from their URLs and replaces the catalog.
    /// Nothing is cached - the lists are fetched fresh on every call.
    /// </summary>
    public static async Task LoadFromUrlAsync(string? validatedUrl, string? rejectedUrl)
    {
        Approved.Clear();
        Rejected.Clear();

        await FetchInto(validatedUrl, Approved, "approved");
        await FetchInto(rejectedUrl, Rejected, "rejected");

        MarseyLogger.Log(MarseyLogger.LogType.INFO, "Safety",
            $"Read safety catalog: {Approved.Count} approved, {Rejected.Count} rejected hashes.");
    }

    /// <summary>
    /// Blocking variant for the loader, which needs the catalog before it can patch.
    /// </summary>
    public static void LoadFromUrl(string? validatedUrl, string? rejectedUrl)
    {
        try
        {
            LoadFromUrlAsync(validatedUrl, rejectedUrl).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            MarseyLogger.Log(MarseyLogger.LogType.WARN, "Safety", $"Failed to read safety catalog: {e.Message}");
        }
    }

    private static async Task FetchInto(string? url, HashSet<string> target, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        string normalized = NormalizeUrl(url);
        try
        {
            string json = await Http.GetStringAsync(normalized);
            ParseInto(json, target);
        }
        catch (Exception e)
        {
            MarseyLogger.Log(MarseyLogger.LogType.WARN, "Safety",
                $"Failed to read {label} list from {normalized}: {e.Message}");
        }
    }

    private static void ParseInto(string json, HashSet<string> target)
    {
        if (string.IsNullOrWhiteSpace(json))
            return; // An empty list is valid - it just contributes no hashes.

        JObject root = JObject.Parse(json);
        foreach (JProperty group in root.Properties())
        {
            if (group.Value is not JArray hashes)
                continue;

            foreach (JToken hash in hashes)
            {
                string? value = hash.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                    target.Add(value.Trim());
            }
        }
    }

    /// <summary>
    /// Turns a GitHub web URL (github.com/.../blob/...) into a raw-content URL.
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        url = url.Trim();

        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Replace("github.com", "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                     .Replace("/blob/", "/", StringComparison.OrdinalIgnoreCase)
                     .Replace("/tree/", "/", StringComparison.OrdinalIgnoreCase);
        }

        return url;
    }

    /// <summary>
    /// Resolves the verdict for a patch hash. Rejected takes precedence over approved.
    /// </summary>
    public static PatchVerdict GetVerdict(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return PatchVerdict.Unknown;

        if (Rejected.Contains(hash))
            return PatchVerdict.Rejected;

        if (Approved.Contains(hash))
            return PatchVerdict.Approved;

        return PatchVerdict.Unknown;
    }
}
