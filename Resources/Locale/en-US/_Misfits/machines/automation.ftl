signal-port-name-powered = Powered
signal-port-description-powered = This port is invoked with HIGH or LOW depending on the machine's power being switched on or off.

# Robotic Arm

signal-port-name-input-machine = Item: Input Machine
signal-port-description-input-machine = A machine automation slot to take items out of, instead of taking them from the floor.

signal-port-name-output-machine = Item: Output Machine
signal-port-description-output-machine = A machine automation slot to insert items into, instead of placing them on the floor.

signal-port-name-item-moved = Item Moved
signal-port-description-item-moved = Signal port that gets pulsed after an item is moved by this arm.

signal-port-name-automation-slot-filter = Item: Filter Slot
signal-port-description-automation-slot-filter = An automation slot for an automation machine's filter.

# Storage Bin

signal-port-name-automation-slot-storage = Item: Storage
signal-port-description-automation-slot-storage = An automation slot for a storage bin's inventory.

signal-port-name-storage-inserted = Inserted
signal-port-description-storage-inserted = Signal port that gets pulsed after an item is inserted into a storage bin.

signal-port-name-storage-removed = Removed
signal-port-description-storage-removed = Signal port that gets pulsed after an item is removed from a storage bin.

# Constructor / Interactor

signal-port-name-machine-start = Start
signal-port-description-machine-start = Signal port to start a machine once.

signal-port-name-machine-repeating = Repeating
signal-port-description-machine-repeating = Signal port to control starting after completing automatically.

signal-port-name-machine-started = Started
signal-port-description-machine-started = Signal port that gets pulsed after a machine starts.

signal-port-name-machine-completed = Completed
signal-port-description-machine-completed = Signal port that gets pulsed after a machine completes its work.

signal-port-name-machine-failed = Failed
signal-port-description-machine-failed = Signal port that gets pulsed after a machine fails to start.

# Interactor

signal-port-name-automation-slot-tool = Item: Tool
signal-port-description-automation-slot-tool = An automation slot for an interactor's held tool.

signal-port-name-alt-interact = Alt Interact Mode
signal-port-description-alt-interact = Signal port to toggle alt interact mode, or set it to a HIGH/LOW value.

signal-port-name-use-in-hand = Use In Hand Mode
signal-port-description-use-in-hand = Signal port to toggle use in hand mode, or set it to a HIGH/LOW value. This will ignore targets and use Z or Alt+Z on the held tool.

signal-port-name-harm-mode = Harm Mode
signal-port-description-harm-mode = Signal port to toggle harm mode, or set it to a HIGH/LOW value. This will hit the target with the held tool like the interactor is in harm mode.

signal-port-name-tool-locked = Tool Locked
signal-port-description-tool-locked = Signal port to toggle the tool lock, or set it to a HIGH/LOW value. Prevents the tool being dropped or picked up by any means.

# Plumbing Pump

signal-port-name-plumbing-input = Plumbing: Input
signal-port-description-plumbing-input = A plumbing automation slot to pump liquids into.

signal-port-name-plumbing-output = Plumbing: Output
signal-port-description-plumbing-output = A plumbing automation slot to pump liquids out of.

# Solution automation

signal-port-name-automation-slot-solution = Solution: Reagents
signal-port-description-automation-slot-solution = An automation slot for a machine's reagent solution, usable by liquid pumps.

# Lathe

signal-port-name-lathe-print = Print last recipe
signal-port-description-lathe-print = Signal port that prints the last set recipe when pulsed.

signal-port-name-lathe-set-recipe = Set lathe recipe
signal-port-description-lathe-set-recipe = Circuit port to set the current lathe recipe to a recipe prototype ID as a string.

signal-port-name-lathe-current-recipe = Lathe recipe
signal-port-description-lathe-current-recipe = Circuit port invoked with the current recipe prototype ID string, which will be used by automation.

signal-port-name-lathe-quantity = Item quantity
signal-port-description-lathe-quantity = Circuit port to set the next production count as an integer. Starts out as 1, lower values are ignored.

# Filter crafting

construction-graph-tag-health-analyzer = health analyzer
construction-graph-tag-cuffs = cuffs
