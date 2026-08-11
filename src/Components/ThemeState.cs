using MudBlazor;

namespace Lassie.Components;

// Scoped (one instance per Blazor Server circuit/session) — matches the plan's
// explicit "in-session only, no persistence across browser restarts" decision.
public class ThemeState
{
    public static readonly MudTheme Theme = new();

    private bool _isDarkMode;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value)
            {
                return;
            }

            _isDarkMode = value;
            OnChange?.Invoke();
        }
    }

    public event Action? OnChange;
}
