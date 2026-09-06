using System;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;

namespace LwpTerm.App.ViewModels.Panels;

/// <summary>Backs the docked "Log" panel — a rolling view of recent log lines.</summary>
public sealed partial class LogViewModel : ObservableObject
{
    private const int MaxLines = 500;

    public LogViewModel()
    {
        InMemoryLogSink.Instance.Emitted += OnEmitted;
    }

    public ObservableCollection<string> Lines { get; } = new();

    [RelayCommand]
    private void Clear() => Lines.Clear();

    private void OnEmitted(object? sender, LogLine line)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(() =>
        {
            Lines.Add(line.Display);
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
        });
    }
}
