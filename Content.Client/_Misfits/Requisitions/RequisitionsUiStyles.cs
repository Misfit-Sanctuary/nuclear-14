using System.Linq;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._Misfits.Requisitions;

public static class RequisitionsUiStyles
{
    public const string ShopTabs = "N14RequisitionsShopTabs";
    public const string Card = "N14RequisitionsCard";
    public const string SidePanel = "N14RequisitionsSidePanel";

    public static readonly Color Green = Color.FromHex("#33FF33");
    public static readonly Color TextDim = Color.FromHex("#5fbf5f");
    public static readonly Color DimGreen = Color.FromHex("#1a5c1a");
    public static readonly Color DarkGreen = Color.FromHex("#0f3d0f");
    public static readonly Color Background = Color.FromHex("#0a0a0a");
    public static readonly Color PanelBackground = Color.FromHex("#0d160d");

    public static Stylesheet Create()
    {
        var baseRules = IoCManager.Resolve<IStylesheetManager>().SheetNano.Rules;
        return new Stylesheet(baseRules.Concat(CreateRules()).ToList());
    }

    public static StyleBox WindowPanel()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = Background,
            BorderColor = DimGreen,
            BorderThickness = new Thickness(2),
        };
    }

    public static StyleBox CardPanel()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = PanelBackground,
            BorderColor = DarkGreen,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 4,
            ContentMarginRightOverride = 4,
            ContentMarginTopOverride = 2,
            ContentMarginBottomOverride = 2,
        };
    }

    public static void ApplyQuantityButton(Button button)
    {
        button.StyleBoxOverride = Box(PanelBackground, Green, new Thickness(2));
        button.MinWidth = System.Math.Max(button.MinWidth, 36);
        button.MinHeight = System.Math.Max(button.MinHeight, 30);
        button.ModulateSelfOverride = Green;
    }

    private static StyleRule[] CreateRules()
    {
        var activeTab = Box(DarkGreen, Green, new Thickness(2));
        activeTab.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);
        activeTab.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);

        var inactiveTab = Box(Background, DimGreen, new Thickness(2));
        inactiveTab.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);
        inactiveTab.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);

        var panel = Box(Background, DimGreen, new Thickness(2));

        return
        [
            Element<TabContainer>()
                .Class(ShopTabs)
                .Prop(TabContainer.StylePropertyPanelStyleBox, panel)
                .Prop(TabContainer.StylePropertyTabStyleBox, activeTab)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, inactiveTab)
                .Prop(TabContainer.stylePropertyTabFontColor, Green)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, DimGreen),

            Element<PanelContainer>()
                .Class(Card)
                .Prop(PanelContainer.StylePropertyPanel, CardPanel()),

            Element<PanelContainer>()
                .Class(SidePanel)
                .Prop(PanelContainer.StylePropertyPanel, WindowPanel()),

            Element<PanelContainer>()
                .Class(DefaultWindow.StyleClassWindowPanel)
                .Prop(PanelContainer.StylePropertyPanel, WindowPanel()),

            Element<PanelContainer>()
                .Class(DefaultWindow.StyleClassWindowHeader)
                .Prop(PanelContainer.StylePropertyPanel, Box(DarkGreen, DimGreen, new Thickness(0, 0, 0, 2))),

            Element<Label>()
                .Class(DefaultWindow.StyleClassWindowTitle)
                .Prop(Label.StylePropertyFontColor, Green),
        ];
    }

    private static StyleBoxFlat Box(Color background, Color border, Thickness thickness)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = thickness,
        };
    }
}
