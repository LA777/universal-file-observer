import { Component, OnInit, Input, ViewChild, ViewEncapsulation, ViewChildren, QueryList } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatTabGroup, MatTabsModule } from '@angular/material/tabs';
import { MatTabChangeEvent } from '@angular/material/tabs';
import { TabChangeService } from './services/tab-change.service';
import { SnapshotComponent } from './components/snapshot/snapshot.component';
import { ForecastComponent } from './components/forecast/forecast.component';
import { SnapshotsComponent } from './components/snapshots/snapshots.component';
import { FilesComponent } from './components/files/files.component';
import { FolderTreeComponent } from './components/folder-tree/folder-tree.component';
import { DialogComponent } from './components/dialog/dialog.component';

// Material imports
import { DragDropModule } from '@angular/cdk/drag-drop';
import { PortalModule } from '@angular/cdk/portal';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { CdkStepperModule } from '@angular/cdk/stepper';
import { CdkTableModule } from '@angular/cdk/table';
import { CdkTreeModule } from '@angular/cdk/tree';
import { OverlayModule } from '@angular/cdk/overlay';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialogModule } from '@angular/material/dialog';
import { MatGridListModule } from '@angular/material/grid-list';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatNativeDateModule, MatRippleModule } from '@angular/material/core';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule as MatTabsModule2 } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatTreeModule } from '@angular/material/tree';
import { MatProgressBarModule } from '@angular/material/progress-bar';

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}

@Component({
    selector: 'app-root',
    templateUrl: './app.component.html',
    styleUrl: './app.component.css',
    encapsulation: ViewEncapsulation.Emulated,
    standalone: true,
    imports: [
      CommonModule,
      RouterModule,
      MatTabsModule,
      DragDropModule,
      PortalModule,
      ScrollingModule,
      CdkStepperModule,
      CdkTableModule,
      CdkTreeModule,
      OverlayModule,
      MatButtonModule,
      MatButtonToggleModule,
      MatDialogModule,
      MatGridListModule,
      MatIconModule,
      MatInputModule,
      MatListModule,
      MatNativeDateModule,
      MatRippleModule,
      MatSelectModule,
      MatSortModule,
      MatTableModule,
      MatTabsModule2,
      MatTooltipModule,
      MatTreeModule,
      MatProgressBarModule,
      ForecastComponent,
      SnapshotComponent,
      SnapshotsComponent,
      FilesComponent,
      FolderTreeComponent
    ]
})
export class AppComponent implements OnInit {
  public forecasts: WeatherForecast[] = [];
  title = 'ufo.client';
  @Input() selectedIndex: number | 0;// The index of the active tab.
  @ViewChild('tabGroup') tabGroup: MatTabGroup;
  @ViewChildren(SnapshotComponent) tabComponents: QueryList<SnapshotComponent>;

  constructor(private tabChangeService: TabChangeService) {}

  ngOnInit() {
  }

  tabChange(index: number) {
    this.tabGroup.selectedIndex = index;
  }

  onTabChange(index: number) {
    const selectedTabComponent = this.tabComponents.toArray()[index];
    if (selectedTabComponent && selectedTabComponent.getLatestSnapshot) {
      selectedTabComponent.getLatestSnapshot();
    }
  }
}
