
using System.Linq;
using System.Text;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Administration;
using Content.Shared.NPC;

namespace Content.Server.NPC.HTN;
/// <summary>
/// Misfit Change: moved HTNDebug functionalities to its own file to stop clutter
/// Methods and events related to debugging HTN system
/// </summary>
public sealed partial class HTNSystem : EntitySystem
{
    private void DebugInit()
    {
        SubscribeNetworkEvent<RequestHTNMessage>(OnHTNMessage);
    }
    private void OnHTNMessage(RequestHTNMessage msg, EntitySessionEventArgs args)
    {
        if (!_admin.HasAdminFlag(args.SenderSession, AdminFlags.Debug))
        {
            _subscribers.Remove(args.SenderSession);
            return;
        }

        if (_subscribers.Add(args.SenderSession))
            return;

        _subscribers.Remove(args.SenderSession);
    }
    private void HTNDebug(HTNComponent comp)
    {
        // Send debug info
        foreach (var session in _subscribers)
        {
            var text = new StringBuilder();

            if (comp.Plan != null)
            {
                text.AppendLine($"BTR: {string.Join(", ", comp.Plan.BranchTraversalRecord)}");
                text.AppendLine($"tasks:");
                var root = comp.RootTask;
                var btr = new List<int>();
                var level = -1;
                AppendDebugText(root, text, comp.Plan.BranchTraversalRecord, btr, ref level, comp.Plan);
            }

            RaiseNetworkEvent(new HTNMessage()
            {
                Uid = GetNetEntity(comp.Owner),
                Text = text.ToString(),
            }, session.Channel);
        }
    }

    private void AppendDebugText(HTNTask task, StringBuilder text, List<int> planBtr, List<int> btr, ref int level, HTNPlan plan)
    {
        // If it's the selected BTR then highlight.
        for (var i = 0; i < btr.Count; i++)
        {
            text.Append("--");
        }

        text.Append(' ');

        if (task is HTNPrimitiveTask primitive)
        {
            // Highlight current task
            if (plan.CurrentTask == primitive && btr.SequenceEqual(plan.BranchTraversalRecord))
            {
                // Still results in false positive if current branch contains multiple of the same task...
                text.Append("> ");
            }
            text.AppendLine(primitive.ToString());
            return;
        }

        if (task is HTNCompoundTask compTask)
        {
            var compound = _prototypeManager.Index<HTNCompoundPrototype>(compTask.Task);
            level++;
            text.AppendLine(compound.ID);
            var branches = compound.Branches;

            for (var i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                btr.Add(i);

                foreach (var sub in branch.Tasks)
                {
                    AppendDebugText(sub, text, planBtr, btr, ref level, plan);
                }

                btr.RemoveAt(btr.Count - 1);
            }

            level--;
            return;
        }

        throw new NotImplementedException();
    }
    public string GetDomain(HTNCompoundTask compound)
    {
        // TODO: Recursively add each one
        var indent = 0;
        var builder = new StringBuilder();
        AppendDomain(builder, compound, ref indent);

        return builder.ToString();
    }
    private void AppendDomain(StringBuilder builder, HTNTask task, ref int indent)
    {
        var buffer = string.Concat(Enumerable.Repeat("    ", indent));

        if (task is HTNPrimitiveTask primitive)
        {
            builder.AppendLine(buffer + $"Primitive: {task}");
            builder.AppendLine(buffer + $"  operator: {primitive.Operator.GetType().Name}");
        }
        else if (task is HTNCompoundTask compTask)
        {
            var compound = _prototypeManager.Index<HTNCompoundPrototype>(compTask.Task);
            builder.AppendLine(buffer + $"Compound: {task}");

            for (var i = 0; i < compound.Branches.Count; i++)
            {
                var branch = compound.Branches[i];

                builder.AppendLine(buffer + "  branch:");
                indent++;

                foreach (var branchTask in branch.Tasks)
                {
                    AppendDomain(builder, branchTask, ref indent);
                }

                indent--;
            }
        }
    }


}
