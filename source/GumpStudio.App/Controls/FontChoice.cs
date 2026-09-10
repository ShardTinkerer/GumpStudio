using GumpStudio.Core.Elements;

namespace GumpStudio.App.Controls;

/// <summary>A font face, as a picker chooses it.</summary>
/// <remarks>
/// Its own file rather than beside <see cref="PickerEntry"/>, where it began:
/// that file is Avalonia-typed — a hue's ramp is a list of
/// <c>Avalonia.Media.Color</c> and a font's sample an
/// <c>Avalonia.Media.Imaging.Bitmap</c> — while this is a value the element model
/// and <see cref="PropertyRow"/> both speak. Splitting it is what lets a view
/// model reach a font choice without reaching a toolkit.
/// </remarks>
public readonly record struct FontChoice(GumpFontFamily Family, int Index);
