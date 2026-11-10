cmd-motd-desc = Prints or sets the Message Of The Day.
cmd-motd-help = motd [ message... ]
cmd-get-motd-desc = Prints the Message Of The Day and opens its window.
cmd-get-motd-help = get-motd
cmd-set-motd-desc = Sets or clears the Message Of The Day.
cmd-set-motd-help = set-motd [ message... ]
cmd-set-motd-hint-head = [ message... ]
cmd-set-motd-hint-cont = [ ...message... ]
cmd-set-motd-cleared-motd-message = Cleared the Message of the Day.
cmd-set-motd-set-motd-message = Set the Message Of The Day to "{$motd}".
motd-none = There is no Message of the Day set.

# #Misfits Add - MOTD pictures shown in the window
cmd-set-motd-image-desc = Sets the pictures shown in the Message of the Day window.
cmd-set-motd-image-help = set-motd-image [ path... ]
cmd-set-motd-image-hint = [ /Textures/path/to/picture.png ... ]
cmd-set-motd-image-set = Set the MOTD pictures to: {$images}.
cmd-set-motd-image-cleared = Cleared the MOTD pictures.
cmd-set-motd-image-too-many = The Message of the Day can show at most {$max} pictures.
cmd-set-motd-image-invalid = "{$path}" is not a valid resource path.

# #Misfits Add - MOTD poll and voting
cmd-set-motd-poll-desc = Sets the poll displayed alongside the Message Of The Day.
cmd-set-motd-poll-help = set-motd-poll <question> | <option> | <option> [| ...]
cmd-set-motd-poll-hint = <question> | <option> | <option> [| ...]
cmd-set-motd-poll-usage = Usage: set-motd-poll <question> | <option> | <option> [| ...]
cmd-set-motd-poll-empty = The question and every option must be non-empty.
cmd-set-motd-poll-too-many-options = A poll can have at most {$max} options.
cmd-set-motd-poll-set = Set the MOTD poll to "{$question}" with {$count} option(s).
cmd-clear-motd-poll-desc = Removes the poll displayed alongside the Message Of The Day.
cmd-clear-motd-poll-help = clear-motd-poll
cmd-clear-motd-poll-cleared = Cleared the MOTD poll.
cmd-clear-motd-poll-no-poll = There is no MOTD poll to clear.
cmd-motd-vote-desc = Votes in the Message Of The Day poll, or prints its results.
cmd-motd-vote-help = motd-vote [option number]
cmd-motd-vote-no-poll = There is no MOTD poll running right now.
cmd-motd-vote-not-a-number = "{$option}" is not a number.
cmd-motd-vote-invalid-option = "{$option}" is not a valid option. Pick a number between 1 and {$max}.
cmd-motd-vote-cast = You voted for {$number}. {$option}.
cmd-motd-vote-changed = You changed your vote to {$number}. {$option}.

motd-poll-header = Poll: {$question}
motd-poll-option = {$number}. {$option} - {$votes} vote(s) ({$percent}%)
motd-poll-total = Total votes: {$total}
motd-poll-hint = Vote with "motd-vote <number>" or by clicking an option in the window.

motd-ui-title = Message of the Day
motd-ui-no-message = There is no Message of the Day set.
motd-ui-poll-question = Poll: {$question}
motd-ui-poll-option = {$number}. {$option}
motd-ui-poll-yours = (your vote)
motd-ui-poll-votes = {$percent}% ({$votes})
motd-ui-poll-total = Total votes: {$total}
motd-ui-poll-hint = Click an option to vote. You can change your vote at any time.

misfits-server-tab-motd-button = Edit MOTD
misfits-server-tab-motd-desc = Edit the message, its pictures and its poll. Applying shows it to everyone.

motd-ui-edit-header = Edit the Message of the Day
motd-ui-edit-text = Message (markup such as [bold] or [color=red] is allowed)
motd-ui-edit-images = Pictures (paths separated by ";", at most 4, relative to /Textures)
motd-ui-edit-question = Poll question (leave empty to remove the poll)
motd-ui-edit-options = Poll options (separated by "|", at least 2)
motd-ui-edit-save = Save and show to everyone
motd-ui-edit-clear-poll = Remove poll
motd-ui-edit-hint = Pictures must exist in the server's Resources/Textures. Invalid paths, more than 4 pictures, or fewer than 2 options are rejected and nothing is changed.
motd-ui-edit-toggle = Edit the MOTD
motd-ui-edit-toggle-close = Close the editor
