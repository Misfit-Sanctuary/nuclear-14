using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Misfits.Guidebook;

public static class GuidebookTheme
{
    public static readonly Color TitleColor = Color.FromHex("#efe1bf");
    public static readonly Color SectionColor = Color.FromHex("#ddc18f");
    public static readonly Color SubSectionColor = Color.FromHex("#caa468");
    public static readonly Color BodyTextColor = Color.FromHex("#f2f2f2");
    public static readonly Color MutedTextColor = Color.FromHex("#b5bdc9");
    public static readonly Color LinkColor = Color.FromHex("#80b6ff");
    public static readonly Color LinkHoverColor = Color.FromHex("#a9d0ff");
    public static readonly Color DividerColor = Color.FromHex("#5d4b31");
    public static readonly Color InfoBackground = Color.FromHex("#1b2530");
    public static readonly Color InfoBorder = Color.FromHex("#5b89b7");
    public static readonly Color WarningBackground = Color.FromHex("#362313");
    public static readonly Color WarningBorder = Color.FromHex("#d89c4a");
    public static readonly Color ExampleBackground = Color.FromHex("#1a1f27");
    public static readonly Color ExampleBorder = Color.FromHex("#59677a");
    public static readonly Color CodeBackground = Color.FromHex("#111418");
    public static readonly Color CodeBorder = Color.FromHex("#707988");
    public static readonly Color CodeTextColor = Color.FromHex("#d9dee7");

    public const string InlineAccentHex = "#caa468";
    public const string DefaultListBullet = "•";
    public const string DefaultLinkText = "View Full Wiki Page";

    public const float TitleBottomMargin = 10f;
    public const float SectionTitleBottomMargin = 8f;
    public const float SubSectionTitleBottomMargin = 6f;
    public const float SubtitleBottomMargin = 6f;
    public const float ParagraphBottomMargin = 10f;
    public const float SectionBottomMargin = 18f;
    public const float SubSectionBottomMargin = 12f;
    public const float ComponentBottomMargin = 12f;
    public const float DividerTopMargin = 2f;
    public const float DividerBottomMargin = 12f;
    public const float DividerThickness = 2f;
    public const float ListIndent = 16f;
    public const float ListItemBottomMargin = 4f;
    public const float BreakHeight = 8f;
    public const float AccentBarWidth = 4f;
    public const float CalloutPadding = 10f;

    public static readonly Thickness PagePadding = new(14, 12, 14, 18);

    public static StyleBoxFlat CreatePanel(Color background, Color border, float borderWidth = 1f)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(borderWidth),
        };
    }

    public static StyleBoxFlat CreateSolidPanel(Color background)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
        };
    }

    public static void ApplyPageTitle(Label label)
    {
        label.StyleClasses.Add("LabelHeadingBigger");
        label.FontColorOverride = TitleColor;
        label.HorizontalExpand = true;
        label.Margin = new Thickness(0, 0, 0, TitleBottomMargin);
    }

    public static void ApplySectionTitle(Label label)
    {
        label.StyleClasses.Add("LabelHeading");
        label.FontColorOverride = SectionColor;
        label.HorizontalExpand = true;
        label.Margin = new Thickness(0, 0, 0, SectionTitleBottomMargin);
    }

    public static void ApplySubSectionTitle(Label label)
    {
        label.StyleClasses.Add("LabelSubText");
        label.FontColorOverride = SubSectionColor;
        label.HorizontalExpand = true;
        label.Margin = new Thickness(0, 0, 0, SubSectionTitleBottomMargin);
    }

    public static void ApplyMutedLabel(Label label, float bottomMargin = 0f)
    {
        label.FontColorOverride = MutedTextColor;
        label.HorizontalExpand = true;
        label.Margin = new Thickness(0, 0, 0, bottomMargin);
    }

    public static void ApplyBodyLabel(RichTextLabel label, float bottomMargin = ParagraphBottomMargin)
    {
        label.HorizontalExpand = true;
        label.Margin = new Thickness(0, 0, 0, bottomMargin);
    }
}