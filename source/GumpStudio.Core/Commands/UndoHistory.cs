namespace GumpStudio.Core.Commands;

/// <summary>A reversible change to a document.</summary>
public interface IUndoableCommand
{
    /// <summary>Text shown next to Undo and Redo in the menu.</summary>
    string Description { get; }

    void Execute();

    void Undo();

    /// <summary>
    /// Tries to fold <paramref name="following"/> into this command.
    /// </summary>
    /// <returns>
    /// True when absorbed, in which case <paramref name="following"/> is not pushed
    /// separately.
    /// </returns>
    /// <remarks>
    /// This is what makes a drag or a run of arrow-key nudges land as one undo
    /// entry instead of hundreds. The original had no equivalent, which is a
    /// large part of why its undo behaviour felt arbitrary.
    /// </remarks>
    bool TryMerge(IUndoableCommand following) => false;
}

/// <summary>
/// The undo and redo history.
/// </summary>
/// <remarks>
/// <para>
/// Command-based rather than snapshot-based. The original deep-cloned every page
/// of the document on every action, from more than thirty call sites, and capped
/// the history by count alone — so memory scaled with document size times
/// history depth.
/// </para>
/// <para>
/// Both <see cref="Undo"/> and <see cref="Redo"/> are safe to call at any time.
/// The original relied on menu-item <c>Enabled</c> state as its only bound check,
/// and its <c>Redo</c> guard was off by one, so reaching the end of the history
/// through any path that bypassed the menu threw.
/// </para>
/// </remarks>
public sealed class UndoHistory
{
    private readonly List<IUndoableCommand> _commands = [];

    // One stamp per retained command, identifying the state that command
    // produces. Parallel to _commands, so trimming keeps them aligned.
    private readonly List<long> _stamps = [];
    private int _cursor;
    private long _sequence;
    private bool _isApplying;

    public UndoHistory(int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Capacity = capacity;
    }

    /// <summary>Maximum number of retained commands.</summary>
    public int Capacity { get; }

    /// <summary>Commands currently retained.</summary>
    public int Count => _commands.Count;

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor < _commands.Count;

    /// <summary>Description of the command <see cref="Undo"/> would reverse.</summary>
    public string? UndoDescription => CanUndo ? _commands[_cursor - 1].Description : null;

    /// <summary>Description of the command <see cref="Redo"/> would reapply.</summary>
    public string? RedoDescription => CanRedo ? _commands[_cursor].Description : null;

    /// <summary>
    /// Identifies the document state this history currently represents. Zero
    /// means nothing has been applied.
    /// </summary>
    /// <remarks>
    /// Exists so a caller can tell whether the document still matches what was
    /// last saved. The undo cursor alone cannot answer that: undoing a step and
    /// then applying a different change discards the redo tail and leaves the
    /// cursor at the same number over a different state. A stamp per command
    /// separates those, and survives the trimming <see cref="Capacity"/> forces.
    /// </remarks>
    public long StateId => _cursor == 0 ? 0 : _stamps[_cursor - 1];

    /// <summary>Raised after any change to the stack or the cursor.</summary>
    public event EventHandler? Changed;

    /// <summary>Runs a command and pushes it onto the history.</summary>
    public void Push(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_isApplying)
        {
            throw new InvalidOperationException(
                "A command cannot push another command while undo or redo is running.");
        }

        command.Execute();

        // Dropped before the merge check, which compares against whatever is
        // now on top.
        DropUndoneTail();

        if (_cursor > 0 && _commands[_cursor - 1].TryMerge(command))
        {
            // A merge still changes the document, so the state it names has to
            // be a new one.
            _stamps[_cursor - 1] = ++_sequence;

            OnChanged();

            return;
        }

        Append(command);

        OnChanged();
    }

    /// <summary>Reverses the most recent command. Does nothing when there is none.</summary>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _isApplying = true;

        try
        {
            _commands[--_cursor].Undo();
        }
        finally
        {
            _isApplying = false;
        }

        OnChanged();

        return true;
    }

    /// <summary>Reapplies the next command. Does nothing when there is none.</summary>
    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _isApplying = true;

        try
        {
            _commands[_cursor++].Execute();
        }
        finally
        {
            _isApplying = false;
        }

        OnChanged();

        return true;
    }

    /// <summary>Discards the whole history, for example after opening a file.</summary>
    public void Clear()
    {
        _commands.Clear();
        _stamps.Clear();
        _cursor = 0;

        OnChanged();
    }

    /// <summary>
    /// Groups everything pushed inside the scope into one undo entry.
    /// </summary>
    /// <remarks>
    /// Used for compound edits such as "align selection", which mutates many
    /// elements but should undo in a single step.
    /// </remarks>
    public CompositeScope BeginComposite(string description) => new(this, description);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Collects commands and pushes them as one on dispose.</summary>
    public sealed class CompositeScope : IDisposable
    {
        private readonly UndoHistory _stack;
        private readonly string _description;
        private readonly List<IUndoableCommand> _collected = [];
        private bool _disposed;

        internal CompositeScope(UndoHistory stack, string description)
        {
            _stack = stack;
            _description = description;
        }

        /// <summary>Runs a command now and defers pushing it until the scope closes.</summary>
        public void Run(IUndoableCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            ObjectDisposedException.ThrowIf(_disposed, this);

            command.Execute();
            _collected.Add(command);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_collected.Count == 0)
            {
                return;
            }

            // Already executed above, so push a pre-applied composite rather than
            // running everything a second time.
            _stack.PushExecuted(new CompositeCommand(_description, [.. _collected]));
        }
    }

    /// <summary>Pushes a command whose effect has already been applied.</summary>
    private void PushExecuted(IUndoableCommand command)
    {
        Append(command);

        OnChanged();
    }

    /// <summary>
    /// Records a command at the cursor, dropping anything undone and trimming
    /// to <see cref="Capacity"/>.
    /// </summary>
    private void Append(IUndoableCommand command)
    {
        DropUndoneTail();

        _commands.Add(command);
        _stamps.Add(++_sequence);
        _cursor++;

        if (_commands.Count > Capacity)
        {
            int excess = _commands.Count - Capacity;

            _commands.RemoveRange(0, excess);
            _stamps.RemoveRange(0, excess);
            _cursor -= excess;
        }
    }

    /// <summary>
    /// Discards anything that was undone, which a new change makes unreachable.
    /// </summary>
    private void DropUndoneTail()
    {
        if (_cursor >= _commands.Count)
        {
            return;
        }

        _commands.RemoveRange(_cursor, _commands.Count - _cursor);
        _stamps.RemoveRange(_cursor, _stamps.Count - _cursor);
    }
}

/// <summary>Several commands that undo and redo as one.</summary>
public sealed class CompositeCommand(string description, IReadOnlyList<IUndoableCommand> commands)
    : IUndoableCommand
{
    public string Description { get; } = description;

    public void Execute()
    {
        foreach (IUndoableCommand command in commands)
        {
            command.Execute();
        }
    }

    public void Undo()
    {
        // Reverse order, so each command sees the state it produced.
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            commands[i].Undo();
        }
    }
}
