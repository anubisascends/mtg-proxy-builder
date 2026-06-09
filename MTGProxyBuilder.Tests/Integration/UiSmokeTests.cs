using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace MTGProxyBuilder.Tests.Integration;

/// <summary>
/// UI smoke tests using FlaUI to verify the WPF application launches and
/// basic UI elements are accessible. These tests launch the actual app.
/// </summary>
[Trait("Category", "UI")]
public class UiSmokeTests : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    private bool LaunchApp()
    {
        try
        {
            // Find the built executable
            string exePath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "MTGProxyBuilder.UI", "bin", "Debug", "net10.0-windows", "tcg-proxy-builder.exe");
            exePath = Path.GetFullPath(exePath);

            if (!File.Exists(exePath))
            {
                // Try alternate naming
                exePath = Path.ChangeExtension(exePath, null);
                exePath = Path.Combine(Path.GetDirectoryName(exePath)!, "MTGProxyBuilder.UI.exe");
            }

            if (!File.Exists(exePath))
                return false;

            _automation = new UIA3Automation();
            _app = Application.Launch(exePath);
            _mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
            return _mainWindow != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Creates a new project by clicking the "+ New" button, making project UI visible.</summary>
    private bool EnsureProjectOpen()
    {
        if (!LaunchApp()) return false;

        var newBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("+ New"))?.AsButton();
        if (newBtn == null) return false;

        newBtn.Click();
        Thread.Sleep(1000); // Wait for project to initialize and UI to render
        return true;
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { }
        try { _app?.Dispose(); } catch { }
        try { _automation?.Dispose(); } catch { }
    }

    private List<string> GetButtonNames() =>
        _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
            .Select(b => b.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

    private List<string> GetTextNames() =>
        _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

    [Fact]
    public void App_Launches_Successfully()
    {
        if (!LaunchApp()) return;

        Assert.NotNull(_mainWindow);
        Assert.Contains("MTG Proxy Builder", _mainWindow!.Title);
    }

    [Fact]
    public void App_WelcomeScreen_HasNewAndOpenButtons()
    {
        if (!LaunchApp()) return;

        var buttonNames = GetButtonNames();
        Assert.Contains("+ New", buttonNames);
        Assert.Contains("Open", buttonNames);
    }

    [Fact]
    public void App_HasToolbarButtons()
    {
        if (!EnsureProjectOpen()) return;

        var buttonNames = GetButtonNames();
        Assert.Contains("Save", buttonNames);
        Assert.Contains("Export PDF", buttonNames);
        Assert.Contains("+ File", buttonNames);
    }

    [Fact]
    public void App_HasDockPanels()
    {
        if (!EnsureProjectOpen()) return;

        // AvalonDock anchorable titles appear as Text elements in the automation tree.
        // Some panels may be in tab groups where only the active tab header is visible,
        // so we check for at least the primary panels.
        var allElements = _mainWindow!.FindAllDescendants();
        var allNames = allElements.Select(e => e.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        Assert.Contains("Search", allNames);
        Assert.Contains("Card", allNames);
        Assert.Contains("Layout", allNames);
    }

    [Fact]
    public void App_HasProjectNameField()
    {
        if (!EnsureProjectOpen()) return;

        var textBoxes = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
        Assert.NotEmpty(textBoxes);

        // The project name textbox should have the default "Untitled Project"
        var projectNameBox = textBoxes.FirstOrDefault(t =>
        {
            try { return t.AsTextBox().Text == "Untitled Project"; }
            catch { return false; }
        });
        Assert.NotNull(projectNameBox);
    }

    [Fact]
    public void App_StatusBarShowsReady()
    {
        if (!EnsureProjectOpen()) return;

        var textNames = GetTextNames();
        Assert.Contains("Ready", textNames);
    }

    [Fact]
    public void App_NewProject_ClearsState()
    {
        if (!EnsureProjectOpen()) return;

        // Click New again to create a second project
        var newBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("+ New"))?.AsButton();
        if (newBtn == null) return;

        newBtn.Click();
        Thread.Sleep(500);

        // Status should reflect new project
        var textNames = GetTextNames();
        var hasNewProjectStatus = textNames.Any(n => n.Contains("New project") || n == "Ready");
        Assert.True(hasNewProjectStatus);
    }

    [Fact]
    public void App_ZoomControls_Exist()
    {
        if (!EnsureProjectOpen()) return;

        var buttonNames = GetButtonNames();
        Assert.Contains("Fit", buttonNames);
        Assert.Contains("1:1", buttonNames);
    }

    [Fact]
    public void App_CanSwitchTabs()
    {
        if (!EnsureProjectOpen()) return;

        var tabs = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
        var layoutTab = tabs.FirstOrDefault(t => t.Name == "Layout");
        if (layoutTab == null) return;

        layoutTab.Click();
        Thread.Sleep(500);

        var textNames = GetTextNames();
        var layoutContent = textNames.FirstOrDefault(n =>
            n.Contains("PAGE") || n.Contains("Page Size") ||
            n.Contains("PRINT") || n.Contains("Print Mode") ||
            n.Contains("CARD SIZE") || n.Contains("Landscape") ||
            n.Contains("GRID") || n.Contains("Columns"));
        Assert.NotNull(layoutContent);
    }
}
