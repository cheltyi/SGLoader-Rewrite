using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class ServerListTabViewModel : MainWindowTabViewModel
{
    /// <summary>
    /// How long to wait after the last keystroke before re-filtering the server list.
    /// Re-filtering a large hub is expensive, so we don't do it on every keystroke.
    /// </summary>
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly MainWindowViewModel _windowVm;
    private readonly ServerListCache _serverListCache;

    // View-models are reused across rebuilds: changing the search string or filters does not
    // re-allocate (and re-subscribe the events of) a view-model for every server, it just
    // re-filters the ones we already have. Cleared on refresh, when the cache hands us new data.
    private readonly Dictionary<ServerStatusData, ServerEntryViewModel> _entryVms = new();

    private readonly DispatcherTimer _searchDebounceTimer;

    public ServerEntryList SearchedServers { get; } = new();

    private string? _searchString;

    public override string Name => "Servers";

    public string? SearchString
    {
        get => _searchString;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchString, value);
            // Debounce: don't rebuild the list while the user is still typing.
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }

    public bool ListTextVisible => _serverListCache.Status != RefreshListStatus.Updated;
    public bool SpinnerVisible => _serverListCache.Status < RefreshListStatus.Updated;

    public string ListText
    {
        get
        {
            var status = _serverListCache.Status;
            switch (status)
            {
                case RefreshListStatus.Error:
                    return "There was an error fetching the master server lists.";
                case RefreshListStatus.PartialError:
                    return "Failed to fetch some or all server lists. Ensure your hub configuration is correct.";
                case RefreshListStatus.UpdatingMaster:
                    return "Fetching master server list...";
                case RefreshListStatus.Updating:
                    return "Discovering servers...";
                case RefreshListStatus.NotUpdated:
                    return "";
                case RefreshListStatus.Updated:
                default:
                    if (SearchedServers.Count == 0 && _serverListCache.AllServers.Count != 0)
                        // TODO: Actually make this show up or just remove it entirely
                        return "No servers match your search or filter settings.";

                    if (_serverListCache.AllServers.Count == 0)
                        return "There are no public servers. Ensure your hub configuration is correct.";

                    return "";
            }
        }
    }

    [Reactive] public bool FiltersVisible { get; set; }

    public ServerListFiltersViewModel Filters { get; }

    public ServerListTabViewModel(MainWindowViewModel windowVm)
    {
        Filters = new ServerListFiltersViewModel(windowVm.Cfg);
        Filters.FiltersUpdated += FiltersOnFiltersUpdated;

        _windowVm = windowVm;
        _serverListCache = Locator.Current.GetRequiredService<ServerListCache>();

        _searchDebounceTimer = new DispatcherTimer { Interval = SearchDebounceDelay };
        _searchDebounceTimer.Tick += (_, _) => UpdateSearchedList();

        _serverListCache.AllServers.CollectionChanged += ServerListUpdated;

        _serverListCache.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(ServerListCache.Status):
                    this.RaisePropertyChanged(nameof(ListText));
                    this.RaisePropertyChanged(nameof(ListTextVisible));
                    this.RaisePropertyChanged(nameof(SpinnerVisible));
                    break;
            }
        };
    }

    private void FiltersOnFiltersUpdated()
    {
        UpdateSearchedList();
    }

    public override void Selected()
    {
        _serverListCache.RequestInitialUpdate();
    }

    public void RefreshPressed()
    {
        _serverListCache.RequestRefresh();
    }

    private void ServerListUpdated(object? sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        // The cache swapped in fresh server objects (a refresh happened); the cached
        // view-models point at stale data, so drop them and let UpdateSearchedList rebuild.
        _entryVms.Clear();

        Filters.UpdatePresentFilters(_serverListCache.AllServers);

        UpdateSearchedList();
    }

    private void UpdateSearchedList()
    {
        // Any pending debounced update is now satisfied by this run.
        _searchDebounceTimer.Stop();

        var sortList = new List<ServerStatusData>();

        foreach (var server in _serverListCache.AllServers)
        {
            if (!DoesSearchMatch(server))
                continue;

            sortList.Add(server);
        }

        Filters.ApplyFilters(sortList);

        sortList.Sort(ServerSortComparer.Instance);

        var entryVms = new List<ServerEntryViewModel>(sortList.Count);
        foreach (var server in sortList)
        {
            if (!_entryVms.TryGetValue(server, out var vm))
            {
                vm = new ServerEntryViewModel(_windowVm, server, _serverListCache, _windowVm.Cfg);
                _entryVms.Add(server, vm);
            }

            entryVms.Add(vm);
        }

        // Swap the whole list in one shot. Clearing and re-adding item by item fired a
        // collection-changed event per server, which froze the UI on large hubs.
        SearchedServers.Replace(entryVms);
    }

    private bool DoesSearchMatch(ServerStatusData data)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
            return true;

        return data.Name != null &&
               data.Name.Contains(SearchString, StringComparison.CurrentCultureIgnoreCase);
    }

    private sealed class ServerSortComparer : NotNullComparer<ServerStatusData>
    {
        public static readonly ServerSortComparer Instance = new();

        public override int Compare(ServerStatusData x, ServerStatusData y)
        {
            // Sort by player count descending.
            var res = x.PlayerCount.CompareTo(y.PlayerCount);
            if (res != 0)
                return -res;

            // Sort by name.
            res = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            if (res != 0)
                return res;

            // Sort by address.
            return string.Compare(x.Address, y.Address, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> that can swap its entire contents with a single
    /// reset notification, instead of one notification per item added or removed.
    /// </summary>
    public sealed class ServerEntryList : ObservableCollection<ServerEntryViewModel>
    {
        public void Replace(IReadOnlyList<ServerEntryViewModel> newItems)
        {
            Items.Clear();
            foreach (var item in newItems)
                Items.Add(item);

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
    }
}
