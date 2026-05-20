using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Microsoft.Toolkit.Mvvm.Input;
using Serilog;
using Splat;
using Marsey.Config;
using Marsey.Game.Resources;
using Marsey.Patches;
using Marsey.Safety;
using Marsey.Subversion;
using Marsey.Misc;
using SS14.Launcher.Marseyverse;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs
{
    public class PatchesTabViewModel : MainWindowTabViewModel
    {
        public override string Name => "Plugins";
        public ObservableCollection<MarseyPatch> MarseyPatches { get; } = new ObservableCollection<MarseyPatch>();
        public ObservableCollection<SubverterPatch> SubverterPatches { get; } = new ObservableCollection<SubverterPatch>();
        public ObservableCollection<ResourcePack> ResourcePacks { get; } = new ObservableCollection<ResourcePack>();
        public ICommand OpenPatchDirectoryCommand { get; }
        public ICommand ReloadModsCommand { get; }
        public ICommand EnableRefreshCommand { get; }

#if DEBUG
        public bool ShowRPacks => true;
#else
        public bool ShowRPacks => false;
#endif

        public PatchesTabViewModel()
        {
            OpenPatchDirectoryCommand = new RelayCommand(() => OpenPatchDirectory(MarseyVars.MarseyFolder));
            ReloadModsCommand = new RelayCommand(ReloadModsAndRefresh);
            EnableRefreshCommand = new RelayCommand(Refresh);
            ReloadMods();
        }

        private bool first = true;
        private void ReloadMods()
        {
            LoadInitialResources();
            LoadPatches();

            if (!first) return;

            EnableConfiguredPatches();
            first = false;
        }

        /// <summary>
        /// Re-reads the patch DLLs and the safety catalog. Bound to the "Recheck mods" button.
        /// </summary>
        private void ReloadModsAndRefresh()
        {
            ReloadMods();
            _ = RefreshCatalogAsync();
        }

        /// <summary>
        /// Opening the patches tab re-reads the safety catalog live from the repository.
        /// </summary>
        public override void Selected()
        {
            base.Selected();
            // Re-scan the folder too, so renamed or deleted patches do not linger in the list.
            ReloadModsAndRefresh();
        }

        private bool _refreshingCatalog;

        /// <summary>
        /// Reads the safety catalog live from the configured URLs, then refreshes verdicts.
        /// Called when the patches tab is opened and on "Recheck mods".
        /// Re-entrant calls (e.g. overlapping tab switches) are ignored.
        /// </summary>
        public async Task RefreshCatalogAsync()
        {
            if (_refreshingCatalog)
                return;

            _refreshingCatalog = true;
            try
            {
                DataManager cfg = Locator.Current.GetRequiredService<DataManager>();
                await SafetyCatalog.LoadFromUrlAsync(
                    cfg.GetCVar(CVars.PatchValidatedUrl),
                    cfg.GetCVar(CVars.PatchRejectedUrl));
                // Re-apply the saved enables: a patch may have been blocked earlier only because
                // the catalog had not been read yet. RefreshVerdicts then drops anything forbidden.
                EnableConfiguredPatches();
                RefreshVerdicts();
            }
            finally
            {
                _refreshingCatalog = false;
            }
        }

        /// <summary>
        /// Re-applies the safety catalog: force-disables anything now forbidden and rebinds
        /// the lists so the status icons reflect the (possibly updated) catalog.
        /// </summary>
        public void RefreshVerdicts()
        {
            foreach (IPatch patch in Marsyfier.GetMarseyPatches())
                if (!patch.Allowed) patch.Enabled = false;

            foreach (IPatch patch in Subverter.GetSubverterPatches())
                if (!patch.Allowed) patch.Enabled = false;

            RebindPatchList(Marsyfier.GetMarseyPatches(), MarseyPatches);
            RebindPatchList(Subverter.GetSubverterPatches(), SubverterPatches);
        }

        private void LoadInitialResources()
        {
            FileHandler.LoadAssemblies();
            ResMan.LoadDir();
        }

        private void LoadPatches()
        {
            RebindPatchList(Marsyfier.GetMarseyPatches(), MarseyPatches);
            RebindPatchList(Subverter.GetSubverterPatches(), SubverterPatches);
            LoadResPacks(ResMan.GetRPacks(), ResourcePacks);
        }

        private void EnableConfiguredPatches()
        {
            List<string> assemblies = Persist.LoadPatchlistConfig();
            LoadEnabledPatches(assemblies, MarseyPatches);
            LoadEnabledPatches(assemblies, SubverterPatches);
        }

        private void OpenPatchDirectory(string directoryName)
        {
            Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = Path.Combine(Directory.GetCurrentDirectory(), directoryName)
            });
        }

        private static void RebindPatchList<T>(List<T> source, ObservableCollection<T> target) where T : IPatch
        {
            // Clearing and re-adding the same patch objects forces the list to re-evaluate
            // its bindings - the patch objects themselves keep their Enabled state.
            target.Clear();
            foreach (T patch in source)
                target.Add(patch);
        }

        private void LoadResPacks(List<ResourcePack> ResPacks, ICollection<ResourcePack> RPacks)
        {
            foreach (ResourcePack resource in ResPacks)
            {
                if (RPacks.All(r => r.Dir != resource.Dir)){
                    RPacks.Add(resource);
                }
            }

            Log.Debug($"Refreshed resourcepacks, got {ResourcePacks.Count}.");
        }

        private void Refresh()
        {
            List<string> assemblyFileNames = new();
            SaveEnabledPatches(MarseyPatches, assemblyFileNames);
            SaveEnabledPatches(SubverterPatches, assemblyFileNames);

            Log.Debug($"Saved {assemblyFileNames.Count} patches to config");
            Persist.SavePatchlistConfig(assemblyFileNames);
        }

        private void SaveEnabledPatches(IEnumerable<IPatch> patches, List<string> fileNames)
        {
            foreach (IPatch patch in patches)
            {
                if (patch.Enabled)
                {
                    fileNames.Add(Path.GetFileName(patch.Asmpath));
                }
            }
        }

        private void LoadEnabledPatches(List<string> fileNames, IEnumerable<IPatch> patches)
        {
            foreach (IPatch patch in from filename in fileNames from patch in patches where Path.GetFileName(patch.Asmpath) == filename select patch)
            {
                patch.Enabled = true;
            }
        }
    }
}

