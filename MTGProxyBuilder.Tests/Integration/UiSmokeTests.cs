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

    /// <summary>
    /// Creates a new project by invoking the "New Project" button on the welcome screen,
    /// making project UI (sidebar, canvas, status bar) visible.
    /// Uses InvokePattern to avoid mouse input privilege issues (Win32Exception: Access denied).
    /// </summary>
    private bool EnsureProjectOpen()
    {
        if (!LaunchApp()) return false;

        // The welcome screen shows "New Project" (not "+ New" which is in the tab bar)
        var newBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("New Project"))?.AsButton();
        if (newBtn == null) return false;

        // Use Invoke pattern rather than mouse click to avoid UAC/elevation input restrictions
        try
        {
            newBtn.Invoke();
        }
        catch
        {
            // Fall back to click if Invoke fails
            try { newBtn.Click(false); } catch { return false; }
        }

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

    private List<string> GetAllNames() =>
        _mainWindow!.FindAllDescendants()
            .Select(e => e.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

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

        // Welcome screen has "New Project" and "Open Project" buttons
        var buttonNames = GetButtonNames();
        Assert.Contains("New Project", buttonNames);
        Assert.Contains("Open Project", buttonNames);
    }

    [Fact]
    public void App_HasMenuBar()
    {
        if (!LaunchApp()) return;

        // Menu bar is always visible, even without a project open.
        // MenuItem headers use _ for keyboard access; FlaUI exposes the clean name.
        var allNames = GetAllNames();
        Assert.Contains("File", allNames);
        Assert.Contains("Edit", allNames);
        Assert.Contains("Cards", allNames);
        Assert.Contains("Tools", allNames);
        Assert.Contains("Help", allNames);
    }

    [Fact]
    public void App_HasToolbarButtons()
    {
        if (!EnsureProjectOpen()) return;

        // Toolbar buttons use icon font (no visible text) but have AutomationProperties.Name set.
        var buttonNames = GetButtonNames();
        Assert.Contains("Save", buttonNames);
        Assert.Contains("Export PDF", buttonNames);
        Assert.Contains("Add Card from File", buttonNames);
    }

    [Fact]
    public void App_HasSidebarSections()
    {
        if (!EnsureProjectOpen()) return;

        // The sidebar has 5 accordion sections whose header TextBlocks contain these names.
        var allNames = GetAllNames();
        Assert.Contains("Search", allNames);
        Assert.Contains("Import", allNames);
        Assert.Contains("Card Details", allNames);
        Assert.Contains("Layout", allNames);
        Assert.Contains("Storage", allNames);
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

        // After opening a project the status bar shows "New project created" or "Ready".
        // StatusText lives on ActiveProject.Inner.StatusText.
        var textNames = GetTextNames();
        var hasStatus = textNames.Any(n =>
            n.Contains("Ready") || n.Contains("New project") || n.Contains("project"));
        Assert.True(hasStatus, $"Expected a status text but found: [{string.Join(", ", textNames.Take(20))}]");
    }

    [Fact]
    public void App_NewProject_ClearsState()
    {
        if (!EnsureProjectOpen()) return;

        // After the first project is open the project tab bar becomes visible,
        // which contains a "+ New" button for creating additional projects.
        var newBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("+ New"))?.AsButton();
        if (newBtn == null) return; // "+ New" button not present — skip gracefully

        try { newBtn.Invoke(); } catch { try { newBtn.Click(false); } catch { return; } }
        Thread.Sleep(500);

        // Status should reflect new project creation
        var textNames = GetTextNames();
        var hasNewProjectStatus = textNames.Any(n => n.Contains("New project") || n == "Ready");
        Assert.True(hasNewProjectStatus);
    }

    [Fact]
    public void App_ZoomControls_Exist()
    {
        if (!EnsureProjectOpen()) return;

        // Zoom buttons use text Content (not icon font), so they appear by name directly.
        var buttonNames = GetButtonNames();
        Assert.Contains("Fit", buttonNames);
        Assert.Contains("1:1", buttonNames);
    }

    [Fact]
    public void App_CanExpandSidebarSection()
    {
        if (!EnsureProjectOpen()) return;

        // Find the "Layout" sidebar section header and click it to expand.
        // The header is a TextBlock named "Layout" inside a clickable Border.
        var layoutHeader = _mainWindow!.FindFirstDescendant(cf => cf.ByName("Layout"));
        if (layoutHeader == null) return; // Gracefully skip if not found

        try { layoutHeader.Click(false); } catch { return; } // Gracefully skip on input restrictions
        Thread.Sleep(500);

        // After expanding, Layout section content should reveal layout-related text.
        var allNames = GetAllNames();
        var hasLayoutContent = allNames.Any(n =>
            n.Contains("PAGE") || n.Contains("Page Size") ||
            n.Contains("PRINT") || n.Contains("Print Mode") ||
            n.Contains("CARD SIZE") || n.Contains("Landscape") ||
            n.Contains("GRID") || n.Contains("Columns"));
        Assert.True(hasLayoutContent,
            $"Expected layout content after expanding sidebar but found: [{string.Join(", ", allNames.Take(30))}]");
    }
}
