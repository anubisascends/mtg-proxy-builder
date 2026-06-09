# UI Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the AvalonDock-based layout with a standard menu bar, icon toolbar, project tab bar, and fixed sidebar with collapsible accordion sections.

**Architecture:** The MainWindow.xaml is rewritten from scratch with a Grid-based layout: menu bar (row 0), icon toolbar (row 1), tab bar (row 2), content area with canvas + sidebar (row 3), status bar (row 4). A new `SidebarSection` UserControl provides the accordion behavior. AvalonDock packages are removed. All panel content (Search, Card, Layout, etc.) moves from AvalonDock anchorables into SidebarSection instances.

**Tech Stack:** WPF, Segoe MDL2 Assets font for toolbar icons, existing MVVM commands

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `MTGProxyBuilder.UI/Controls/SidebarSection.xaml` | Create | Reusable accordion section: clickable header with chevron + collapsible content |
| `MTGProxyBuilder.UI/Controls/SidebarSection.xaml.cs` | Create | IsExpanded dependency property, toggle logic |
| `MTGProxyBuilder.UI/MainWindow.xaml` | Major rewrite | Menu bar + toolbar + tab bar + canvas/sidebar layout |
| `MTGProxyBuilder.UI/MainWindow.xaml.cs` | Major rewrite | Remove dock layout code, simplify event handlers |
| `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj` | Modify | Remove AvalonDock packages |
| `MTGProxyBuilder.Core/Services/AppSettingsService.cs` | Modify | Add sidebar expanded states |
| `MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs` | Modify | Load/save sidebar states |
| `MTGProxyBuilder.Tests/Integration/UiSmokeTests.cs` | Modify | Update for menu/toolbar/sidebar |

---

### Task 1: Create SidebarSection control

**Files:**
- Create: `MTGProxyBuilder.UI/Controls/SidebarSection.xaml`
- Create: `MTGProxyBuilder.UI/Controls/SidebarSection.xaml.cs`

- [ ] **Step 1: Create the XAML**

Create `MTGProxyBuilder.UI/Controls/SidebarSection.xaml`:

```xml
<UserControl x:Class="MTGProxyBuilder.UI.Controls.SidebarSection"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border BorderBrush="#3E3E42" BorderThickness="0,0,0,1">
        <StackPanel>
            <!-- Clickable header -->
            <Border x:Name="HeaderBorder" Background="#2D2D30" Padding="10,8" Cursor="Hand"
                    MouseLeftButtonUp="OnHeaderClick">
                <Border.Style>
                    <Style TargetType="Border">
                        <Style.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#3E3E42"/>
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name="ChevronText" Text="&#x25B8;" Foreground="#888" FontSize="10"
                               VerticalAlignment="Center" Margin="0,0,8,0" Width="10"/>
                    <TextBlock Grid.Column="1" x:Name="TitleText" Foreground="#CCC" FontSize="12"
                               VerticalAlignment="Center"/>
                </Grid>
            </Border>
            <!-- Collapsible content -->
            <Border x:Name="ContentBorder" Background="#2D2D30" Visibility="Collapsed">
                <ContentPresenter x:Name="SectionContent"/>
            </Border>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create the code-behind**

Create `MTGProxyBuilder.UI/Controls/SidebarSection.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class SidebarSection : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(SidebarSection),
                new PropertyMetadata("Section", OnHeaderChanged));

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(SidebarSection),
                new PropertyMetadata(false, OnIsExpandedChanged));

        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register(nameof(SectionBody), typeof(object), typeof(SidebarSection),
                new PropertyMetadata(null, OnSectionBodyChanged));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public object? SectionBody
        {
            get => GetValue(SectionContentProperty);
            set => SetValue(SectionContentProperty, value);
        }

        public SidebarSection()
        {
            InitializeComponent();
        }

        private void OnHeaderClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
                s.TitleText.Text = (string)e.NewValue;
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
            {
                bool expanded = (bool)e.NewValue;
                s.ContentBorder.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                s.ChevronText.Text = expanded ? "\u25BE" : "\u25B8";
            }
        }

        private static void OnSectionBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
                s.SectionContent.Content = e.NewValue;
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/Controls/SidebarSection.xaml MTGProxyBuilder.UI/Controls/SidebarSection.xaml.cs
git commit -m "feat: create SidebarSection accordion control"
```

---

### Task 2: Add sidebar state persistence to AppSettings

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/AppSettingsService.cs`

