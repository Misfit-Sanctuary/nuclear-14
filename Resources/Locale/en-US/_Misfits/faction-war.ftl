# Faction War Panel (FactionWarWindow GUI) - #Misfits Add
# #Misfits Refactor - /war panel redesign: tabs, war cards with inline join/ceasefire, guided declare form.

faction-war-cmd-desc = Opens the War panel.
faction-war-join-cmd-desc = Opens the War panel on the Wars tab, where you can join a side in a pending war.

faction-war-window-title = War
faction-war-refresh = Refresh
faction-war-raid-notice = Declaring war does not approve raids. Use /raid to request one.

# Summary row
faction-war-summary-no-character = No character
faction-war-summary-not-in-war = You are not fighting in any war. { $count ->
    [0] No wars this round.
    [one] 1 war this round.
   *[other] { $count } wars this round.
}
faction-war-summary-in-war = You fight for { $side } in { $war }.

# Tabs
faction-war-tab-wars = Wars
faction-war-tab-wars-count = Wars ({ $count })
faction-war-tab-declare = Declare War
faction-war-tab-mine = My requests
faction-war-tab-mine-count = My requests ({ $count } open)

# Wars tab
faction-war-no-active-wars = No wars this round.
faction-war-no-my-wars = You haven't declared or joined a war this round.
faction-war-role-declarer = You declared this war
faction-war-role-target = War was declared on you
faction-war-role-joined = You joined { $side }
faction-war-card-declared = Declared by { $by } against { $against }
faction-war-phase-pending = Gathering sides
faction-war-phase-review = Reviewing sides
faction-war-phase-active = Active
faction-war-side-count = { $count ->
    [one] 1 fighter
   *[other] { $count } fighters
}
faction-war-side-count-you = { $count ->
    [one] 1 fighter, including you
   *[other] { $count } fighters, including you
}
faction-war-join-side-button = Join { $side }
faction-war-join-confirm-button = Confirm: become KOS
faction-war-join-kos-confirm = Joining makes you Kill-On-Sight to the other side until the war ends. Press again to confirm.
# #Cythisiax Edited - shortened footer so it reads at a glance
faction-war-join-kos-note = You can join a side while a war is gathering sides. Once you join, you are Kill-On-Sight to the other side until the war ends.
faction-war-ceasefire-hint = You started this fight, so you can offer to end it.
faction-war-ceasefire-button = Propose ceasefire
faction-war-ceasefire-confirm = Confirm ceasefire offer

# Incoming ceasefire banner
faction-war-ceasefire-incoming = { $name } proposes a ceasefire.
faction-war-ceasefire-incoming-hint = Accepting ends the war for both sides.
faction-war-ceasefire-accept = Accept ceasefire
faction-war-ceasefire-reject = Reject

# Declare tab
faction-war-step-target = 1. Who are you declaring war on?
faction-war-kind-faction = Faction
faction-war-kind-group = Group
faction-war-select-faction = Select a faction...
faction-war-select-group = Select a group...
faction-war-step-reason = 2. Why? (casus belli)
faction-war-casus-belli-placeholder = State your justification for war...
faction-war-reason-counter = { $count } / { $min } words minimum
faction-war-step-review = 3. Review
faction-war-preview-locked = You can't declare war right now. See the message above.
faction-war-preview-no-targets = There is nobody you can declare war on right now.
faction-war-preview-pick-target = Pick a faction or group to declare war on.
faction-war-preview-need-reason = You will declare war on [bold]{ $target }[/bold]. Give a reason of at least { $min } words.
faction-war-preview-ready = You will declare war on [bold]{ $target }[/bold], because: [italic]"{ $reason }"[/italic]
faction-war-rules = What happens next: the war gathers sides for 5 minutes and anyone may join a side from the Wars tab. Then each side's leader reviews who joined, and the war goes live.
faction-war-declare-button = Declare war
faction-war-declare-confirm = Press again to declare war
faction-war-confirm-prompt = Press Declare again to declare war on { $target }.

# War invitation popup
faction-war-invite-title = War invitation
faction-war-invite-header = Join { $side }?
faction-war-invite-body = [bold]{ $inviter }[/bold] has invited you to fight for [bold]{ $side }[/bold].
faction-war-invite-note = Accepting makes you Kill-On-Sight to the other side until the war ends.
faction-war-invite-accept = Join side
faction-war-invite-decline = Decline

# Side review popup
faction-war-review-title = Review your side
faction-war-review-header = Who fights for { $side }?
faction-war-review-hint = Everyone stays on your side unless you mark them for removal.
faction-war-review-empty = Nobody else joined your side.
faction-war-review-remove = Remove
faction-war-review-count = Keeping { $keep }, removing { $remove }.
faction-war-review-submit = Confirm side