public class PathToFileNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? path = value as string;
        return Path.GetFileName(path);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public class BooleanToPreloadConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "(preload)" : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public class BooleanToOnOffConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "ON" : "OFF";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

/// <summary>Maps a patch verdict to the geometry of its status icon.</summary>
public class VerdictToIconGeometryConverter : IValueConverter
{
    // Material Design icon geometries (24x24 viewbox), scaled via Stretch=Uniform.
    private static readonly Geometry Check =
        Geometry.Parse("M9 16.17 L4.83 12 3.41 13.41 9 19 21 7 19.59 5.59 Z");
    private static readonly Geometry Bang =
        Geometry.Parse("M10 3 H14 V15 H10 Z M10 17 H14 V21 H10 Z");
    private static readonly Geometry Cross =
        Geometry.Parse("M19 6.41 L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 Z");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (PatchVerdict)(value ?? PatchVerdict.Unknown) switch
        {
            PatchVerdict.Approved => Check,
            PatchVerdict.Rejected => Cross,
            _ => Bang
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>Maps a patch verdict to the colour of its status icon.</summary>
public class VerdictToIconBrushConverter : IValueConverter
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFC107"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F44336"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (PatchVerdict)(value ?? PatchVerdict.Unknown) switch
        {
            PatchVerdict.Approved => Green,
            PatchVerdict.Rejected => Red,
            _ => Amber
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>Maps a patch verdict to a human-readable tooltip.</summary>
public class VerdictToTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (PatchVerdict)(value ?? PatchVerdict.Unknown) switch
        {
            PatchVerdict.Approved => "Approved - this patch's hash is in the patch repository.",
            PatchVerdict.Rejected => "Rejected - this patch's hash is in the banned list.",
            _ => "Unknown - this patch's hash is not in the patch repository."
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
