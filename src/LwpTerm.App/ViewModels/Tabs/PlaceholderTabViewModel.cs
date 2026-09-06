namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>
/// Temporary tab content used while the real protocol surfaces are being built
/// (milestones M2–M5). Shows which session was launched and its target.
/// </summary>
public sealed class PlaceholderTabViewModel : SessionTabViewModel
{
    public PlaceholderTabViewModel(string title, string protocol, string target)
        : base(title)
    {
        Protocol = protocol;
        Target = target;
        StatusText = $"{protocol} — not yet implemented";
        ToolTip = target;
        State = SessionTabState.Idle;
    }

    public string Protocol { get; }

    public string Target { get; }
}
