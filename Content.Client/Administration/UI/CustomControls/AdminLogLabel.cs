// #Misfits Removed - Replaced by AdminLogRow in Content.Client/_Misfits/Administration/UI/Logs/.
// AdminLogRow provides category badges, color-coded player names, expandable entity detail,
// and severity-tinted row backgrounds. This flat RichTextLabel row is no longer used but
// preserved per §4 (never delete files).
//
// using Content.Shared.Administration.Logs;
// using Robust.Client.UserInterface;
// using Robust.Client.UserInterface.Controls;
//
// namespace Content.Client.Administration.UI.CustomControls;
//
// public sealed class AdminLogLabel : RichTextLabel
// {
//     public AdminLogLabel(ref SharedAdminLog log, HSeparator separator)
//     {
//         Log = log;
//         Separator = separator;
//
//         SetMessage($"{log.Date:HH:mm:ss}: {log.Message}");
//         OnVisibilityChanged += VisibilityChanged;
//     }
//
//     public SharedAdminLog Log { get; }
//
//     public HSeparator Separator { get; }
//
//     private void VisibilityChanged(Control control)
//     {
//         Separator.Visible = Visible;
//     }
//
//     protected override void Dispose(bool disposing)
//     {
//         base.Dispose(disposing);
//
//         OnVisibilityChanged -= VisibilityChanged;
//     }
// }
