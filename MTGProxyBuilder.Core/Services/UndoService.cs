using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class UndoService
    {
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private const int MaxStackSize = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public event Action? StateChanged;

        /// <summary>
        /// Saves the current card list state onto the undo stack.
        /// Call this BEFORE making a change.
        /// </summary>
        public void SaveState(IList<CardModel> cards)
        {
            var snapshot = JsonConvert.SerializeObject(cards, Formatting.None,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            _undoStack.Push(snapshot);
            _redoStack.Clear(); // new action invalidates redo history

            // Trim stack if too large
            if (_undoStack.Count > MaxStackSize)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 0; i < MaxStackSize; i++)
                    _undoStack.Push(temp[MaxStackSize - 1 - i]);
            }

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Undoes the last operation. Returns the restored card list, or null if nothing to undo.
        /// Pass the CURRENT state so it can be pushed to redo.
        /// </summary>
        public List<CardModel>? Undo(IList<CardModel> currentCards)
        {
            if (_undoStack.Count == 0) return null;

            // Save current state to redo stack
            var currentSnapshot = JsonConvert.SerializeObject(currentCards, Formatting.None,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            _redoStack.Push(currentSnapshot);

            // Pop and restore
            var snapshot = _undoStack.Pop();
            StateChanged?.Invoke();
            return JsonConvert.DeserializeObject<List<CardModel>>(snapshot);
        }

        /// <summary>
        /// Redoes the last undone operation.
        /// </summary>
        public List<CardModel>? Redo(IList<CardModel> currentCards)
        {
            if (_redoStack.Count == 0) return null;

            var currentSnapshot = JsonConvert.SerializeObject(currentCards, Formatting.None,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            _undoStack.Push(currentSnapshot);

            var snapshot = _redoStack.Pop();
            StateChanged?.Invoke();
            return JsonConvert.DeserializeObject<List<CardModel>>(snapshot);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke();
        }
    }
}
