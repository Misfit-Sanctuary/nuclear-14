# #Misfits Add - Localization for the /raid request system (player window + admin tab).

raid-request-window-title = Raid Request
raid-request-refresh = Refresh

# #Misfits Refactor - /raid window redesign: summary row, tabs, guided form, status cards.

# Header / eligibility
raid-request-your-faction = Raiding as { $faction }
raid-request-no-faction = You don't belong to a recognized faction.
raid-request-individual-banner = Wastelanders request raids for themselves. Only you are told the decision.
raid-request-faction-banner = You can request raids for your whole faction.
raid-request-not-eligible = Your faction ({ $faction }) is not permitted to submit raid requests.

# Tabs
raid-request-tab-raids = Raids
raid-request-tab-raids-count = Raids ({ $count })
raid-request-tab-new = Declare Raid
raid-request-tab-mine = My requests
raid-request-tab-mine-count = My requests ({ $count } open)

# Declare Raid form
raid-request-step-target = 1. Who do you want to raid?
raid-request-select-target = Select a faction or group...
raid-request-step-location = 2. Where? (optional)
raid-request-location-placeholder = e.g. NCR Outpost Bravo, the southern dunes...
raid-request-step-reason = 3. Why, and what's the plan?
raid-request-reason-placeholder = Explain who you intend to raid and why. Minimum five words.
raid-request-reason-counter = { $count } / { $min } words minimum
raid-request-step-review = 4. Review
raid-request-preview-ineligible = You can't submit a raid request right now. See the message above.
raid-request-preview-no-targets = There is nobody you can raid right now.
raid-request-preview-pick-target = Pick a faction or group to raid.
raid-request-preview-need-reason = You will request a raid on [bold]{ $target }[/bold]. Give a reason of at least { $min } words.
raid-request-preview-ready = You will request a raid on [bold]{ $target }[/bold] at { $location }, because: [italic]"{ $reason }"[/italic]
raid-request-process-faction = What happens next: the admins and the target's highest-ranking member online are asked. The first answer decides. If approved, there are 5 minutes of prep before the raid goes live for up to 15 minutes.
raid-request-process-individual = What happens next: the admins review your request. If approved, there are 5 minutes of prep before the raid goes live for up to 15 minutes.
raid-request-submit-button = Submit request
raid-request-submit-confirm = Press again to submit
raid-request-confirm-prompt = Press Submit again to send this request.

# Raids tab (join a side, like /war)
raid-request-no-open-raids = No approved or live raids right now.
# #Cythisiax Edited - shortened footer so it reads at a glance
raid-request-join-note = You can join a side while a raid is in prep. Once you join, the other side may attack you until the raid ends.
raid-request-raid-card-title = #{ $id }  { $raiders } vs { $defenders }
raid-request-side-raiders = Raiders: { $name }
raid-request-side-defenders = Defenders: { $name }
raid-request-side-volunteers = { $count ->
    [0] No volunteers
    [one] 1 volunteer
   *[other] { $count } volunteers
}
raid-request-side-you-member = Your side (by membership). { $count ->
    [0] No volunteers.
    [one] 1 volunteer.
   *[other] { $count } volunteers.
}
raid-request-side-you-volunteer = You volunteered here. { $count ->
    [one] 1 volunteer.
   *[other] { $count } volunteers.
}
raid-request-join-button = Join { $name }
raid-request-join-confirm = Confirm: join raid
raid-request-join-tooltip = You become a valid target for the other side until the raid ends.

# My requests
raid-request-no-my-requests = You haven't submitted any raid requests this round.
raid-request-card-title = #{ $id }  { $target }
raid-request-card-location = Location: { $location }
raid-request-card-decided-by = Decided by { $name }:
raid-request-status-pending = Waiting
raid-request-status-pending-hint = Waiting for an admin or the target's leader to answer.
raid-request-status-approved = Approved
raid-request-status-approved-hint = Approved. Prep is underway; the raid goes live after 5 minutes.
raid-request-status-active = Live
raid-request-status-active-hint = The raid is live. Ally and enemy tags are shown.
raid-request-status-denied = Denied
raid-request-status-denied-hint = This raid was refused. Do not attack.
raid-request-status-concluded = Over
raid-request-status-concluded-hint = This raid has ended. Stop all raid hostilities.
raid-request-status-unclaimed = Unanswered
raid-request-status-unclaimed-hint = Nobody answered this request.

# Admin embedded panel (bwoink Raid tab)
raid-request-filter-pending = Pending
raid-request-filter-decided = Decided
raid-request-filter-all = All
raid-request-list-empty = (no requests match this filter)
raid-request-no-selection = Select a request to view details.
raid-request-admin-comment-label = Remarks (sent to faction or group):
raid-request-admin-comment-placeholder = Required. This message is delivered to every notified player.
raid-request-approve-button = Approve
raid-request-deny-button = Deny
raid-request-end-button = End Raid
raid-request-comment-required = A comment is required before approving or denying.
raid-request-pending-count = { $count } pending raid request(s)
raid-request-no-pending = No pending raid requests.

# Decision popups (client-side)
raid-request-popup-approved = Raid APPROVED: { $from } → { $to }
raid-request-popup-denied = Raid DENIED: { $from } → { $to }
raid-request-popup-target-warning = Incoming raid threat APPROVED against { $target }!

# #Misfits Add - Mandatory acknowledgement shown when a raid concludes.
raid-concluded-window-title = Raid Over
raid-concluded-header = RAID #{ $id } HAS ENDED
raid-concluded-parties = { $from } vs { $to }
raid-concluded-warning = Combat authorization has ended. Stop all raid-related hostilities immediately.
raid-concluded-acknowledge = I acknowledge

# #Misfits Add - Peer-faction / peer-group approval popup (target leader bypass).
raid-request-peer-window-title = Incoming raid request
raid-request-peer-heading = { $faction } wants to raid you
raid-request-peer-note = You are the highest-ranking member online, so you can answer for your side. Your remarks are shown to the other side.
raid-request-peer-header = Incoming raid request from { $faction } against { $target }.
raid-request-peer-from-label = From:
raid-request-peer-location-label = Stated location:
raid-request-peer-location-unset = (not specified)
raid-request-peer-reason-label = Reason / plan:
raid-request-peer-comment-placeholder = Optional. Will be shown verbatim to the other faction or group.
raid-request-peer-approve-button = Allow Raid
raid-request-peer-deny-button = Refuse
