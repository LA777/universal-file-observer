import { Component, OnInit, ViewChild, OnDestroy, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabGroup, MatTabsModule } from '@angular/material/tabs';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { TabChangeService } from '../../services/tab-change.service';
import { AuthService } from '../../services/auth.service';
import { SnapshotComponent } from '../snapshot/snapshot.component';
import { SnapshotsComponent } from '../snapshots/snapshots.component';
import { FilesComponent } from '../files/files.component';
import { LabelsComponent } from '../labels/labels.component';
import { SearchComponent } from '../search/search.component';

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

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MatTabsModule,
    MatButtonModule,
    MatIconModule,
    DragDropModule,
    PortalModule,
    ScrollingModule,
    CdkStepperModule,
    CdkTableModule,
    CdkTreeModule,
    OverlayModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatGridListModule,
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
    SnapshotComponent,
    SnapshotsComponent,
    FilesComponent,
    LabelsComponent,
    SearchComponent
  ],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
  @ViewChild('tabGroup') tabGroup: MatTabGroup;
  private destroy$ = new Subject<void>();

  constructor(
    private tabChangeService: TabChangeService,
    private authService: AuthService,
    private router: Router
  ) { }

  ngOnInit() {
    // Check if user is authenticated
    if (!this.authService.isAuthenticated) {
      console.log('User not authenticated, navigating to login');
      this.router.navigate(['/login']).then(success => {
        console.log('Navigation to login:', success ? 'successful' : 'failed');
      });
      return;
    }

    // Subscribe to auth state changes and redirect if user logs out
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe(user => {
        console.log('Current user:', user);
        if (!user) {
          // User has logged out, redirect to login
          console.log('User logged out, navigating to login');
          this.router.navigate(['/login']).then(success => {
            console.log('Navigation to login:', success ? 'successful' : 'failed');
          });
        }
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onTabChange(index: number) {
    this.tabChangeService.notifyTabChange(index);
  }

  tabChange(index: number) {
    if (this.tabGroup) {
      this.tabGroup.selectedIndex = index;
    }
  }
}

