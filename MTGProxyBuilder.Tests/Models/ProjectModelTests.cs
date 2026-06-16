using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class ProjectModelTests
{
    [Fact]
    public void NewProject_HasDefaults()
    {
        var project = new ProjectModel();
        Assert.Equal("Untitled Project", project.ProjectName);
        Assert.NotNull(project.Cards);
        Assert.Empty(project.Cards);
        Assert.NotNull(project.PageSettings);
        Assert.NotNull(project.PrintSettings);
    }

    [Fact]
    public void TotalCards_CountsIndividualCards()
    {
        var project = new ProjectModel
        {
            Cards = new List<CardModel>
            {
                new() { Name = "A" },
                new() { Name = "B" },
                new() { Name = "C" }
            }
        };
        Assert.Equal(3, project.TotalCards);
    }

    [Fact]
    public void TotalCards_EmptyList_ReturnsZero()
    {
        var project = new ProjectModel();
        Assert.Equal(0, project.TotalCards);
    }

    [Fact]
    public void TotalPages_CalculatesCorrectly()
    {
        var project = new ProjectModel();
        // Default A4 with 63x88mm cards + 1.5mm bleed = ~9 per page
        for (int i = 0; i < 20; i++)
            project.Cards.Add(new CardModel { Quantity = 1 });

        int cardsPerPage = project.PageSettings.CardsPerPage;
        Assert.True(cardsPerPage > 0);

        int expected = (int)Math.Ceiling(20.0 / cardsPerPage);
        Assert.Equal(expected, project.TotalPages);
    }

    [Fact]
    public void TotalPages_EmptyProject_ReturnsZero()
    {
        var project = new ProjectModel();
        Assert.Equal(0, project.TotalPages);
    }

    [Fact]
    public void TotalPages_WithMultipleCards()
    {
        var project = new ProjectModel();
        for (int i = 0; i < 10; i++)
            project.Cards.Add(new CardModel { Name = $"Card {i}" });

        int cardsPerPage = project.PageSettings.CardsPerPage;
        int expected = (int)Math.Ceiling(10.0 / cardsPerPage);
        Assert.Equal(expected, project.TotalPages);
    }

    [Fact]
    public void PrinterProfileName_DefaultsToNull()
    {
        var project = new ProjectModel();
        Assert.Null(project.PrinterProfileName);
    }

    [Fact]
    public void PrinterProfileName_CanBeSet()
    {
        var project = new ProjectModel();
        project.PrinterProfileName = "Canon PIXMA";
        Assert.Equal("Canon PIXMA", project.PrinterProfileName);
    }
}
