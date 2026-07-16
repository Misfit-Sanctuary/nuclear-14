// #Misfits Change /Tweak: Department panel for whitelist administration (checkbox-per-job style)
using System.Linq;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Misfits.Administration.UI;

public sealed class WhitelistDepartmentPanel : PanelContainer
{
    public Action<List<ProtoId<JobPrototype>>, bool>? OnSetJobs;

    private readonly List<(string JobName, CheckBox CheckBox)> _jobCheckboxes = new();

    public WhitelistDepartmentPanel(
        DepartmentPrototype department,
        IPrototypeManager proto,
        HashSet<ProtoId<JobPrototype>> whitelists)
    {
        Margin = new Thickness(0, 0, 0, 6);
        StyleClasses.Add("BackgroundDark");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
        };

        // Department header toggle — toggles all jobs at once
        var allWhitelisted = department.Roles.All(whitelists.Contains);
        var header = new CheckBox
        {
            Text = Loc.GetString(department.ID),
            Pressed = allWhitelisted,
            HorizontalExpand = true,
            Modulate = department.Color,
        };

        header.OnPressed += _ =>
        {
            // Batch every job that actually changes into one request so the server
            // only asks for a single blanket justification for the whole department.
            var changedJobs = new List<ProtoId<JobPrototype>>();
            foreach (var id in department.Roles)
            {
                if (whitelists.Contains(id) != header.Pressed)
                    changedJobs.Add(id);
            }

            if (changedJobs.Count > 0)
                OnSetJobs?.Invoke(changedJobs, header.Pressed);
        };

        // Job checkboxes in a grid (4 columns, same as vanilla)
        var grey = Color.FromHex("#ccc");
        var jobsGrid = new GridContainer
        {
            Columns = 4,
            HSeparationOverride = 6,
            VSeparationOverride = 3,
        };

        foreach (var (jobProtoId, job) in department.Roles
                     .Select(id => (Id: id, Job: proto.Index<JobPrototype>(id)))
                     .OrderByDescending(entry => entry.Job.RealDisplayWeight)
                     .ThenBy(entry => entry.Job.ID))
        {
            var capturedId = jobProtoId;
            var cb = new CheckBox
            {
                Text = job.LocalizedName,
                Pressed = whitelists.Contains(jobProtoId),
            };
            if (!job.Whitelisted)
                cb.Modulate = grey;

            cb.OnPressed += _ => OnSetJobs?.Invoke(new List<ProtoId<JobPrototype>> { capturedId }, cb.Pressed);
            _jobCheckboxes.Add((job.LocalizedName, cb));
            jobsGrid.AddChild(cb);
        }

        root.AddChild(header);
        root.AddChild(jobsGrid);
        AddChild(root);
    }

    /// <summary>
    /// Shows/hides individual job checkboxes based on the filter string.
    /// Returns true if any are visible.
    /// </summary>
    public bool Filter(string filter)
    {
        var empty = string.IsNullOrWhiteSpace(filter);
        var anyVisible = false;
        foreach (var (name, cb) in _jobCheckboxes)
        {
            var visible = empty || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
            cb.Visible = visible;
            if (visible)
                anyVisible = true;
        }
        Visible = anyVisible;
        return anyVisible;
    }
}