// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Medical.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared._Misfits.Common.Medical;
using Content.Shared._Misfits.Genetics.Console;
using Robust.Shared.Containers;

namespace Content.Server._Misfits.Genetics.Console;

/// <summary>
/// Adapts this fork's legacy medical scanner and device-linking API to the
/// scanner events used by the imported genetics console.
/// </summary>
public sealed class GeneticsMedicalScannerLinkSystem : EntitySystem
{
    private const string ScannerSourcePort = "MedicalScannerSender";
    private const string ScannerSinkPort = "MedicalScannerReceiver";
    private const string BodyContainerId = "scanner-bodyContainer";

    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GeneticsScannerComponent, MapInitEvent>(OnConsoleMapInit);
        SubscribeLocalEvent<GeneticsScannerComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<GeneticsScannerComponent, PortDisconnectedEvent>(OnPortDisconnected);

        SubscribeLocalEvent<MedicalScannerComponent, EntInsertedIntoContainerMessage>(OnScannerInserted);
        SubscribeLocalEvent<MedicalScannerComponent, EntRemovedFromContainerMessage>(OnScannerEjected);
    }

    private void OnConsoleMapInit(Entity<GeneticsScannerComponent> ent, ref MapInitEvent args)
    {
        // Handheld genome scanners also use GeneticsScannerComponent, but do
        // not support device links.
        if (!TryComp<DeviceLinkSourceComponent>(ent.Owner, out var source))
            return;

        // Device links are serialized. Restore the genetics-side state when a
        // map containing an already linked console is loaded.
        foreach (var (sink, ports) in source.LinkedPorts)
        {
            if (!ports.Contains((ScannerSourcePort, ScannerSinkPort)) ||
                !TryComp<MedicalScannerComponent>(sink, out var scanner))
            {
                continue;
            }

            Connect(ent.Owner, sink, scanner);
            return;
        }
    }

    private void OnNewLink(Entity<GeneticsScannerComponent> ent, ref NewLinkEvent args)
    {
        if (args.Source != ent.Owner ||
            args.SourcePort != ScannerSourcePort ||
            args.SinkPort != ScannerSinkPort ||
            !TryComp<MedicalScannerComponent>(args.Sink, out var scanner))
        {
            return;
        }

        Connect(ent.Owner, args.Sink, scanner);
    }

    private void OnPortDisconnected(Entity<GeneticsScannerComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port != ScannerSourcePort)
            return;

        var scanner = ent.Comp.Scanner ?? EntityUid.Invalid;
        var ev = new ScannerDisconnectedEvent(scanner);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    private void OnScannerInserted(Entity<MedicalScannerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != BodyContainerId)
            return;

        RaiseForLinkedConsoles(ent.Owner, new ScannerInsertedEvent(ent.Owner, args.Entity));
    }

    private void OnScannerEjected(Entity<MedicalScannerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != BodyContainerId)
            return;

        RaiseForLinkedConsoles(ent.Owner, new ScannerEjectedEvent(ent.Owner, args.Entity));
    }

    private void Connect(EntityUid console, EntityUid scanner, MedicalScannerComponent scannerComp)
    {
        var connected = new ScannerConnectedEvent(scanner);
        RaiseLocalEvent(console, ref connected);

        if (scannerComp.BodyContainer.ContainedEntity is not { } occupant)
            return;

        var inserted = new ScannerInsertedEvent(scanner, occupant);
        RaiseLocalEvent(console, ref inserted);
    }

    private void RaiseForLinkedConsoles<TEvent>(EntityUid scanner, TEvent ev)
        where TEvent : struct
    {
        var consoles = EntityQueryEnumerator<GeneticsScannerComponent>();
        while (consoles.MoveNext(out var console, out _))
        {
            if (!IsScannerLink(console, scanner))
                continue;

            RaiseLocalEvent(console, ref ev);
        }
    }

    private bool IsScannerLink(EntityUid console, EntityUid scanner)
    {
        // Avoid asking DeviceLinkSystem to resolve components on handheld
        // genetics scanners (or on entities currently being deleted).
        if (!TryComp<DeviceLinkSourceComponent>(console, out var source) ||
            !HasComp<DeviceLinkSinkComponent>(scanner))
        {
            return false;
        }

        return _deviceLink.GetLinks(console, scanner, source)
            .Any(link => link.source.Id == ScannerSourcePort && link.sink.Id == ScannerSinkPort);
    }
}
