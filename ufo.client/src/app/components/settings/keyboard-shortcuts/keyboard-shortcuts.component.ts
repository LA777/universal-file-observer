import { Component, OnInit, ChangeDetectionStrategy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { KeyBinding, KeyBindingUpdate } from '../../../models/models';
import { KeyBindingsService } from '../../../services/key-bindings.service';
import { chordFromEvent, describeChord } from '../../../shared/key-chord';
import { describeHttpError } from '../../../shared/http-error';

/** Which slot of which action is being recorded into. */
export type BindingSlot = 'primaryKey' | 'secondaryKey';

interface SlotAddress {
  actionId: string;
  slot: BindingSlot;
}

/** The actions of one group, under the heading they share. */
interface BindingGroup {
  name: string;
  bindings: KeyBinding[];
}

/**
 * The keyboard-shortcuts table on the Settings page.
 *
 * Two slots per action, because one key is rarely enough to satisfy both the
 * function-key convention and the key everyone's hand already reaches for -
 * Delete answers to F8 and to Del, and neither has to lose.
 *
 * Edits are held here until Save. A table where every keystroke went straight to
 * the server could not express a swap: giving F5 to Move while Copy still holds
 * it is a conflict on the way to a perfectly good arrangement.
 */
@Component({
  selector: 'app-keyboard-shortcuts',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule],
  templateUrl: './keyboard-shortcuts.component.html',
  styleUrl: './keyboard-shortcuts.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class KeyboardShortcutsComponent implements OnInit {
  /** The table as edited, which is the saved list until the user touches it. */
  readonly bindings = signal<KeyBinding[]>([]);

  /** The slot listening for a keypress, or null when none is. */
  readonly capturingSlot = signal<SlotAddress | null>(null);

  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly errorMessage = signal('');
  readonly savedMessage = signal('');

  /** Grouped for rendering, in the order the server listed them. */
  readonly groups = computed<BindingGroup[]>(() => {
    const groups: BindingGroup[] = [];

    for (const binding of this.bindings()) {
      const existingGroup = groups.find(group => group.name === binding.group);

      if (existingGroup) {
        existingGroup.bindings.push(binding);
      } else {
        groups.push({ name: binding.group, bindings: [binding] });
      }
    }

    return groups;
  });

  /**
   * Every chord claimed by more than one action.
   *
   * Computed over the whole table rather than checked as each key is captured,
   * so the moment the user resolves a clash it stops being reported - including
   * when they resolve it by changing the *other* action.
   */
  readonly conflictingChords = computed<ReadonlySet<string>>(() => {
    const owners = new Map<string, string>();
    const conflicts = new Set<string>();

    for (const binding of this.bindings()) {
      for (const chord of [binding.primaryKey, binding.secondaryKey]) {
        if (!chord) {
          continue;
        }

        const existingOwner = owners.get(chord);

        if (existingOwner !== undefined && existingOwner !== binding.actionId) {
          conflicts.add(chord);
        } else {
          owners.set(chord, binding.actionId);
        }
      }
    }

    return conflicts;
  });

  readonly hasConflicts = computed(() => this.conflictingChords().size > 0);

  /** Whether anything differs from what is on the server. */
  readonly hasChanges = signal(false);

  constructor(private keyBindingsService: KeyBindingsService) {}

  ngOnInit(): void {
    this.keyBindingsService.load().subscribe({
      next: keyBindings => {
        this.bindings.set(keyBindings.map(keyBinding => ({ ...keyBinding })));
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(describeHttpError(error, { action: 'load the keyboard shortcuts' }).message);
        this.isLoading.set(false);
      },
    });
  }

  describe(chord: string): string {
    return describeChord(chord);
  }

  isCapturing(actionId: string, slot: BindingSlot): boolean {
    const capturing = this.capturingSlot();

    return capturing?.actionId === actionId && capturing.slot === slot;
  }

  isConflicting(chord: string): boolean {
    return this.conflictingChords().has(chord);
  }

  /**
   * Arms a slot. The next keypress becomes the binding.
   *
   * Focus is taken explicitly rather than left to the click. Safari and Firefox
   * on macOS do not focus a button when it is clicked, and an armed slot that
   * never receives a keydown is a page where recording silently does nothing.
   */
  startCapturing(event: Event, actionId: string, slot: BindingSlot): void {
    this.savedMessage.set('');
    this.capturingSlot.set({ actionId, slot });
    (event.currentTarget as HTMLElement | null)?.focus();
  }

  stopCapturing(): void {
    this.capturingSlot.set(null);
  }

  /**
   * Records a keypress into the armed slot.
   *
   * Only while that slot is the one listening. The slot keeps focus after it has
   * recorded something, so a handler that swallowed everything would rebind the
   * action to Tab the moment the user tried to leave, and there would be no way
   * out of the table at all. Unarmed, every key is left to the browser - which is
   * also what lets Enter and Space arm the slot, since they reach it as a click.
   */
  onSlotKeyDown(event: KeyboardEvent, actionId: string, slot: BindingSlot): void {
    if (!this.isCapturing(actionId, slot)) {
      return;
    }

    // Armed, everything is swallowed - including Tab and the function keys the
    // browser has its own ideas about. A shortcuts page that let F5 through
    // would reload itself instead of recording it.
    event.preventDefault();
    event.stopPropagation();

    if (event.key === 'Escape') {
      this.stopCapturing();
      return;
    }

    // Backspace clears the slot: there has to be a way back to None, and every
    // other key is a candidate binding rather than a command.
    if (event.key === 'Backspace') {
      this.applyChord(actionId, slot, '');
      return;
    }

    const chord = chordFromEvent(event);

    // A bare modifier is not a chord yet; the user is still reaching for the
    // key that goes with it, so the slot keeps listening.
    if (chord !== null) {
      this.applyChord(actionId, slot, chord);
    }
  }

  clearSlot(actionId: string, slot: BindingSlot): void {
    this.applyChord(actionId, slot, '');
  }

  /** Puts one action back to what this build ships with. */
  resetAction(actionId: string): void {
    this.updateBinding(actionId, binding => ({
      ...binding,
      primaryKey: binding.defaultPrimaryKey,
      secondaryKey: binding.defaultSecondaryKey,
    }));
  }

  /** Puts the whole table back. Takes effect on Save like any other edit. */
  resetAll(): void {
    this.savedMessage.set('');
    this.bindings.update(bindings =>
      bindings.map(binding => ({
        ...binding,
        primaryKey: binding.defaultPrimaryKey,
        secondaryKey: binding.defaultSecondaryKey,
      })),
    );
    this.hasChanges.set(true);
  }

  isActionDefault(binding: KeyBinding): boolean {
    return (
      binding.primaryKey === binding.defaultPrimaryKey && binding.secondaryKey === binding.defaultSecondaryKey
    );
  }

  save(): void {
    // The server refuses a duplicated chord too; this is here so the answer
    // arrives before the round trip rather than as a rejected save.
    if (this.hasConflicts() || this.isSaving()) {
      return;
    }

    const updates: KeyBindingUpdate[] = this.bindings().map(binding => ({
      actionId: binding.actionId,
      primaryKey: binding.primaryKey,
      secondaryKey: binding.secondaryKey,
    }));

    this.isSaving.set(true);
    this.errorMessage.set('');
    this.savedMessage.set('');

    this.keyBindingsService.save(updates).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.hasChanges.set(false);
        this.savedMessage.set('Shortcuts saved.');
      },
      error: (error: unknown) => {
        this.isSaving.set(false);
        this.errorMessage.set(describeHttpError(error, { action: 'save the keyboard shortcuts' }).message);
      },
    });
  }

  private applyChord(actionId: string, slot: BindingSlot, chord: string): void {
    this.updateBinding(actionId, binding => {
      const updated = { ...binding, [slot]: chord };

      // The same chord in both slots of one action is a duplicate, not a second
      // way of doing it, so filling one clears the other.
      if (chord && slot === 'primaryKey' && updated.secondaryKey === chord) {
        updated.secondaryKey = '';
      } else if (chord && slot === 'secondaryKey' && updated.primaryKey === chord) {
        updated.primaryKey = '';
      }

      return updated;
    });

    this.stopCapturing();
  }

  private updateBinding(actionId: string, change: (binding: KeyBinding) => KeyBinding): void {
    this.savedMessage.set('');
    this.bindings.update(bindings =>
      bindings.map(binding => (binding.actionId === actionId ? change(binding) : binding)),
    );
    this.hasChanges.set(true);
  }
}
