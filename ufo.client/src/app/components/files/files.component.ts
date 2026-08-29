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

  setActive(side: PanelSide) {
    this.activePanel = side;
  }
}
