using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class UndoServiceTests
{
    private static List<CardModel> MakeCards(params string[] names)
    {
        return names.Select(n => new CardModel { Name = n }).ToList();
    }

    [Fact]
    public void Initially_CannotUndoOrRedo()
    {
        var svc = new UndoService();
        Assert.False(svc.CanUndo);
        Assert.False(svc.CanRedo);
    }

    [Fact]
    public void SaveState_EnablesUndo()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));
        Assert.True(svc.CanUndo);
        Assert.False(svc.CanRedo);
    }

    [Fact]
    public void Undo_RestoresPreviousState()
    {
        var svc = new UndoService();
        var original = MakeCards("A", "B");
        svc.SaveState(original);

        var current = MakeCards("A", "B", "C");
        var restored = svc.Undo(current);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        Assert.Equal("A", restored[0].Name);
        Assert.Equal("B", restored[1].Name);
    }

    [Fact]
    public void Undo_EnablesRedo()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));
        svc.Undo(MakeCards("A", "B"));

        Assert.True(svc.CanRedo);
    }

    [Fact]
    public void Redo_RestoresUndoneState()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));

        var afterChange = MakeCards("A", "B");
        svc.Undo(afterChange);

        var redone = svc.Redo(MakeCards("A"));
        Assert.NotNull(redone);
        Assert.Equal(2, redone!.Count);
    }

    [Fact]
    public void SaveState_ClearsRedoStack()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));
        svc.Undo(MakeCards("A", "B"));
        Assert.True(svc.CanRedo);

        svc.SaveState(MakeCards("A", "C")); // New action
        Assert.False(svc.CanRedo);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsNull()
    {
        var svc = new UndoService();
        var result = svc.Undo(MakeCards("A"));
        Assert.Null(result);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsNull()
    {
        var svc = new UndoService();
        var result = svc.Redo(MakeCards("A"));
        Assert.Null(result);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));
        svc.SaveState(MakeCards("B"));
        svc.Undo(MakeCards("C"));

        svc.Clear();
        Assert.False(svc.CanUndo);
        Assert.False(svc.CanRedo);
    }

    [Fact]
    public void StackLimit_DoesNotExceed50()
    {
        var svc = new UndoService();
        for (int i = 0; i < 60; i++)
            svc.SaveState(MakeCards($"Card{i}"));

        // Should be able to undo 50 times
        int undoCount = 0;
        while (svc.CanUndo)
        {
            svc.Undo(MakeCards("current"));
            undoCount++;
        }
        Assert.Equal(50, undoCount);
    }

    [Fact]
    public void StateChanged_FiresOnSaveState()
    {
        var svc = new UndoService();
        int fireCount = 0;
        svc.StateChanged += () => fireCount++;

        svc.SaveState(MakeCards("A"));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void StateChanged_FiresOnUndo()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("A"));

        int fireCount = 0;
        svc.StateChanged += () => fireCount++;
        svc.Undo(MakeCards("B"));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void MultipleUndoRedo_MaintainsCorrectOrder()
    {
        var svc = new UndoService();
        svc.SaveState(MakeCards("v1"));
        svc.SaveState(MakeCards("v1", "v2"));
        svc.SaveState(MakeCards("v1", "v2", "v3"));

        // Undo twice
        var r1 = svc.Undo(MakeCards("v1", "v2", "v3", "v4"));
        Assert.Equal(3, r1!.Count);

        var r2 = svc.Undo(MakeCards("v1", "v2", "v3"));
        Assert.Equal(2, r2!.Count);

        // Redo once
        var r3 = svc.Redo(MakeCards("v1", "v2"));
        Assert.Equal(3, r3!.Count);
    }

    [Fact]
    public void CardProperties_PreservedThroughSerialization()
    {
        var svc = new UndoService();
        var cards = new List<CardModel>
        {
            new()
            {
                Name = "Test Card",
                ManaCost = "{2}{U}",
                CMC = 3,
                TypeLine = "Creature — Wizard",
                Rarity = "rare",
                Quantity = 4
            }
        };
        svc.SaveState(cards);

        var restored = svc.Undo(new List<CardModel>());
        Assert.NotNull(restored);
        Assert.Single(restored!);
        Assert.Equal("Test Card", restored[0].Name);
        Assert.Equal("{2}{U}", restored[0].ManaCost);
        Assert.Equal(3f, restored[0].CMC);
        Assert.Equal("Creature — Wizard", restored[0].TypeLine);
        Assert.Equal("rare", restored[0].Rarity);
        Assert.Equal(4, restored[0].Quantity);
    }
}
