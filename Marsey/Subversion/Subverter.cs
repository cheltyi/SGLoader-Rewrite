using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Marsey.Config;
using Marsey.PatchAssembly;
using Marsey.Patches;
using Marsey.Misc;
using Marsey.Safety;
using Marsey.Stealthsey;
using Marsey.Stealthsey.Reflection;

namespace Marsey.Subversion;

/// <summary>
/// Manages patches/addons based on the Subverter patch
/// </summary>
public static class Subverter
{
    public static List<SubverterPatch> GetSubverterPatches() => PatchListManager.GetPatchList<SubverterPatch>();
}

public class SubverterPatch : IPatch, INotifyPropertyChanged
{
    public string Asmpath { get; set; }
    public Assembly Asm { get; set; }
    public string Name { get; set; }
    public string Desc { get; set; }
    public MethodInfo? Entry { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        // A patch the safety level forbids can never be turned on, not even
        // programmatically (e.g. when restoring the saved patch list).
        set
        {
            bool newValue = value && Allowed;
            if (_enabled == newValue) return;
            _enabled = newValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public string Hash { get; set; } = ""; // SHA-256 of the patch assembly file
    public PatchVerdict Verdict => SafetyCatalog.GetVerdict(Hash);
    public bool Allowed => PatchAssessor.IsAllowed(Verdict, MarseyConf.PatchSafety);

    public SubverterPatch(string asmpath, Assembly asm, string name, string desc)
    {
        Asmpath = asmpath;
        Name = name;
        Desc = desc;
        Asm = asm;
    }

    public override bool Equals(object obj)
    {
        if (obj is SubverterPatch other)
        {
            return this.Name == other.Name && this.Desc == other.Desc;
        }
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Name, Desc);
}
