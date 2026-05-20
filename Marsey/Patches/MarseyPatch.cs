using System;
using System.ComponentModel;
using System.Reflection;
using Marsey.Config;
using Marsey.Safety;

namespace Marsey.Patches;

/// <summary>
/// This class contains the data about a patch (called a Marsey), that is later used the loader to alter the game's functionality.
/// </summary>
public class MarseyPatch : IPatch, INotifyPropertyChanged
{
    public string Asmpath { get; set; } // DLL file path
    public Assembly Asm { get; set; } // Assembly containing the patch
    public string Name { get; set; } // Patch's name
    public string Desc { get; set; } // Patch's description
    public MethodInfo? Entry { get; set; } // Method to execute on patch, if available
    public bool Preload { get; set; } = false; // Is the patch getting loaded before game assemblies

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

    public MarseyPatch(string asmpath, Assembly asm, string name, string desc, bool preload = false)
    {
        this.Asmpath = asmpath;
        this.Name = name;
        this.Desc = desc;
        this.Asm = asm;
        this.Preload = preload;
    }

    public override bool Equals(object obj)
    {
        if (obj is MarseyPatch other)
        {
            return this.Name == other.Name && this.Desc == other.Desc;
        }
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Name, Desc);
}