- [ ] **Step 1: Add sidebar state properties to AppSettings**

In `AppSettings` class, add after the last existing property:

```csharp
        [JsonProperty("sidebarSearchExpanded")]
        public bool SidebarSearchExpanded { get; set; } = true;

        [JsonProperty("sidebarImportExpanded")]
        public bool SidebarImportExpanded { get; set; }

        [JsonProperty("sidebarCardDetailsExpanded")]
        public bool SidebarCardDetailsExpanded { get; set; }

        [JsonProperty("sidebarLayoutExpanded")]
        public bool SidebarLayoutExpanded { get; set; }

        [JsonProperty("sidebarStorageExpanded")]
        public bool SidebarStorageExpanded { get; set; }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/AppSettingsService.cs
git commit -m "feat: add sidebar section expanded states to AppSettings"
```

---

### Task 3: Remove AvalonDock packages

**Files:**
- Modify: `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj`

- [ ] **Step 1: Remove AvalonDock package references**

In `MTGProxyBuilder.UI.csproj`, remove these two lines from the PackageReference ItemGroup:

```xml
    <PackageReference Include="Dirkster.AvalonDock" Version="4.74.1" />
    <PackageReference Include="Dirkster.AvalonDock.Themes.VS2013" Version="4.74.1" />
```

- [ ] **Step 2: Run dotnet restore**

