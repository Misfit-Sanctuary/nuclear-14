ent-PrizeTicket = caravan ticket
   .desc = A ticket used for exchange with a special "trading machine". Lets you get fairly powerful weapons if you have enough tickets.
ent-PrizeTicket1 = { ent-PrizeTicket }
   .suffix = 1
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket10  = { ent-PrizeTicket }
   .suffix = 10
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket30  = { ent-PrizeTicket }
   .suffix = 30
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket60  = { ent-PrizeTicket }
   .suffix = 60
   .desc = { ent-PrizeTicket.desc }
nc-store-window-title = Trading Terminal
nc-store-select-category = Select a category
nc-store-search-placeholder = Search items...
nc-store-footer-balance = Balance:
nc-store-tab-buy = Buy
nc-store-tab-sell = Sell
nc-store-tab-contracts = Contracts
nc-store-cat-ready-short = Ready
nc-store-cat-crate-short = In crate
nc-store-cat-ready-full = Ready for sale
nc-store-cat-crate-full = Ready for sale (in crate)
nc-store-category-fallback = Misc
nc-store-mass-sell-button = Sell crate contents
nc-store-mass-sell-tooltip = Option for quickly selling all contents.
    Conditions:
    - The crate must be closed
    - You must be pulling the crate
nc-store-mass-sell-tooltip-with-reward = { nc-store-mass-sell-tooltip }

    Estimated value: { $reward }
nc-store-only-mass-sell = This item can only be sold in bulk via a closed crate.
nc-store-show-more = Show more ({ $count })
nc-store-prompt-select-category = Please select a category on the left.
nc-store-empty-search = No items found for your search.
nc-store-empty-category-search = This category has no items matching your search.
nc-store-search-results-buy = Search results (Buy): { $count }
nc-store-search-results-sell = Search results (Sell): { $count }
nc-store-no-stock = Out of stock
nc-store-buying-finished = Limit reached
nc-store-remaining = Remaining: { $count }
nc-store-will-buy = Required: { $count }
nc-store-owned = You have: { $count }
nc-store-no-access = Access error
nc-store-contracts-empty = No active contracts yet. Check back later.
nc-store-difficulty-easy = Easy
nc-store-difficulty-medium = Medium
nc-store-difficulty-hard = Hard
nc-store-difficulty-bronze = Bronze
nc-store-difficulty-iron = Iron
nc-store-difficulty-silver = Silver
nc-store-difficulty-gold = Gold
nc-store-difficulty-mithril = Mithril
nc-store-difficulty-diamond = Diamond
nc-store-contract-title = Contract ({ $difficulty })
nc-store-contract-badge-single = One-time
nc-store-contract-badge-single-tooltip =
    This contract can be completed only once per shift.
    After completion it disappears from the list.
nc-store-contract-goals-header = Order goals:
nc-store-contract-reward-header = Reward:
nc-store-contract-items-header = Items:
nc-store-contract-action-claim = Complete contract
nc-store-contract-action-claim-progress = Submit part ({ $progress }/{ $required })
nc-store-contract-action-can-claim = Ready to turn in
nc-store-contract-action-not-done = Not completed
nc-store-contract-claim-tooltip-single = Complete a one-time contract and receive the full reward.
nc-store-contract-claim-tooltip-repeatable = Turn in current progress on the contract and receive the reward.
nc-store-contract-claim-tooltip-not-done = Contract conditions are not met yet. Not enough items.
nc-store-contract-completed = Contract completed successfully!
nc-store-contract-goal-line = { $item }: { $count } pcs.
nc-store-contract-goal-progress-line = { $item }: { $progress }/{ $count } pcs.
nc-store-contract-progress-line = Completion progress: { $progress } of { $required }
nc-store-currency-format = { $amount } { $currency }
nc-store-contract-title-pretty = Contract: { $difficulty } - { $goal }
nc-store-contract-title-pretty-nogoal = Contract: { $difficulty }

nc-store-contract-desc-default = Fulfill the contract requirements and claim the reward.

# Misfits Add — 6-tier contract system UI strings
nc-contract-tier-first-access = You've registered with the caravan network — Bronze tier unlocked!
nc-contract-tier-unlocked = { $tier } tier unlocked! Keep pushing.
nc-store-tab-tier-hall-of-fame = Hall of Fame
nc-store-hall-of-fame-empty = No one has completed contracts yet this round.
nc-store-hall-of-fame-entry = { $name } — { $tier } ({ $count } completed)
nc-contract-tier-locked = Locked — complete { $prevTier } tier contracts to unlock.
nc-contract-card-locked-hint = { $tier } tier — not yet unlocked.
nc-store-contract-desc-generated = Required: { $goals }

nc-store-contract-goal-inline = { $item } x{ $count }

nc-store-unknown-item = ???

nc-store-proto-tooltip-name-only = { $name }
nc-store-proto-tooltip = { $name }
    { $desc }

nc-store-contract-reward-none = No reward specified
nc-store-contract-reward-item-line = { $item } x{ $count }

nc-store-contract-badge-completed = COMPLETED
nc-store-contract-badge-completed-tooltip = Contract complete - you can claim the reward.

# #Misfits Add - Store popup messages for UI open feedback
nc-store-popup-no-access = You don't have access to this terminal.
nc-store-popup-too-far = You are too far from the terminal.
nc-store-popup-in-use = This terminal is currently in use.
nc-store-popup-crate-open = Close the crate first.
nc-store-popup-no-crate = You need to pull a closed crate to sell.
nc-store-popup-invalid-listing = Invalid listing.
nc-store-popup-transaction-failed = Transaction failed.
nc-store-popup-crate-too-far = The crate is too far away.
