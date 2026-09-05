namespace GumpStudio.Core.Elements;

/// <summary>What a button does when clicked.</summary>
public enum ButtonKind
{
    /// <summary>Switches the gump to another page.</summary>
    Page = 0,

    /// <summary>Sends a response to the server.</summary>
    Reply = 1,
}

/// <summary>Which art a button shows in the designer.</summary>
public enum ButtonState
{
    Normal = 0,
    Pressed = 1,
}

/// <summary>A clickable button with separate normal and pressed art.</summary>
public sealed class ButtonElement : Element
{
    private int _normalId = 247;
    private int _pressedId = 248;
    private ButtonKind _kind = ButtonKind.Reply;
    private ButtonState _state = ButtonState.Normal;
    private int _param;
    private string _codeBehind = string.Empty;

    public override string TypeName => "Button";

    public int NormalId
    {
        get => _normalId;
        set => Set(ref _normalId, value);
    }

    public int PressedId
    {
        get => _pressedId;
        set => Set(ref _pressedId, value);
    }

    public ButtonKind Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    /// <summary>Which art the designer previews. Not part of the exported gump.</summary>
    public ButtonState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    /// <summary>The page number or reply id, depending on <see cref="Kind"/>.</summary>
    public int Param
    {
        get => _param;
        set => Set(ref _param, value);
    }

    /// <summary>Script fragment some exporters emit for this button's handler.</summary>
    public string CodeBehind
    {
        get => _codeBehind;
        set => Set(ref _codeBehind, value ?? string.Empty);
    }

    protected override void CopyTo(Element target)
    {
        ButtonElement clone = (ButtonElement)target;

        clone._normalId = _normalId;
        clone._pressedId = _pressedId;
        clone._kind = _kind;
        clone._state = _state;
        clone._param = _param;
        clone._codeBehind = _codeBehind;
    }

    protected override Element CreateInstance() => new ButtonElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A checkbox with separate checked and unchecked art.</summary>
public class CheckboxElement : Element
{
    private int _checkedId = 210;
    private int _uncheckedId = 209;
    private bool _isChecked;
    private int _groupId;

    public override string TypeName => "Checkbox";

    public int CheckedId
    {
        get => _checkedId;
        set => Set(ref _checkedId, value);
    }

    public int UncheckedId
    {
        get => _uncheckedId;
        set => Set(ref _uncheckedId, value);
    }

    public virtual bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    /// <summary>Response group, used by the server to bundle related controls.</summary>
    public int GroupId
    {
        get => _groupId;
        set => Set(ref _groupId, value);
    }

    protected override void CopyTo(Element target)
    {
        CheckboxElement clone = (CheckboxElement)target;

        clone._checkedId = _checkedId;
        clone._uncheckedId = _uncheckedId;
        clone._isChecked = _isChecked;
        clone._groupId = _groupId;
    }

    protected override Element CreateInstance() => new CheckboxElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }

    /// <summary>Sets the checked flag without the radio group's exclusivity rule.</summary>
    private protected void SetCheckedField(bool value) => Set(ref _isChecked, value, nameof(IsChecked));

    private protected bool CheckedField => _isChecked;
}

/// <summary>
/// A radio button: a checkbox where checking one clears its siblings in the
/// same group.
/// </summary>
public sealed class RadioElement : CheckboxElement
{
    private int _value;

    public override string TypeName => "Radio";

    /// <summary>The value reported to the server when this option is selected.</summary>
    public int Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Checking clears every other radio in the same group. The original walked
    /// the parent on every set and threw when the element had no parent yet,
    /// which happened during load; here an unparented radio simply sets itself.
    /// </remarks>
    public override bool IsChecked
    {
        get => CheckedField;
        set
        {
            SetCheckedField(value);

            if (!value || Parent is null)
            {
                return;
            }

            foreach (Element sibling in Parent.Descendants())
            {
                if (!ReferenceEquals(sibling, this)
                    && sibling is RadioElement radio
                    && radio.GroupId == GroupId)
                {
                    radio.SetCheckedField(false);
                }
            }
        }
    }

    protected override void CopyTo(Element target)
    {
        base.CopyTo(target);

        ((RadioElement)target)._value = _value;
    }

    protected override Element CreateInstance() => new RadioElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A single-line text input.</summary>
public sealed class TextEntryElement : ResizableElement
{
    private string _initialText = string.Empty;
    private int _hue;
    private int _entryId;
    private int _maxLength;

    public TextEntryElement() => SetInitialSize(100, 20);

    public override string TypeName => "TextEntry";

    /// <summary>Text the field starts out containing.</summary>
    public string InitialText
    {
        get => _initialText;
        set => Set(ref _initialText, value ?? string.Empty);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    /// <summary>The id the server uses to read this field back.</summary>
    public int EntryId
    {
        get => _entryId;
        set => Set(ref _entryId, value);
    }

    /// <summary>Maximum accepted length, or 0 for unlimited.</summary>
    public int MaxLength
    {
        get => _maxLength;
        set => Set(ref _maxLength, value);
    }

    protected override void CopyTo(Element target)
    {
        TextEntryElement clone = (TextEntryElement)target;

        clone._initialText = _initialText;
        clone._hue = _hue;
        clone._entryId = _entryId;
        clone._maxLength = _maxLength;
    }

    protected override Element CreateInstance() => new TextEntryElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}
