# #Cythisiax Added - Faction roster: the faction lead's roster / recruitment GUI.

faction-roster-name-rangers = New California Republic Rangers
faction-roster-name-enclave = Enclave

faction-roster-track-rangers = Rangers
faction-roster-track-military = Military
faction-roster-track-science = Science

faction-roster-offer-rangers = {$recruiter} is offering you a place among the New California Republic Rangers.

faction-roster-not-authorized = You do not hold the authority to manage this roster.

faction-roster-window-title = Faction Roster
faction-roster-refresh = Refresh
faction-roster-tab-roster = Roster
faction-roster-tab-recruit = Recruit
faction-roster-tab-oath = Oath
faction-roster-filter-placeholder = Search by name...
faction-roster-loading = Loading roster...
# #Cythisiax Edited - the roster only lists connected members now
faction-roster-summary = {$online} {$online ->
    [one] member
   *[other] members
} online.
faction-roster-empty = No members online.
# #Cythisiax Added - shown when someone without discharge rights tries to remove a member
faction-roster-status-discharge-denied = You can only demote down to the lowest rank. Removing someone from the faction needs higher command.

faction-roster-offline = Offline
faction-roster-online = Online
faction-roster-on-duty = On duty

faction-roster-promote = Promote
faction-roster-promote-tooltip = Promote to {$rank}.
faction-roster-demote = Demote
faction-roster-demote-tooltip = Demote to {$rank}.
faction-roster-demote-remove-tooltip = Remove from {$rank}.
faction-roster-specialty-grant = Assign as {$role}.
faction-roster-specialty-revoke = Remove from {$role}.
faction-roster-discharge = Discharge
faction-roster-discharge-confirm = Confirm?

faction-roster-recruit-none = No person near to recruit.
faction-roster-recruit = Recruit
faction-roster-recruit-pending = Offer pending

faction-roster-oath-hint = The oath recruits read before accepting. Write [name] for the recruit's name and [recruiter] for yours.
faction-roster-oath-counter = {$count} / {$max} characters
faction-roster-oath-save = Save Oath
faction-roster-oath-reset = Restore Default
faction-roster-status-oath-saved = Oath saved. New recruits will see it.
faction-roster-status-oath-reset = Oath restored to the default.
faction-roster-status-oath-too-long = The oath is too long (max {$max} characters).
faction-roster-status-oath-denied = Only Faction Leadership can change the oath.

faction-roster-status-not-member = That person is no longer on the roster.
faction-roster-status-self = You cannot change your own rank.
faction-roster-status-locked = {$name} is part of Faction Leadership.
faction-roster-status-max-rank = {$name} already holds the highest rank you can grant.
faction-roster-status-min-rank = {$name} has no lower rank. Use Discharge to remove them.
faction-roster-status-promoted = Promoted {$name} to {$rank}.
faction-roster-status-demoted = Demoted {$name} to {$rank}.
faction-roster-status-track-removed = Removed {$name} from {$rank}.
faction-roster-status-discharged = Discharged {$name}.
faction-roster-status-specialty-granted = Assigned {$name} as {$role}.
faction-roster-status-specialty-revoked = Removed {$name} from {$role}.
faction-roster-status-cannot-recruit = That person cannot be recruited right now.
faction-roster-status-too-far = They are too far away.
faction-roster-status-pending = They are already considering an offer.
faction-roster-status-offer-sent = Recruitment offer sent to {$name}.

faction-roster-notify-recruited = You have joined the {$faction} as a {$rank}.
faction-roster-notify-promoted = You have been promoted to {$rank}.
faction-roster-notify-demoted = You have been demoted to {$rank}.
faction-roster-notify-track-removed = You are no longer a {$rank}.
faction-roster-notify-recruited-plain = You have joined the {$faction}.
faction-roster-notify-discharged = You have been discharged from the {$faction}.
faction-roster-notify-specialty-granted = You have been assigned as {$role}.
faction-roster-notify-specialty-revoked = You are no longer assigned as {$role}.
faction-roster-recruit-accepted = {CAPITALIZE($name)} accepted and joins the {$faction}.
faction-roster-recruit-declined = {CAPITALIZE($name)} declined your recruitment offer.

faction-recruit-offer-title = Recruitment Offer
faction-recruit-offer-header = {$recruiter} invites you to join the {$faction}
faction-recruit-offer-note = Closing this window declines the offer.
faction-recruit-offer-accept = Accept
faction-recruit-offer-decline = Decline
