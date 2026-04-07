# Misfits Add — Ticket system localization for admin help & mentor help

ticket-system-claim-button = Claim
ticket-system-unclaim-button = Unclaim
ticket-system-resolve-button = Resolve
ticket-system-reopen-button = Reopen
ticket-system-status-open = Open — Unclaimed
ticket-system-status-claimed = Claimed by {$admin}
ticket-system-status-resolved = Resolved by {$admin}
ticket-system-created = [{$type} TICKET #{$id}] {$player} created a new ticket.
ticket-system-claimed = [{$type} TICKET #{$id}] Claimed by {$admin}.
ticket-system-unclaimed = [{$type} TICKET #{$id}] Unclaimed by {$admin} — ticket is open.
ticket-system-resolved = [{$type} TICKET #{$id}] Resolved by {$admin}.
ticket-system-reopened = [{$type} TICKET #{$id}] Reopened by {$admin}.
ticket-system-auto-claimed = [{$type} TICKET #{$id}] Auto-claimed by {$admin} on first reply.
ticket-system-auto-resolved-disconnect = [{$type} TICKET #{$id}] Auto-resolved — player disconnected.
# #Misfits Add - Player-facing resolve notification (sent only to the player, not admin panel)
ticket-system-resolved-with-cooldown = Your ticket has been resolved. If you need further assistance, please wait 1 minute before opening a new one.
# #Misfits Add - Player-facing cooldown block message (sent only to the player when they attempt to re-ticket too quickly)
ticket-system-cooldown-blocked = Please wait one minute after resolution before making a new ticket.
ticket-system-toast-new-title = New Ticket
ticket-system-toast-new-body = Ticket #{$id} from {$player}
ticket-system-toast-claimed-title = Ticket Claimed
ticket-system-toast-claimed-body = Ticket #{$id} claimed by {$admin}
ticket-system-toast-resolved-title = Ticket Resolved
ticket-system-toast-resolved-body = Ticket #{$id} resolved by {$admin}
ticket-system-toast-reopened-title = Ticket Reopened
ticket-system-toast-reopened-body = Ticket #{$id} from {$player} was reopened
ticket-system-must-claim-first = You must claim this ticket before replying. Press the Claim button above.
ticket-system-claim-to-reply = Claim this ticket to reply...
ticket-system-reminder = [TICKETS] {$count} unclaimed ticket(s) waiting. Use the Help panel to claim them.
ticket-system-reminder-ahelp = [AHELP TICKET] {$count} unclaimed ticket(s) waiting. Use the Help panel to claim them.
ticket-system-reminder-mhelp = [MHELP TICKET] {$count} unclaimed ticket(s) waiting. Use the Help panel to claim them.

# Quick Reply window (opened from player info panel Ahelp button)
ticket-system-quick-reply-title = AHelp — {$player}
ticket-system-quick-reply-target = Messaging: {$player}
ticket-system-quick-reply-placeholder = Type a message to send...

# Audit Log window
ticket-audit-log-button = Audit Log
ticket-audit-log-window-title = Help Ticket Audit Log

# #Misfits Add - expanded filter panel locale keys
ticket-audit-filter-player-label = Player (Name or ID):
ticket-audit-filter-player-placeholder = Enter name or GUID…
ticket-audit-filter-admin-label = Admin Name:
ticket-audit-filter-admin-placeholder = Search by admin name…
ticket-audit-filter-date-from = From Date:
ticket-audit-filter-date-to = To Date:
ticket-audit-filter-date-placeholder = YYYY-MM-DD
ticket-audit-filter-search = Search
ticket-audit-filter-clear = Clear Filters
ticket-audit-filter-month-this = This Month
ticket-audit-filter-month-last = Last Month

# Tab titles
ticket-audit-tab-events = Audit Events
ticket-audit-tab-stats = Admin Statistics

# Events list headers
ticket-audit-header-time = Time
ticket-audit-header-id = Ticket
ticket-audit-header-event = Event
ticket-audit-header-player = Player
ticket-audit-header-admin = Admin

# Pagination
ticket-audit-pagination-prev = ← Prev
ticket-audit-pagination-next = Next →
ticket-audit-pagination-label = Page {$page} of {$total} ({$count} total events)
ticket-audit-pagination-placeholder = Loading…

# Statistics tab
ticket-audit-stats-label = Admin Ticket Handling Statistics (for selected period)
ticket-audit-stats-admin = Admin Name
ticket-audit-stats-resolved = Resolved
ticket-audit-stats-claimed = Claimed
ticket-audit-stats-empty = No statistics available for the selected period.

# General
ticket-audit-log-empty = No ticket events found.

# Chat history window
ticket-chat-history-window-title = Ticket #{$id} — {$player}
ticket-chat-history-loading = Loading chat history…
ticket-chat-history-empty = No messages found for this ticket.

# Audit event type display names
ticket-audit-event-created = Created
ticket-audit-event-claimed = Claimed
ticket-audit-event-unclaimed = Unclaimed
ticket-audit-event-resolved = Resolved
ticket-audit-event-reopened = Reopened
ticket-audit-event-auto-resolved = Auto-Resolved

# #Misfits Add - Player search bar placeholder and open ticket count label
ticket-search-player-placeholder = Message player...
ticket-open-count = Open: { $count }
