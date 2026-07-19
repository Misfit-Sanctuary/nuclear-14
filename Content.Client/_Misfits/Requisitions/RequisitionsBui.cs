using System.Linq;
using System.Numerics;
using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.Shared._Misfits.Requisitions.Components.RequisitionsElevatorMode;

namespace Content.Client._Misfits.Requisitions;

[UsedImplicitly]
public sealed class RequisitionsBui : BoundUserInterface
{
    private const float CategoryMinWidth = 180f;
    private const float CategoryPanelPadding = 12f;
    private const string PrewarMoneyProto = "N14CurrencyPrewar";

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;
    private readonly Dictionary<CartKey, int> _cart = new();
    private readonly Dictionary<CartKey, RequisitionsProductCard> _productCards = new();
    private readonly Dictionary<string, string> _renderSigs = new();

    private RequisitionsComputerComponent? _state;
    private RequisitionsWindow? _window;
    private int? _selectedCategory;
    private bool _confirmLower;

    public RequisitionsBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _sprite = EntMan.System<SpriteSystem>();
    }

    protected override void Open()
    {
        base.Open();
        Refresh();
    }

    private void EnsureWindow()
    {
        if (_window != null)
            return;

        _window = this.CreateWindow<RequisitionsWindow>();
        _renderSigs.Clear();

        _window.SearchBar.OnTextChanged += _ => RefreshVisibleEntries();
        _window.PlatformButton.OnPressed += _ => PressPlatformButton();
        _window.ClearCartButton.OnPressed += _ =>
        {
            _cart.Clear();
            RefreshVisibleEntries();
        };
        _window.BuyButton.OnPressed += _ => BuyCart();
        _window.SellRefreshButton.OnPressed += _ => SendMessage(new RequisitionsRefreshMsg());
        _window.SellSearchBar.OnTextChanged += _ => UpdateSellTab();
        _window.PrintHistoryButton.OnPressed += _ => SendMessage(new RequisitionsPrintHistoryMsg());
        _window.WithdrawButton.OnPressed += _ => SendMessage(new RequisitionsWithdrawStorageMsg());
    }

    public void Refresh()
    {
        EnsureWindow();
        RefreshShop();

        if (_window is { IsOpen: false })
            _window.OpenCentered();
    }

    private void RefreshShop()
    {
        _entities.TryGetComponent(Owner, out _state);

        if (_state is { } computer &&
            _selectedCategory is { } selectedCategory &&
            selectedCategory >= computer.Categories.Count)
        {
            _selectedCategory = null;
        }

        UpdateLinkStatus();
        UpdatePlatformButtons();
        UpdateBalance();
        UpdateSellTab();
        UpdateStorage();
        _window?.SetPlatformBusy(_state?.BusyStart, _state?.BusyEnd);
        PopulateCategories();
        RefreshProducts();
        RefreshCart();
        PopulatePendingOrders();
        UpdateHistory();
        UpdateBounties();
    }

    private void RefreshProducts()
    {
        if (_window == null)
            return;

        var search = _window.SearchBar.Text.Trim();
        var sig = $"{_selectedCategory}|{search}|{CategoriesSignature()}|{DictSignature(_state?.Purchased)}";
        if (RenderChanged("products", sig))
            PopulateProducts();
        else
            UpdateVisibleProductCards();
    }

    private string CategoriesSignature()
    {
        if (_state is not { } computer)
            return "x";

        var sb = new System.Text.StringBuilder();
        foreach (var category in computer.Categories)
        {
            sb.Append(category.Name);
            sb.Append(':');
            sb.Append(category.Entries.Count);
            sb.Append(';');
        }

        return sb.ToString();
    }

    private static string DictSignature(Dictionary<string, int>? dict)
    {
        if (dict == null || dict.Count == 0)
            return "x";

        var sb = new System.Text.StringBuilder();
        foreach (var (key, value) in dict)
        {
            sb.Append(key);
            sb.Append('=');
            sb.Append(value);
            sb.Append(';');
        }

        return sb.ToString();
    }

    private void UpdateStorage()
    {
        if (_window == null)
            return;

        var storage = _state?.Storage;
        var sig = $"{_state is { Linked: true }}|{_state is { Full: true }}|{DictSignature(storage)}";
        if (!RenderChanged("storage", sig))
            return;

        _window.StorageContainer.DisposeAllChildren();

        if (storage == null || storage.Count == 0)
        {
            _window.StorageStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-storage-empty")));
            _window.WithdrawButton.Disabled = true;
            return;
        }

        _window.StorageStatusLabel.SetMessage(FormattedMessage.FromUnformatted(string.Empty));
        var canWithdraw = _state is { Linked: true, Full: false };
        _window.WithdrawButton.Disabled = !canWithdraw;
        foreach (var (proto, count) in storage)
            _window.StorageContainer.AddChild(BuildStorageRow(proto, count, canWithdraw));
    }

    private Control BuildStorageRow(string proto, int count, bool canWithdraw)
    {
        var name = _prototypes.TryIndex<EntityPrototype>(proto, out var p) ? p.Name : proto;
        var markup = Loc.GetString("n14-requisitions-storage-item", ("item", name), ("count", count));

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        row.AddChild(MakeIcon(proto, 32));
        row.AddChild(new Control { MinWidth = 6 });

        var label = new RichTextLabel { VerticalAlignment = Control.VAlignment.Center, HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow(markup));
        row.AddChild(label);

        row.TooltipSupplier = _ => BuildCrateTooltip(proto);
        row.MouseFilter = Control.MouseFilterMode.Pass;

        var spin = new SpinBox { Value = count, MinWidth = 90, VerticalAlignment = Control.VAlignment.Center };
        spin.IsValid = i => i >= 1 && i <= count;
        spin.InitDefaultButtons();
        row.AddChild(spin);
        row.AddChild(new Control { MinWidth = 6 });

        var button = new Button
        {
            Text = Loc.GetString("n14-requisitions-storage-bring-up"),
            Disabled = !canWithdraw,
            VerticalAlignment = Control.VAlignment.Center,
        };
        RequisitionsUiStyles.ApplyQuantityButton(button);
        button.OnPressed += _ => SendMessage(new RequisitionsWithdrawStorageMsg { Proto = proto, Amount = spin.Value });
        row.AddChild(button);

        return row;
    }

    private void UpdateBounties()
    {
        if (_window == null)
            return;

        var bounties = _state?.Bounties;
        var randomRequests = _state?.RandomRequests;
        var now = _timing.CurTime;
        var sig = (bounties == null
            ? "x"
            : $"{string.Join(";", bounties.Select(b => $"{b.Id}|{b.Amount}|{b.Reward}|{b.RewardCrate}"))}#{string.Join(",", _state?.CompletedBounties ?? new List<string>())}#{DictSignature(_state?.BountyProgress)}")
            + "|" + (randomRequests == null
                ? "x"
                : string.Join(";", randomRequests.Select(s => s.Request is { } r
                    ? $"{string.Join(",", r.Targets.Select(t => $"{t.IsReagent}|{t.TargetId}|{t.Amount}|{t.Progress}"))}|{r.Score}|{DictSignature(r.RewardItems)}|{CountdownBucket(r.RerollAvailableAt, now)}"
                    : $"empty|{CountdownBucket(s.NextRollAt, now)}")));
        if (!RenderChanged("bounties", sig))
            return;

        _window.BountiesContainer.DisposeAllChildren();

        var hasBounties = bounties is { Count: > 0 };
        var hasRandomRequests = randomRequests is { Count: > 0 };

        if (!hasBounties && !hasRandomRequests)
        {
            _window.BountiesStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-bounties-empty")));
            return;
        }

        _window.BountiesStatusLabel.SetMessage(FormattedMessage.FromUnformatted(string.Empty));

        if (hasBounties)
        {
            var completed = _state?.CompletedBounties ?? new List<string>();
            var progress = _state?.BountyProgress ?? new Dictionary<string, int>();
            foreach (var bounty in bounties!)
            {
                var isDone = completed.Contains(bounty.Id);
                var borderColor = isDone ? RequisitionsUiStyles.DimGreen : RequisitionsUiStyles.DarkGreen;
                var card = BuildBountyCard(bounty, isDone, progress.GetValueOrDefault(bounty.Id));
                _window.BountiesContainer.AddChild(WrapCard(card, borderColor));
            }
        }

        if (hasRandomRequests)
        {
            if (hasBounties)
                _window.BountiesContainer.AddChild(BuildDivider());

            _window.BountiesContainer.AddChild(new Label
            {
                Text = Loc.GetString("n14-requisitions-random-requests-title"),
                FontColorOverride = RequisitionsUiStyles.Green,
            });

            for (var i = 0; i < randomRequests!.Count; i++)
            {
                var slot = randomRequests[i];
                var borderColor = slot.Request is { IsHard: true } ? RequisitionsUiStyles.Green : RequisitionsUiStyles.DarkGreen;
                _window.BountiesContainer.AddChild(WrapCard(BuildRandomRequestRow(i, slot), borderColor));
            }
        }
    }

    private static Control WrapCard(Control content, Color borderColor)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = RequisitionsUiStyles.PanelBackground,
                BorderColor = borderColor,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            },
            HorizontalExpand = true,
        };
        panel.AddChild(content);

        var wrapper = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        wrapper.AddChild(panel);
        wrapper.AddChild(new Control { MinHeight = 6 });
        return wrapper;
    }

    private Control BuildDivider()
    {
        return new PanelContainer
        {
            PanelOverride = RequisitionsUiStyles.CardPanel(),
            MinHeight = 2,
            Margin = new Thickness(0, 6),
        };
    }

    private Control BuildBountyCard(RequisitionsBounty bounty, bool isDone, int progress)
    {
        var name = bounty.Name is { } loc && Loc.TryGetString(loc, out var localized)
            ? localized
            : _prototypes.TryIndex<EntityPrototype>(bounty.Item, out var itemProto)
                ? itemProto.Name
                : bounty.Item.Id;

        var markup = isDone
            ? Loc.GetString("n14-requisitions-bounty-row-done", ("amount", bounty.Amount), ("item", name))
            : Loc.GetString("n14-requisitions-bounty-row", ("done", progress), ("amount", bounty.Amount), ("item", name));

        var requestChips = BuildChipWrap();
        requestChips.AddChild(BuildItemChip(bounty.Item.Id, false, markup));

        var rewardContent = bounty.RewardCrate is { } crate
            ? BuildCrateContentsChips(crate.Id) ?? BuildItemChip(crate.Id, false,
                _prototypes.TryIndex<EntityPrototype>(crate, out var crateProto) ? crateProto.Name : crate.Id)
            : BuildCashRewardChip(bounty.Reward);

        return BuildTwoSectionCard(requestChips, null, rewardContent);
    }

    private Control BuildRandomRequestRow(int slotIndex, RequisitionsRandomSlot slot)
    {
        if (slot.Request is not { } request || request.Targets.Count == 0)
        {
            var remaining = slot.NextRollAt - _timing.CurTime;
            var waitText = remaining > TimeSpan.Zero
                ? Loc.GetString("n14-requisitions-random-request-refilling", ("time", FormatCountdown(remaining)))
                : Loc.GetString("n14-requisitions-random-request-refilling-soon");

            var emptyLabel = new RichTextLabel { VerticalAlignment = Control.VAlignment.Center, HorizontalExpand = true };
            emptyLabel.SetMessage(FormattedMessage.FromUnformatted(waitText));

            var emptyRow = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 2),
            };
            emptyRow.AddChild(emptyLabel);
            return emptyRow;
        }

        var requestChips = BuildChipWrap();

        foreach (var target in request.Targets)
        {
            var iconProto = target.TargetId;
            string name;
            if (target.IsReagent)
            {
                name = _prototypes.TryIndex<ReagentPrototype>(target.TargetId, out var reagentProto) ? reagentProto.LocalizedName : target.TargetId;
            }
            else if (_prototypes.TryIndex<EntityPrototype>(target.TargetId, out var itemProto))
            {
                name = itemProto.Name;
            }
            else if (_prototypes.TryIndex<StackPrototype>(target.TargetId, out var stackProto))
            {
                name = Loc.GetString(stackProto.Name);
                iconProto = stackProto.Spawn;
            }
            else
            {
                name = target.TargetId;
            }

            var markup = Loc.GetString("n14-requisitions-random-request-row",
                ("done", target.Progress),
                ("amount", target.Amount),
                ("item", name));
            requestChips.AddChild(BuildItemChip(iconProto, target.IsReagent, markup));
        }

        var rewardContent = request.DirectBudget
            ? BuildBudgetRewardChip(request.Score)
            : request.RewardItems.Count > 0
                ? BuildRewardItemChips(request.RewardItems)
                : new Control();

        var canReroll = _timing.CurTime >= request.RerollAvailableAt;
        var rerollButton = new Button
        {
            Text = canReroll
                ? Loc.GetString("n14-requisitions-random-request-reroll")
                : Loc.GetString("n14-requisitions-random-request-reroll-wait", ("time", FormatCountdown(request.RerollAvailableAt - _timing.CurTime))),
            Disabled = !canReroll,
            VerticalAlignment = Control.VAlignment.Center,
        };
        RequisitionsUiStyles.ApplyQuantityButton(rerollButton);
        rerollButton.OnPressed += _ => SendMessage(new RequisitionsRerollRequestMsg(slotIndex));

        return BuildTwoSectionCard(requestChips, rerollButton, rewardContent);
    }

    private Control BuildTwoSectionCard(Control requestContent, Button? rerollButton, Control rewardContent)
    {
        var outer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        var requestHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };
        requestHeader.AddChild(new Label
        {
            Text = Loc.GetString("n14-requisitions-section-request"),
            FontColorOverride = RequisitionsUiStyles.TextDim,
            HorizontalExpand = true,
        });
        if (rerollButton != null)
            requestHeader.AddChild(rerollButton);
        outer.AddChild(requestHeader);
        outer.AddChild(new Control { MinHeight = 2 });
        outer.AddChild(requestContent);

        outer.AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = RequisitionsUiStyles.DarkGreen },
            MinHeight = 1,
            Margin = new Thickness(0, 6),
        });

        outer.AddChild(new Label
        {
            Text = Loc.GetString("n14-requisitions-section-reward"),
            FontColorOverride = RequisitionsUiStyles.TextDim,
        });
        outer.AddChild(new Control { MinHeight = 2 });
        outer.AddChild(rewardContent);

        return outer;
    }

    private static WrapContainer BuildChipWrap()
    {
        return new WrapContainer
        {
            LayoutAxis = Axis.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 12,
            CrossSeparationOverride = 4,
        };
    }

    private Control BuildItemChip(string iconProto, bool isReagent, string markup)
    {
        var chip = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

        if (!isReagent)
        {
            chip.AddChild(MakeIcon(iconProto, 28));
            chip.AddChild(new Control { MinWidth = 4 });
        }

        var label = new RichTextLabel { VerticalAlignment = Control.VAlignment.Center };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow(markup));
        chip.AddChild(label);

        return chip;
    }

    private Control BuildRewardItemChips(Dictionary<string, int> rewardItems)
    {
        var wrap = BuildChipWrap();
        foreach (var (protoId, amount) in rewardItems)
        {
            var name = _prototypes.TryIndex<EntityPrototype>(protoId, out var proto) ? proto.Name : protoId;
            wrap.AddChild(BuildItemChip(protoId, false, $"{amount}x {name}"));
        }
        return wrap;
    }

    private Control? BuildCrateContentsChips(string crateProto)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(crateProto, out var proto) ||
            !proto.TryGetComponent<StorageFillComponent>("StorageFill", out var fill) ||
            fill.Contents.Count == 0)
        {
            return null;
        }

        var wrap = BuildChipWrap();
        foreach (var item in fill.Contents)
        {
            if (item.PrototypeId is not { } pid)
                continue;

            var name = _prototypes.TryIndex<EntityPrototype>(pid, out var itemProto) ? itemProto.Name : pid.Id;
            wrap.AddChild(BuildItemChip(pid.Id, false, $"{item.Amount}x {name}"));
        }

        return wrap.ChildCount > 0 ? wrap : null;
    }

    private Control BuildCashRewardChip(int amount)
    {
        return BuildItemChip(PrewarMoneyProto, false, Loc.GetString("n14-requisitions-bounty-reward-cash", ("reward", amount)));
    }

    private Control BuildBudgetRewardChip(int score)
    {
        return BuildItemChip(PrewarMoneyProto, false, Loc.GetString("n14-requisitions-random-request-cash-reward", ("amount", score)));
    }

    private static int CountdownBucket(TimeSpan target, TimeSpan now)
    {
        var remaining = (target - now).TotalSeconds;
        return remaining <= 0 ? 0 : (int) (remaining / 15);
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "0:00";

        var minutes = (int) remaining.TotalMinutes;
        var seconds = remaining.Seconds;
        return $"{minutes}:{seconds:D2}";
    }

    private void UpdateHistory()
    {
        if (_window == null)
            return;

        var history = _state?.History;
        var sig = history == null
            ? "x"
            : string.Join(";", history.Select(e => $"{e.Buyer}|{e.Crate}|{e.Amount}|{e.Cost}|{e.Sold}"));
        if (!RenderChanged("history", sig))
            return;

        _window.HistoryContainer.DisposeAllChildren();
        if (history == null || history.Count == 0)
        {
            _window.HistoryStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-history-empty")));
            return;
        }

        _window.HistoryStatusLabel.SetMessage(FormattedMessage.FromUnformatted(string.Empty));
        foreach (var entry in history)
        {
            var name = _prototypes.TryIndex<EntityPrototype>(entry.Crate, out var proto) ? proto.Name : entry.Crate;
            var markup = entry.Sold
                ? Loc.GetString("n14-requisitions-history-row-sold", ("amount", entry.Amount), ("item", name), ("cost", entry.Cost))
                : Loc.GetString("n14-requisitions-history-row-bought", ("buyer", entry.Buyer), ("amount", entry.Amount), ("item", name), ("cost", entry.Cost));
            _window.HistoryContainer.AddChild(MakeIconRow(entry.Crate, markup));
        }
    }

    private void UpdateSellTab()
    {
        if (_window == null)
            return;

        var value = _state?.PlatformSaleValue ?? 0;
        var platformItems = _state?.PlatformItems;

        var search = _window.SellSearchBar.Text.Trim();
        var itemsSig = platformItems == null
            ? "x"
            : string.Join(";", platformItems.Select(i => $"{i.Proto}|{i.Count}|{i.Value}|{string.Join(",", i.Outputs)}"));
        var sig = $"{value}|{search}|{itemsSig}|{(_state?.SellEntries.Count ?? 0)}";
        if (!RenderChanged("sell", sig))
            return;

        _window.SellItemsContainer.DisposeAllChildren();
        if (platformItems == null || platformItems.Count == 0)
        {
            _window.SellItemsContainer.AddChild(new Label { Text = Loc.GetString("n14-requisitions-sell-empty") });
        }
        else
        {
            foreach (var item in platformItems)
            {
                var itemName = _prototypes.TryIndex<EntityPrototype>(item.Proto, out var ip) ? ip.Name : item.Proto;
                var markup = item.Value > 0
                    ? Loc.GetString("n14-requisitions-sell-item", ("item", itemName), ("count", item.Count), ("value", item.Value))
                    : Loc.GetString("n14-requisitions-sell-item-trade", ("item", itemName), ("count", item.Count));
                var outputs = item.Outputs.Count > 0
                    ? item.Outputs.Select(o => (EntProtoId) o).ToList()
                    : null;
                _window.SellItemsContainer.AddChild(MakeIconRow(item.Proto, markup, outputs));
            }
        }

        _window.SellTotalLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(
            Loc.GetString("n14-requisitions-sell-total", ("value", value))));

        _window.SellCatalogContainer.DisposeAllChildren();
        var sellEntries = _state?.SellEntries;
        if (sellEntries == null || sellEntries.Count == 0)
        {
            _window.SellCatalogContainer.AddChild(new Label
            {
                Text = Loc.GetString("n14-requisitions-sell-catalog-empty"),
            });
            return;
        }

        foreach (var entry in sellEntries)
        {
            var name = entry.Name is { } loc && Loc.TryGetString(loc, out var localized)
                ? localized
                : _prototypes.TryIndex<EntityPrototype>(entry.Item, out var itemProto)
                    ? itemProto.Name
                    : entry.Item.Id;

            if (!string.IsNullOrWhiteSpace(search) && !name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                continue;

            string reward;
            if (entry.Exchange.Count > 0)
                reward = entry.Value > 0 ? $"${entry.Value} +" : "→";
            else
                reward = $"${entry.Value}";

            var markup = Loc.GetString("n14-requisitions-sell-catalog-row", ("item", name), ("reward", reward));
            _window.SellCatalogContainer.AddChild(MakeIconRow(entry.Item.Id, markup, entry.Exchange));
        }
    }

    private void RefreshVisibleEntries()
    {
        RefreshProducts();
        RefreshCart();
        PopulatePendingOrders();
    }

    private bool RenderChanged(string key, string signature)
    {
        if (_renderSigs.TryGetValue(key, out var prev) && prev == signature)
            return false;

        _renderSigs[key] = signature;
        return true;
    }

    private void UpdateLinkStatus()
    {
        if (_window == null)
            return;

        var linked = _state is { Linked: true };
        var key = linked ? "n14-requisitions-linked" : "n14-requisitions-unlinked";
        _window.LinkStatusLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(key)));
    }

    private void UpdatePlatformButtons()
    {
        if (_window == null)
            return;

        _window.PlatformButton.Disabled = true;

        if (_state == null)
        {
            _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-missing");
            return;
        }

        if (_state.Busy || _state.PlatformLowered is Preparing or Lowering or Raising)
        {
            _confirmLower = false;
            _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-busy");
            return;
        }

        switch (_state.PlatformLowered)
        {
            case Lowered:
                _confirmLower = false;
                _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-raise");
                _window.PlatformButton.Disabled = false;
                break;
            case Raised:
                _window.PlatformButton.Text = _confirmLower
                    ? Loc.GetString("n14-requisitions-platform-lower-confirm")
                    : Loc.GetString("n14-requisitions-platform-lower");
                _window.PlatformButton.Disabled = false;
                break;
            default:
                _confirmLower = false;
                _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-missing");
                break;
        }
    }

    private void PressPlatformButton()
    {
        if (_state == null || _state.Busy)
            return;

        switch (_state.PlatformLowered)
        {
            case Lowered:
                SendMessage(new RequisitionsPlatformMsg(true));
                break;
            case Raised:
                if (_state.PlatformSaleValue > 0 && !_confirmLower)
                {
                    _confirmLower = true;
                    _window!.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-lower-confirm");
                    return;
                }
                _confirmLower = false;
                SendMessage(new RequisitionsPlatformMsg(false));
                break;
        }
    }

    private void UpdateBalance()
    {
        if (_window == null || _state == null)
            return;

        _window.BudgetLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
            "n14-requisitions-balance",
            ("balance", _state.Balance))));

        _window.CapacityLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
            "n14-requisitions-capacity",
            ("count", _state.OrderCount),
            ("capacity", _state.Capacity))));
    }

    private void PopulateCategories()
    {
        if (_window == null)
            return;

        if (!RenderChanged("categories", $"{_selectedCategory}|{CategoriesSignature()}"))
            return;

        _window.CategoriesContainer.DisposeAllChildren();

        var buttons = new List<Button>();

        var allButton = CreateCategoryButton(Loc.GetString("n14-requisitions-category-all"), _selectedCategory == null);
        allButton.OnPressed += _ =>
        {
            _selectedCategory = null;
            RefreshShop();
        };
        buttons.Add(allButton);

        if (_state is { } computer)
        {
            for (var i = 0; i < computer.Categories.Count; i++)
            {
                var categoryIndex = i;
                var button = CreateCategoryButton(Loc.GetString(computer.Categories[i].Name), _selectedCategory == categoryIndex);
                button.OnPressed += _ =>
                {
                    _selectedCategory = categoryIndex;
                    RefreshShop();
                };
                buttons.Add(button);
            }
        }

        var buttonWidth = CategoryMinWidth;
        foreach (var button in buttons)
        {
            button.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            buttonWidth = Math.Max(buttonWidth, MathF.Ceiling(button.DesiredSize.X));
        }

        _window.CategoryPanel.MinWidth = buttonWidth + CategoryPanelPadding;
        foreach (var button in buttons)
        {
            button.MinWidth = buttonWidth;
            _window.CategoriesContainer.AddChild(button);
        }
    }

    private static Button CreateCategoryButton(string text, bool disabled)
    {
        var button = new Button
        {
            Text = text,
            Disabled = disabled,
            ClipText = false,
            HorizontalExpand = true,
        };
        RequisitionsUiStyles.ApplyQuantityButton(button);
        return button;
    }

    private void PopulateProducts()
    {
        if (_window == null)
            return;

        _window.ProductsContainer.DisposeAllChildren();
        _productCards.Clear();

        if (_state is not { } computer)
            return;

        var search = _window.SearchBar.Text.Trim();
        var added = 0;

        for (var categoryIndex = 0; categoryIndex < computer.Categories.Count; categoryIndex++)
        {
            if (_selectedCategory != null && _selectedCategory != categoryIndex)
                continue;

            var category = computer.Categories[categoryIndex];
            for (var entryIndex = 0; entryIndex < category.Entries.Count; entryIndex++)
            {
                var entry = category.Entries[entryIndex];
                var display = GetDisplay(entry);
                if (!MatchesSearch(display, search))
                    continue;

                var key = new CartKey(categoryIndex, entryIndex);
                var costText = GetCostText(entry);
                if (entry.Stock >= 0)
                {
                    var bought = _state?.Purchased.GetValueOrDefault(entry.Crate.Id) ?? 0;
                    costText += "  " + Loc.GetString("n14-requisitions-stock-left", ("left", Math.Max(0, entry.Stock - bought)));
                }

                var card = new RequisitionsProductCard
                {
                    CategoryIndex = categoryIndex,
                    EntryIndex = entryIndex,
                    UnitCost = entry.Cost,
                    Cost = { Text = costText },
                    Icon =
                    {
                        Textures = display.IconTextures,
                        Modulate = display.IconModulate,
                    },
                };
                card.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
                card.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);
                card.TooltipSupplier = _ => BuildCrateTooltip(entry.Crate.Id);

                card.AddButton.OnPressed += _ => AddToCart(key);
                card.RemoveButton.OnPressed += _ => RemoveFromCart(key);
                card.MaxButton.OnPressed += _ => AddToCart(key, int.MaxValue);

                _productCards[key] = card;
                UpdateProductCard(card, key);
                _window.ProductsContainer.AddChild(card);
                added++;
            }
        }

        if (added == 0)
        {
            _window.ProductsContainer.AddChild(new Label
            {
                Text = Loc.GetString("n14-requisitions-products-empty"),
            });
        }
    }

    private void AddToCart(CartKey key, int amount = 1)
    {
        if (!TryGetEntry(key, out var entry))
            return;

        var add = Math.Min(amount, MaxAddable(key, entry));
        if (add <= 0)
            return;

        _cart[key] = GetCartAmount(key) + add;
        RefreshCart();
        UpdateVisibleProductCards();
    }

    private void RemoveFromCart(CartKey key)
    {
        var amount = GetCartAmount(key);
        if (amount <= 0)
            return;

        if (amount == 1)
            _cart.Remove(key);
        else
            _cart[key] = amount - 1;

        RefreshCart();
        UpdateVisibleProductCards();
    }

    private bool CanAdd(CartKey key, RequisitionsEntry entry)
    {
        return MaxAddable(key, entry) > 0;
    }

    private int MaxAddable(CartKey key, RequisitionsEntry entry)
    {
        if (_state == null)
            return 0;

        var byCapacity = GetRemainingCapacity() - GetCartAmount();
        var byBudget = entry.Cost > 0
            ? (_state.Balance - GetCartSupplyTotal()) / entry.Cost
            : byCapacity;
        var byStock = StockRemaining(key, entry);

        return Math.Max(0, Math.Min(Math.Min(byCapacity, byBudget), byStock));
    }

    private int StockRemaining(CartKey key, RequisitionsEntry entry)
    {
        if (entry.Stock < 0)
            return int.MaxValue;

        var bought = _state?.Purchased.GetValueOrDefault(entry.Crate.Id) ?? 0;
        return Math.Max(0, entry.Stock - bought - GetCartAmount(key));
    }

    private Control? BuildCrateTooltip(string crateProto)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(crateProto, out var proto) ||
            !proto.TryGetComponent<StorageFillComponent>("StorageFill", out var fill) ||
            fill.Contents.Count == 0)
        {
            return null;
        }

        var contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6),
        };

        foreach (var item in fill.Contents)
        {
            if (item.PrototypeId is not { } pid)
                continue;

            var name = _prototypes.TryIndex<EntityPrototype>(pid, out var itemProto) ? itemProto.Name : pid.Id;
            contents.AddChild(MakeIconRow(pid.Id, $"{item.Amount}x {name}"));
        }

        if (contents.ChildCount == 0)
            return null;

        var panel = new PanelContainer { PanelOverride = RequisitionsUiStyles.CardPanel() };
        panel.AddChild(contents);
        return panel;
    }

    private void RefreshCart()
    {
        if (_window == null)
            return;

        _window.CartContainer.DisposeAllChildren();

        var search = _window.SearchBar.Text.Trim();
        var scopedItems = 0;
        var visibleItems = 0;
        var items = _cart.OrderBy(p => p.Key.Category).ThenBy(p => p.Key.Entry).Select(p => (p.Key, p.Value)).ToList();

        foreach (var (key, amount) in items)
        {
            if (_selectedCategory != null && _selectedCategory != key.Category)
                continue;

            if (!TryGetEntry(key, out var entry))
                continue;

            scopedItems++;
            var display = GetDisplay(entry);
            if (!MatchesSearch(display, search))
                continue;

            visibleItems++;
            var row = new RequisitionsCartRow
            {
                CategoryIndex = key.Category,
                EntryIndex = key.Entry,
                UnitCost = entry.Cost,
                Quantity = { Text = amount.ToString() },
                Cost = { Text = GetLineCostText(entry, amount) },
                Icon =
                {
                    Textures = display.IconTextures,
                    Modulate = display.IconModulate,
                },
            };
            row.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
            row.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);

            row.AddButton.OnPressed += _ => AddToCart(key);
            row.RemoveButton.OnPressed += _ => RemoveFromCart(key);
            row.AddButton.Disabled = !CanAdd(key, entry);

            _window.CartContainer.AddChild(row);
        }

        var supplyTotal = GetCartSupplyTotal();
        var cartAmount = GetCartAmount();
        var remainingCapacity = GetRemainingCapacity();

        _window.CartTotalLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(
            Loc.GetString("n14-requisitions-cart-total", ("total", supplyTotal))));

        var status = string.Empty;
        if (_cart.Count == 0)
            status = Loc.GetString("n14-requisitions-cart-empty");
        else if (visibleItems == 0 && !string.IsNullOrWhiteSpace(search))
            status = Loc.GetString("n14-requisitions-cart-filter-empty");
        else if (scopedItems == 0)
            status = Loc.GetString("n14-requisitions-cart-category-empty");
        else if (_state != null && supplyTotal > _state.Balance)
            status = Loc.GetString("n14-requisitions-cart-insufficient-funds");
        else if (cartAmount > remainingCapacity)
            status = Loc.GetString("n14-requisitions-cart-insufficient-capacity");

        _window.CartStatusLabel.SetMessage(FormattedMessage.FromUnformatted(status));

        _window.ClearCartButton.Disabled = _cart.Count == 0;
        _window.BuyButton.Disabled = _cart.Count == 0 ||
                                     _state == null ||
                                     supplyTotal > _state.Balance ||
                                     cartAmount > remainingCapacity;
    }

    private void PopulatePendingOrders()
    {
        if (_window == null)
            return;

        var search = _window.SearchBar.Text.Trim();
        var pendingSig = _state == null
            ? "x"
            : string.Join(";", _state.PendingOrders.Select(o => $"{o.Entry.Crate}|{o.Amount}"));
        if (!RenderChanged("pending", $"{_selectedCategory}|{search}|{pendingSig}"))
            return;

        _window.PendingContainer.DisposeAllChildren();

        if (_state == null || _state.PendingOrders.Count == 0)
        {
            _window.PendingStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-pending-empty")));
            return;
        }
        var visibleOrders = new List<(RequisitionsPendingOrder Order, int? Category, ProductDisplay Display)>();
        foreach (var order in _state.PendingOrders)
        {
            var category = GetPendingOrderCategory(order.Entry);
            if (_selectedCategory != null && category != _selectedCategory)
                continue;

            var display = GetDisplay(order.Entry);
            if (!MatchesSearch(display, search))
                continue;

            visibleOrders.Add((order, category, display));
        }

        visibleOrders.Sort((a, b) =>
        {
            var categoryComparison = (a.Category ?? int.MaxValue).CompareTo(b.Category ?? int.MaxValue);
            if (categoryComparison != 0)
                return categoryComparison;

            return string.Compare(a.Display.Name, b.Display.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var (order, _, display) in visibleOrders)
        {
            var card = new RequisitionsPendingOrderCard
            {
                Cost = { Text = GetCostText(order.Entry) },
                Quantity = { Text = Loc.GetString("n14-requisitions-pending-quantity", ("amount", order.Amount)) },
                Icon =
                {
                    Textures = display.IconTextures,
                    Modulate = display.IconModulate,
                },
            };
            card.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
            card.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);

            _window.PendingContainer.AddChild(card);
        }

        var status = (visibleOrders.Count, string.IsNullOrWhiteSpace(search)) switch
        {
            (0, false) => Loc.GetString("n14-requisitions-pending-filter-empty"),
            (0, true) => Loc.GetString("n14-requisitions-pending-category-empty"),
            _ => string.Empty,
        };
        _window.PendingStatusLabel.SetMessage(FormattedMessage.FromUnformatted(status));
    }

    private void UpdateVisibleProductCards()
    {
        foreach (var (key, card) in _productCards)
        {
            UpdateProductCard(card, key);
        }
    }

    private void UpdateProductCard(RequisitionsProductCard card, CartKey key)
    {
        var amount = GetCartAmount(key);
        card.Quantity.Text = amount.ToString();
        card.RemoveButton.Disabled = amount <= 0;
        var canAdd = TryGetEntry(key, out var entry) && CanAdd(key, entry);
        card.AddButton.Disabled = !canAdd;
        card.MaxButton.Disabled = !canAdd;
    }

    private void BuyCart()
    {
        if (_cart.Count == 0)
            return;

        var items = new List<RequisitionsCartItem>();
        foreach (var (key, amount) in _cart)
        {
            if (amount > 0)
                items.Add(new RequisitionsCartItem(key.Category, key.Entry, amount));
        }

        if (items.Count == 0)
            return;

        SendMessage(new RequisitionsBuyCartMsg(items));

        _cart.Clear();
        RefreshCart();
        UpdateVisibleProductCards();
    }

    private int? GetPendingOrderCategory(RequisitionsEntry entry)
    {
        if (_state is not { } computer)
            return null;

        for (var categoryIndex = 0; categoryIndex < computer.Categories.Count; categoryIndex++)
        {
            var category = computer.Categories[categoryIndex];
            foreach (var categoryEntry in category.Entries)
            {
                if (SamePendingEntry(categoryEntry, entry))
                    return categoryIndex;
            }
        }

        return null;
    }

    private static bool SamePendingEntry(RequisitionsEntry a, RequisitionsEntry b)
    {
        if (a.Crate != b.Crate ||
            a.Cost != b.Cost ||
            a.Name != b.Name ||
            a.Description != b.Description ||
            !Equals(a.Icon, b.Icon) ||
            a.Entities.Count != b.Entities.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Entities.Count; i++)
        {
            if (a.Entities[i] != b.Entities[i])
                return false;
        }

        return true;
    }

    private bool TryGetEntry(CartKey key, out RequisitionsEntry entry)
    {
        entry = default!;

        if (_state is not { } computer || key.Category < 0 || key.Category >= computer.Categories.Count)
            return false;

        var category = computer.Categories[key.Category];
        if (key.Entry < 0 || key.Entry >= category.Entries.Count)
            return false;

        entry = category.Entries[key.Entry];
        return true;
    }

    private ProductDisplay GetDisplay(RequisitionsEntry entry)
    {
        _prototypes.TryIndex<EntityPrototype>(entry.Crate, out var prototype);

        var name = prototype?.Name ?? entry.Crate.ToString();
        if (entry.Name is { } nameOverride && Loc.TryGetString(nameOverride, out var localizedName))
            name = localizedName;

        var description = prototype?.Description ?? string.Empty;
        if (entry.Description is { } descriptionOverride && Loc.TryGetString(descriptionOverride, out var localizedDescription))
            description = localizedDescription;
        if (string.IsNullOrWhiteSpace(description))
            description = Loc.GetString("n14-requisitions-card-no-description");

        var icon = GetDisplayIcon(entry.Icon, prototype);

        return new ProductDisplay(name, description, icon.Textures, icon.Modulate);
    }

    private DisplayIcon GetDisplayIcon(SpriteSpecifier? icon, EntityPrototype? fallbackPrototype)
    {
        if (icon is SpriteSpecifier.EntityPrototype entityIcon &&
            _prototypes.TryIndex<EntityPrototype>(entityIcon.EntityPrototypeId, out var iconPrototype))
        {
            return GetPrototypeDisplayIcon(iconPrototype);
        }

        if (icon != null)
            return new DisplayIcon(new List<Texture> { _sprite.Frame0(icon) }, Color.White);

        if (fallbackPrototype != null)
            return GetPrototypeDisplayIcon(fallbackPrototype);

        return new DisplayIcon(new List<Texture>(), Color.White);
    }

    private DisplayIcon GetPrototypeDisplayIcon(EntityPrototype prototype)
    {
        var textures = SpriteComponent.GetPrototypeTextures(prototype, _resources)
            .Select(texture => texture.Default)
            .ToList();

        var modulate = Color.White;
        if (prototype.TryGetComponent<SpriteComponent>("Sprite", out var sprite) &&
            sprite.AllLayers.Any())
        {
            modulate = sprite.AllLayers.First().Color;
        }

        return new DisplayIcon(textures, modulate);
    }

    private DisplayIcon GetEntityIcon(string proto)
    {
        return _prototypes.TryIndex<EntityPrototype>(proto, out var prototype)
            ? GetPrototypeDisplayIcon(prototype)
            : new DisplayIcon(new List<Texture>(), Color.White);
    }

    private LayeredTextureRect MakeIcon(string proto, float size)
    {
        var data = GetEntityIcon(proto);
        return new LayeredTextureRect
        {
            SetSize = new Vector2(size, size),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = Control.VAlignment.Center,
            Textures = data.Textures,
            Modulate = data.Modulate,
        };
    }

    private Control MakeIconRow(string itemProto, string markup, IReadOnlyList<EntProtoId>? outputs = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        row.AddChild(MakeIcon(itemProto, 32));
        row.AddChild(new Control { MinWidth = 6 });

        var label = new RichTextLabel { VerticalAlignment = Control.VAlignment.Center };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow(markup));
        row.AddChild(label);

        if (outputs != null)
        {
            foreach (var output in outputs)
            {
                row.AddChild(new Control { MinWidth = 4 });

                var outputProto = output.Id;
                var icon = MakeIcon(outputProto, 28);
                icon.TooltipSupplier = _ => BuildCrateTooltip(outputProto);
                icon.MouseFilter = Control.MouseFilterMode.Pass;
                row.AddChild(icon);
            }
        }

        row.AddChild(new Control { HorizontalExpand = true });

        row.TooltipSupplier = _ => BuildCrateTooltip(itemProto);
        row.MouseFilter = Control.MouseFilterMode.Pass;

        return row;
    }

    private static bool MatchesSearch(ProductDisplay display, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return display.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetCostText(RequisitionsEntry entry)
    {
        return Loc.GetString("n14-requisitions-card-cost", ("cost", entry.Cost));
    }

    private static string GetLineCostText(RequisitionsEntry entry, int amount)
    {
        return Loc.GetString("n14-requisitions-cart-row-cost", ("cost", entry.Cost * amount));
    }

    private int GetCartAmount(CartKey key)
    {
        return _cart.GetValueOrDefault(key);
    }

    private int GetCartAmount()
    {
        var amount = 0;
        foreach (var count in _cart.Values)
        {
            amount += count;
        }

        return amount;
    }

    private int GetCartSupplyTotal()
    {
        var total = 0;
        foreach (var (key, amount) in _cart)
        {
            if (TryGetEntry(key, out var entry))
                total += entry.Cost * amount;
        }

        return total;
    }

    private int GetRemainingCapacity()
    {
        if (_state == null)
            return 0;

        return Math.Max(0, _state.Capacity - _state.OrderCount);
    }

    private readonly record struct CartKey(int Category, int Entry);

    private readonly record struct DisplayIcon(List<Texture> Textures, Color Modulate);

    private readonly record struct ProductDisplay(string Name, string Description, List<Texture> IconTextures, Color IconModulate);
}
