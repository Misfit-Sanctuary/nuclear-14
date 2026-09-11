# Requisition Computer
n14-requisition-paperwork-receiver-name = Logistics Branch
n14-requisition-paperwork-reward-message = Confirmation Received! Transferred ${$amount} from budget surplus
n14-requisition-request-fulfilled = Request fulfilled! Reward has been added to the account.

# Player feedback
n14-requisition-insufficient-funds = Insufficient budget for this order.
n14-requisition-platform-full = The platform has no room for this order.
n14-requisition-storage-full = Storage is full; some traded goods were discarded.

# Requisition Invoice
n14-requisition-paper-print-name = {$name} invoice
n14-requisition-paper-print-manifest = [head=2]
    {$containerName}[/head][bold]{$content}[/bold][head=2]
    WT. {$weight} LBS
    LOT {$lot}
    S/N {$serialNumber}[/head]
n14-requisition-paper-print-content = - {$count} {$item}

# History transcript
n14-requisition-transcript-name = requisitions transcript
n14-requisition-transcript-header = [head=2]{$group} REQUISITIONS LOG[/head]
n14-requisition-transcript-bought = {$buyer}: bought {$amount}x {$item} for ${$cost}
n14-requisition-transcript-sold = Sold {$amount}x {$item} for ${$cost}