Run: `dotnet restore`
Expected: Restore succeeds. (Build will FAIL at this point because MainWindow.xaml still references AvalonDock — that's expected and fixed in Task 4.)

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj
git commit -m "chore: remove AvalonDock NuGet packages"
```

---

### Task 4: Rewrite MainWindow.xaml

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml`

This is the largest task. The entire 1129-line XAML file is rewritten. The implementer must:

1. Read the CURRENT `MainWindow.xaml` fully to understand all the panel content
2. Read the CURRENT `MainWindow.xaml.cs` to understand event handlers and named elements
3. Rewrite the XAML with the new layout structure

- [ ] **Step 1: Read current files**

Read `MTGProxyBuilder.UI/MainWindow.xaml` and `MTGProxyBuilder.UI/MainWindow.xaml.cs` completely.

- [ ] **Step 2: Rewrite MainWindow.xaml**

Replace the entire file. The new structure must follow this layout:

**Window root Grid rows:**
- Row 0 (Auto): Menu bar
- Row 1 (Auto): Icon toolbar
- Row 2 (Auto): Project tab bar (visible when HasActiveProject)
- Row 3 (Auto): Update banner (visible when UpdateAvailable)
- Row 4 (*): Main content area — welcome screen OR canvas+sidebar
- Row 5 (Auto): Status bar

**Remove all AvalonDock namespaces and markup:**
- Remove `xmlns:avalonDock` and `xmlns:avalonTheme`
- Remove the entire `DockingManager` element
- Remove the `LayoutRoot`, `LayoutPanel`, `LayoutDocumentPane`, `LayoutAnchorablePane` tree

**Menu bar (Row 0):**
- `<Menu>` with dark theme styling (Background="#2D2D30", Foreground="#CCC")
- 5 top-level MenuItems: File, Edit, Cards, Tools, Help
- Each MenuItem binds to existing commands via `Command="{Binding ActiveProject.Inner.SaveProjectCommand}"` etc.
- File: NewProjectCommand, OpenProjectCommand, SaveProjectCommand, SaveProjectAsCommand, ---, ExportPdfCommand, ExportSvgCommand, ---, ExitCommand
- Edit: UndoCommand, RedoCommand, ---, ClearAllCardsCommand
- Cards: AddCardFromFileCommand, ImportDeckCommand (need to wire), ImportMpcFillXmlCommand (need to wire), ---, RemoveCardCommand
- Tools: ManageFrontArtLibraryCommand, OpenSettingsCommand
- Help: (no command yet — just static About text)
- Per-project commands bind through `ActiveProject.Inner` DataContext
- Global commands (New, Open, Art Library, Settings, Exit) bind directly to Shell

**Icon toolbar (Row 1):**
- `<ToolBarTray>` or a styled `<StackPanel>` with icon buttons
- Font family: `Segoe MDL2 Assets` for icon text
- Button style: 32x32, transparent bg, #CCC foreground, #3E3E42 hover, no border
- Groups separated by vertical `<Border Width="1" Background="#444"/>`
- Group 1: New (\uE7C3), Open (\uE8E5), Save (\uE74E)
- Group 2: Undo (\uE7A7), Redo (\uE7A6)
- Group 3: Add File (\uE710), Export PDF (\uE8A5)
- Group 4 (right-aligned): Art Library (\uE8B9), Settings (\uE713)
- Each button has a ToolTip with text + shortcut
- Per-project buttons bind through `ActiveProject.Inner`

**Tab bar (Row 2):**
- Keep the existing tab bar structure (ItemsControl with project tabs, + New, Open)
- Just move it from Row 0 to Row 2
- Visibility bound to HasActiveProject

**Content area (Row 4):**
- Welcome screen (visible when !HasActiveProject): keep existing
- When HasActiveProject: a Grid with two columns
  - Column 0 (*): Canvas area (ScrollViewer + GridEditorCanvas + zoom controls + render overlay)
  - Column 1 (300px): Sidebar
- Sidebar is a `<ScrollViewer>` containing a `<StackPanel>` of `SidebarSection` controls
- 5 sections: Search, Import, Card Details, Layout, Storage
- Content of each section is the SAME XAML that's currently inside the AvalonDock anchorables (the ScrollViewer > StackPanel content) — just moved into `SidebarSection.SectionBody`
- Bind `IsExpanded` on each section for persistence

**IMPORTANT — preserve these named elements** (used by code-behind):
- `ScryfallSearchBox` (TextBox for search — has KeyDown handler)
- `DeckImportUrlBox` (TextBox for deck URL — has KeyDown handler)
- `GridCanvas` (GridEditorCanvas — has event handlers)
- `CanvasScrollViewer` (ScrollViewer — zoom/pan handlers)
- `CanvasScale` (ScaleTransform — zoom)
- `ZoomLabel` (TextBlock — zoom percentage)
- `StatusLabel` / status bar elements
- All buttons with Click handlers that remain

**IMPORTANT — the Filter panel content** (currently in the "Filter" AvalonDock anchorable) should NOT become a sidebar section. The filter controls (FilterText, FilterRarity, FilterColor, SortBy, SortDescending) are project-level card filters — they should move into the Card Details section or be placed as a small filter strip above the canvas. The implementer should check with the current code to decide. The simplest approach: merge Filter into the bottom of the Card Details section under a "FILTER & SORT" heading.

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded (may have warnings about unused code-behind methods — fixed in Task 5).

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml
git commit -m "feat: rewrite MainWindow with menu bar, toolbar, and sidebar accordion"
```

---

### Task 5: Rewrite MainWindow.xaml.cs

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml.cs`

- [ ] **Step 1: Read and understand current code-behind**

The current file has:
- Zoom/pan logic (OnCanvasMouseWheel, ZoomIn/Out/Reset/Fit, SetZoom, pan handlers)
- Keyboard shortcuts (KeyDown handler)
- Tab click/close handlers
- Dock layout persistence (CapturePanelContents, LoadDockLayout, SaveDockLayout)
- Window closing handler
- Color picker click
- Search box Enter key handlers
- Canvas event wiring (CardDoubleClicked, CreateTokenRequested, etc.)

- [ ] **Step 2: Rewrite code-behind**

**Remove entirely:**
- `CapturePanelContents()` method
- `LoadDockLayout()` method
- `SaveDockLayout()` method
- `_panelContents` dictionary
- `DockLayoutPath` static field
- The `Loaded += (_, _) => LoadDockLayout()` line
- The `using AvalonDock.Layout;` and `using AvalonDock.Layout.Serialization;` usings

**Keep unchanged:**
- All zoom/pan methods (OnCanvasMouseWheel, ZoomIn/Out/Reset/Fit, SetZoom, pan handlers)
- Keyboard shortcuts handler
- Tab click/close handlers (OnTabClick, OnTabClose)
- Color picker handler (OnOutlineColorClick)
- Search box key handlers
- Canvas event wiring
- Scryfall double-click handler

**Modify OnWindowClosing:**
- Remove the `SaveDockLayout()` call
- Keep the `CanCloseApplication()` check

**Add sidebar state persistence:**
In the constructor, after setting DataContext, load sidebar states from settings and bind them. The simplest approach: wire `Loaded` to read settings and set `IsExpanded` on each `SidebarSection` by name. Wire `Closing` to save the states back.

```csharp
Loaded += (_, _) =>
{
    var settings = ((ShellViewModel)DataContext).AppSettings;
    SearchSection.IsExpanded = settings.SidebarSearchExpanded;
    ImportSection.IsExpanded = settings.SidebarImportExpanded;
    CardDetailsSection.IsExpanded = settings.SidebarCardDetailsExpanded;
    LayoutSection.IsExpanded = settings.SidebarLayoutExpanded;
    StorageSection.IsExpanded = settings.SidebarStorageExpanded;
};
```

And in `OnWindowClosing`:
```csharp
var settings = Shell.AppSettings;
settings.SidebarSearchExpanded = SearchSection.IsExpanded;
settings.SidebarImportExpanded = ImportSection.IsExpanded;
settings.SidebarCardDetailsExpanded = CardDetailsSection.IsExpanded;
settings.SidebarLayoutExpanded = LayoutSection.IsExpanded;
settings.SidebarStorageExpanded = StorageSection.IsExpanded;
Shell.SaveSettings();
```

This requires `ShellViewModel` to expose `AppSettings` and a `SaveSettings()` method.

- [ ] **Step 3: Expose AppSettings on ShellViewModel**

In `ShellViewModel.cs`, add:

```csharp
public AppSettings AppSettings => _appSettings.Settings;

public void SaveSettings() => _appSettings.Save();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml.cs MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs
git commit -m "feat: rewrite MainWindow code-behind, remove dock layout, add sidebar persistence"
```

---

### Task 6: Update UI smoke tests

**Files:**
- Modify: `MTGProxyBuilder.Tests/Integration/UiSmokeTests.cs`

- [ ] **Step 1: Update tests for new UI structure**

Update the test assertions:

**App_HasToolbarButtons** — Check for toolbar icon buttons. Since icons have no text `Content`, check by ToolTip or AutomationProperties.Name. The simplest approach: add `AutomationProperties.Name` to each toolbar button in the XAML (e.g. `AutomationProperties.Name="Save"`), then find by name.

**App_HasDockPanels** — Rename to `App_HasSidebarSections`. Look for "Search", "Import", "Card Details", "Layout", "Storage" text elements.

**App_HasMenuBar** — New test. Find menu items: "File", "Edit", "Cards", "Tools", "Help".

**App_CanSwitchTabs** — Remove (no AvalonDock tabs to switch). Or repurpose to verify sidebar sections can be expanded.

- [ ] **Step 2: Build and run tests**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Tests/Integration/UiSmokeTests.cs
git commit -m "feat: update UI smoke tests for menu bar, toolbar, and sidebar"
```

---

### Task 7: Final build, test, and cleanup

- [ ] **Step 1: Full rebuild**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 3: Verify AvalonDock is fully removed**

Run: `grep -r "avalonDock\|AvalonDock\|DockingManager\|LayoutAnchorable\|dock_layout" MTGProxyBuilder.UI/ --include="*.cs" --include="*.xaml"`
Expected: No matches.

- [ ] **Step 4: Commit plan doc**

```bash
git add docs/
git commit -m "docs: add UI restructure implementation plan"
```
