import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FilePanelComponent } from '../file-panel/file-panel.component';

type PanelSide = 'left' | 'right';

@Component({
  selector: 'app-files',
  standalone: true,
  templateUrl: './files.component.html',
  styleUrl: './files.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [CommonModule, FilePanelComponent]
})
export class FilesComponent {
  activePanel: PanelSide = 'left';

  /**
   * Where each panel is standing.
   *
   * Held here rather than read off the panels because each one is the other's
   * destination: Copy and Move send the selection to the folder the opposite
   * panel has open, so each needs to know a path it does not own. Null until that
   * panel has finished its first listing.
   */
  leftPath: string | null = null;
  rightPath: string | null = null;

  setActive(side: PanelSide) {
    this.activePanel = side;
  }
}
