using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CustomCardProjectTests
{
    [Fact]
    public void NewProject_HasDefaultValues()
    {
        var project = new CustomCardProject();
        Assert.Equal("Untitled Card", project.ProjectName);
        Assert.Equal(1500, project.CardWidthPx);
        Assert.Equal(2100, project.CardHeightPx);
        Assert.Equal("#000000", project.BackgroundColor);
        Assert.NotNull(project.Layers);
        Assert.Empty(project.Layers);
    }

    [Fact]
    public void NewProject_HasUniqueId()
    {
        var p1 = new CustomCardProject();
        var p2 = new CustomCardProject();
        Assert.NotEqual(p1.ProjectId, p2.ProjectId);
    }

    [Fact]
    public void Layers_MaintainOrder()
    {
        var project = new CustomCardProject();
        var layer1 = new ImageLayer { Name = "Bottom", ZOrder = 0 };
        var layer2 = new TextLayer { Name = "Top", ZOrder = 1 };
        project.Layers.Add(layer1);
        project.Layers.Add(layer2);

        Assert.Equal(2, project.Layers.Count);
        Assert.Equal("Bottom", project.Layers[0].Name);
        Assert.Equal("Top", project.Layers[1].Name);
    }

    [Fact]
    public void Layers_SupportMixedTypes()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new ImageLayer { Name = "Image" });
        project.Layers.Add(new TextLayer { Name = "Text" });

        Assert.IsType<ImageLayer>(project.Layers[0]);
        Assert.IsType<TextLayer>(project.Layers[1]);
    }

    [Fact]
    public void PropertyChanged_RaisedOnSetters()
    {
        var project = new CustomCardProject();
        var changedProps = new List<string>();
        project.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        project.ProjectName = "Test Card";
        project.BackgroundColor = "#FF0000";
        project.CardWidthPx = 500;
        project.CardHeightPx = 700;

        Assert.Contains("ProjectName", changedProps);
        Assert.Contains("BackgroundColor", changedProps);
        Assert.Contains("CardWidthPx", changedProps);
        Assert.Contains("CardHeightPx", changedProps);
    }
}
