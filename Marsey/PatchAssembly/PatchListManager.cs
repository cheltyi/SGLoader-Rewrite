using Marsey.Patches;
using System.Collections.Generic;
using System.Linq;
using Marsey.Config;
using Marsey.Misc;

namespace Marsey.PatchAssembly;

/// <summary>
/// Manages patch lists.
/// </summary>
public static class PatchListManager
{
    private static readonly List<IPatch> _patches = new List<IPatch>();

    /// <summary>
    /// Drops patches whose assembly file is no longer among the given files, so a renamed
    /// or deleted patch stops showing up in the list.
    /// </summary>
    /// <param name="currentFiles">The patch files currently present in the folder.</param>
    public static void SyncToFolder(ICollection<string> currentFiles)
    {
        int removed = _patches.RemoveAll(p => !currentFiles.Contains(p.Asmpath));
        if (removed > 0)
            MarseyLogger.Log(MarseyLogger.LogType.DEBG, $"Removed {removed} patch(es) no longer present in the folder.");
    }

    /// <summary>
    /// Whether a patch loaded from the given assembly path is already in the list.
    /// </summary>
    public static bool HasPatch(string asmpath)
    {
        return _patches.Any(p => p.Asmpath == asmpath);
    }

    /// <summary>
    /// Adds a patch to the list if it is not already present.
    /// </summary>
    /// <param name="patch">The patch to add.</param>
    public static void AddPatchToList(IPatch patch)
    {
        if (_patches.Any(p => p.Asmpath == patch.Asmpath)) return;

        MarseyLogger.Log(MarseyLogger.LogType.TRCE, $"Adding {patch.Name} ({patch.Asmpath}) to patchlist");
        _patches.Add(patch);
    }

    /// <summary>
    /// Returns the list of patches of a specific type.
    /// </summary>
    public static List<T> GetPatchList<T>() where T : IPatch
    {
        return _patches.OfType<T>().ToList();
    }

    /// <summary>
    /// Clears the list of patches.
    /// </summary>
    public static void ResetList()
    {
        _patches.Clear();
    }
}
