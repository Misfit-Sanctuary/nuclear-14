using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Misfits.Requisitions.Components;
using Content.Server.Labels.Components;
using Content.Server.Paper;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Requisitions
{
    public sealed partial class RequisitionsSystem
    {
        [Dependency] private readonly PaperSystem _paperSystem = default!;
        [Dependency] private readonly ItemSlotsSystem _slots = default!;
        [Dependency] private readonly MetaDataSystem _metaSystem = default!;

        private void PrintInvoice(EntityUid requisitionOrder, EntityCoordinates coordinates, string paperwork)
        {
            var printedPaper = EntityManager.SpawnEntity(paperwork, coordinates);

            if (!TryComp<PaperComponent>(printedPaper, out var paper))
                return;

            var serialNum = _random.Next(10000, 999999);
            var lotNum = _random.Next(10, 99);

            var orderName = MetaData(requisitionOrder).EntityName;
            uint weight = 10;
            var contentList = new FormattedMessage();

            if (TryComp(requisitionOrder, out ContainerManagerComponent? containerComp))
            {
                foreach (var container in containerComp.Containers.Values)
                {
                    var entityIndex = 0;
                    var contentCount = container.ContainedEntities.Count;

                    var content = container.ContainedEntities.GroupBy(
                        item => MetaData(item).EntityName,
                        item => item,
                        (itemName, itemGroup) => new
                        {
                            Key = itemName,
                            Name = itemName,
                            Count = itemGroup.Count()
                        });

                    if (contentCount < 1)
                        continue;

                    contentList.PushNewline();

                    foreach (var entity in content)
                    {
                        if (entity.Name == orderName)
                            continue;

                        weight += 10;
                        contentList.AddMarkupOrThrow($"{Loc.GetString("n14-requisition-paper-print-content",
                            ("count", entity.Count),
                            ("item", entity.Name.ToUpper()))}");

                        if (entityIndex == contentCount)
                            continue;

                        contentList.PushNewline();
                        entityIndex++;
                    }
                }
            }

            _metaSystem.SetEntityName(printedPaper, Loc.GetString(
                "n14-requisition-paper-print-name", ("name", orderName)));

            _paperSystem.SetContent(printedPaper, Loc.GetString(
                "n14-requisition-paper-print-manifest",
                ("containerName", orderName.ToUpper()),
                ("content", contentList.ToMarkup()),
                ("weight", weight),
                ("lot", lotNum),
                ("serialNumber", $"{serialNum:000000}")), paper);

            if (TryComp<PaperLabelComponent>(requisitionOrder, out var label))
            {
                _slots.TryInsert(requisitionOrder, label.LabelSlot, printedPaper, null);
            }
        }

        private bool IsInvoice(Entity<PaperComponent?> ent, [NotNullWhen(true)] out RequisitionsInvoiceComponent? invoice)
        {
            invoice = null;
            if (!Resolve(ent, ref ent.Comp, false))
                return false;

            if (!TryComp(ent.Owner, out invoice))
                return false;

            return ent.Comp.StampState == invoice.RequiredStamp;
        }

        private int SubmitInvoices(EntityUid uid)
        {
            var compoundRewards = 0;
            if (IsInvoice(uid, out var invoice))
                compoundRewards += invoice.Reward;

            if (!TryComp<ContainerManagerComponent>(uid, out var container))
                return compoundRewards;

            // It iterates everything inside the crate, including labelSlot
            foreach (var containerValues in container.Containers.Values)
            {
                foreach (var content in containerValues.ContainedEntities)
                {
                    if (IsInvoice(content, out invoice))
                        compoundRewards += invoice.Reward;
                }
            }

            return compoundRewards;
        }
    }
}
