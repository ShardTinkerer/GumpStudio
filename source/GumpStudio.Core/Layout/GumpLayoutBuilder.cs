using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Layout;

/// <summary>
/// Turns a document into the client's layout commands.
/// </summary>
/// <remarks>
/// <para>
/// The single place the rules every exporter shares are decided. Before this
/// existed each of them re-derived the lot: three separate page loops, three
/// copies of the radio-group tracker, three text-index allocators, and three
/// element switches with a <c>default</c> arm that silently skipped anything new.
/// The same defects then had to be found and fixed once per exporter.
/// </para>
/// <para>
/// It is the second real implementation of <see cref="IElementVisitor"/>, so
/// adding an element type breaks the build here rather than producing a gump with
/// the element missing.
/// </para>
/// </remarks>
public static class GumpLayoutBuilder
{
    /// <summary>Builds the layout for a document.</summary>
    public static GumpLayout Build(GumpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Emitter emitter = new();

        for (int page = 0; page < document.PageCount; page++)
        {
            emitter.BeginPage(page);

            foreach (Element element in document.Pages[page].Leaves())
            {
                emitter.Emit(element);
            }

            emitter.EndPage();
        }

        return new GumpLayout(document.Properties.Clone(), emitter.Commands, emitter.Texts);
    }

    /// <summary>Walks one document, accumulating commands and text slots.</summary>
    private sealed class Emitter : IElementVisitor
    {
        private readonly List<LayoutCommand> _commands = [];
        private readonly List<LayoutText> _texts = [];

        private int _page;
        private int _radioGroup = -1;
        private Element? _element;
        private GumpPoint _at;
        private bool _first;

        public IReadOnlyList<LayoutCommand> Commands => _commands;

        public IReadOnlyList<LayoutText> Texts => _texts;

        public void BeginPage(int page)
        {
            _page = page;

            // The client resets the current group on every page, so the tracker
            // resets with it. Carrying it across pages made the exporters skip
            // the group command for a page whose first radio happened to match
            // the last group used on the previous one, dropping every radio on
            // that page into group 0.
            _radioGroup = -1;

            _commands.Add(new PageCommand(Structural(), page));
        }

        public void EndPage()
        {
            // Only when a non-zero group was opened. An explicit `group 0` is
            // left unclosed, which is what the POL exporter has always done and
            // what its NoGroupMeansNoEndGroup test pins.
            if (_radioGroup > 0)
            {
                _commands.Add(new EndGroupCommand(Structural()));
            }
        }

        public void Emit(Element element)
        {
            _element = element;
            _at = element.GetAbsolutePosition();
            _first = true;

            // Before the element's own command, so a converter that writes a
            // comment ahead of the first command puts it above the group rather
            // than between the group and the radio it applies to.
            if (element is RadioElement radio && radio.GroupId != _radioGroup)
            {
                _radioGroup = radio.GroupId;

                _commands.Add(new GroupCommand(Origin(), radio.GroupId));
            }

            element.Accept(this);

            // Both attach to whichever element the client created last, so they
            // follow their own element immediately.
            if (element.TooltipClilocId != 0)
            {
                _commands.Add(new TooltipCommand(
                    Origin(), element.TooltipClilocId, element.TooltipArguments));
            }

            if (element.ItemPropertySerial != 0)
            {
                _commands.Add(new ItemPropertyCommand(Origin(), element.ItemPropertySerial));
            }
        }

        public void Visit(GroupElement element)
        {
            // Leaves() never yields one: a group is an editor construct with no
            // client representation, and its only effect is on its children's
            // absolute positions.
        }

        public void Visit(AlphaElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new CheckerTransCommand(
                Origin(), _at.X, _at.Y, element.Width, element.Height));
        }

        public void Visit(BackgroundElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new ResizePicCommand(
                Origin(), _at.X, _at.Y, element.GumpId, element.Width, element.Height));
        }

        public void Visit(TiledElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new GumpPicTiledCommand(
                Origin(), _at.X, _at.Y, element.Width, element.Height, element.GumpId));
        }

        public void Visit(ImageElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new GumpPicCommand(
                Origin(), _at.X, _at.Y, element.GumpId, element.Hue, element.PartialHue));
        }

        public void Visit(ItemElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new TilePicCommand(Origin(), _at.X, _at.Y, element.ItemId, element.Hue));
        }

        public void Visit(PicInPicElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new PicInPicCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.GumpId,
                element.SourceX,
                element.SourceY,
                element.Width,
                element.Height,
                element.Hue,
                element.PartialHue));
        }

        public void Visit(TileAsGumpElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new TileAsGumpPicCommand(
                Origin(), _at.X, _at.Y, element.ItemId, element.LinkId, element.ParamB, element.ParamC));
        }

        public void Visit(LabelElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            TextRef text = AddText(element.Text, TextRole.Label);

            _commands.Add(element.Cropped
                ? new CroppedTextCommand(
                    Origin(), _at.X, _at.Y, element.Width, element.Height, element.Hue, text)
                : new TextCommand(Origin(), _at.X, _at.Y, element.Hue, text));
        }

        public void Visit(TextEntryElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            TextRef text = AddText(element.InitialText, TextRole.Entry);

            _commands.Add(new TextEntryCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.Width,
                element.Height,
                element.Hue,
                element.EntryId,
                text,
                element.MaxLength));
        }

        public void Visit(HtmlElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            // A localised area names a cliloc id, so it allocates no text slot.
            if (element.ContentKind == HtmlContentKind.Html)
            {
                TextRef text = AddText(element.Html, TextRole.Html);

                _commands.Add(new HtmlGumpCommand(
                    Origin(),
                    _at.X,
                    _at.Y,
                    element.Width,
                    element.Height,
                    text,
                    element.ShowBackground,
                    element.ShowScrollbar));

                return;
            }

            _commands.Add(new XmfHtmlCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.Width,
                element.Height,
                element.ClilocId,
                element.ShowBackground,
                element.ShowScrollbar,
                element.Color,
                element.Arguments));
        }

        public void Visit(ButtonElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            ButtonTile? tile = element.TileId != 0
                ? new ButtonTile(element.TileId, element.TileHue, element.TileX, element.TileY)
                : null;

            _commands.Add(new ButtonCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.NormalId,
                element.PressedId,
                element.Kind,
                element.Param,
                tile,
                element.CodeBehind));
        }

        // Radio derives from Checkbox, so the two Visit overloads stay distinct
        // rather than relying on a switch whose case order decides the answer.
        public void Visit(RadioElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new RadioCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.UncheckedId,
                element.CheckedId,
                element.IsChecked,
                element.Value));
        }

        public void Visit(CheckboxElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            _commands.Add(new CheckboxCommand(
                Origin(),
                _at.X,
                _at.Y,
                element.UncheckedId,
                element.CheckedId,
                element.IsChecked,
                element.GroupId));
        }

        private TextRef AddText(string value, TextRole role)
        {
            _texts.Add(new LayoutText(value, role));

            return new TextRef(_texts.Count - 1);
        }

        /// <summary>An origin for the element being emitted, primary the first time.</summary>
        private CommandOrigin Origin()
        {
            CommandOrigin origin = new(
                _commands.Count,
                _page,
                _first,
                _element?.Name ?? string.Empty,
                _element?.Comment ?? string.Empty);

            _first = false;

            return origin;
        }

        private CommandOrigin Structural() => CommandOrigin.Structural(_commands.Count, _page);
    }
}
