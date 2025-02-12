import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';

import {DragDropModule} from '@angular/cdk/drag-drop';
import {PortalModule} from '@angular/cdk/portal';
import {ScrollingModule} from '@angular/cdk/scrolling';
import {CdkStepperModule} from '@angular/cdk/stepper';
import {CdkTableModule} from '@angular/cdk/table';
import {CdkTreeModule} from '@angular/cdk/tree';
import {OverlayModule} from '@angular/cdk/overlay';

//import {MatAutocompleteModule} from '@angular/material/autocomplete';
//import {MatBadgeModule} from '@angular/material/badge';
//import {MatBottomSheetModule} from '@angular/material/bottom-sheet';
import {MatButtonModule} from '@angular/material/button';
import {MatButtonToggleModule} from '@angular/material/button-toggle';
//import {MatCardModule} from '@angular/material/card';
//import {MatCheckboxModule} from '@angular/material/checkbox';
//import {MatChipsModule} from '@angular/material/chips';
//import {MatStepperModule} from '@angular/material/stepper';
//import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatDialogModule} from '@angular/material/dialog';
//import {MatDividerModule} from '@angular/material/divider';
//import {MatExpansionModule} from '@angular/material/expansion';
import {MatGridListModule} from '@angular/material/grid-list';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatListModule} from '@angular/material/list';
//import {MatMenuModule} from '@angular/material/menu';
import {MatNativeDateModule, MatRippleModule} from '@angular/material/core';
//import {MatPaginatorModule} from '@angular/material/paginator';
//import {MatProgressBarModule} from '@angular/material/progress-bar';
//import {MatProgressSpinnerModule} from '@angular/material/progress-spinner';
//import {MatRadioModule} from '@angular/material/radio';
import {MatSelectModule} from '@angular/material/select';
//import {MatSidenavModule} from '@angular/material/sidenav';
//import {MatSliderModule} from '@angular/material/slider';
//import {MatSlideToggleModule} from '@angular/material/slide-toggle';
//import {MatSnackBarModule} from '@angular/material/snack-bar';
import {MatSortModule} from '@angular/material/sort';
import {MatTableModule} from '@angular/material/table';
import {MatTabsModule} from '@angular/material/tabs';
//import {MatToolbarModule} from '@angular/material/toolbar';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatTreeModule} from '@angular/material/tree';

import { AppComponent } from './app.component';

import { ForecastComponent } from './components/forecast/forecast.component';
import { SnapshotComponent } from './components/snapshot/snapshot.component';
import { SnapshotsComponent } from './components/snapshots/snapshots.component';
import { FilesComponent } from './components/files/files.component';
import { DialogComponent } from './components/dialog/dialog.component';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

@NgModule({ declarations: [
        AppComponent,
        ForecastComponent,
        SnapshotComponent,
        SnapshotsComponent,
        FilesComponent,
        DialogComponent
    ],
    bootstrap: [AppComponent], imports: [BrowserModule,
        CdkStepperModule,
        CdkTableModule,
        CdkTreeModule,
        DragDropModule,
        PortalModule,
        ScrollingModule,
        OverlayModule,
        //MatAutocompleteModule,
        //MatBadgeModule,
        //MatBottomSheetModule,
        MatButtonModule,
        MatButtonToggleModule,
        //MatCardModule,
        //MatCheckboxModule,
        //MatChipsModule,
        //MatStepperModule,
        //MatDatepickerModule,
        MatDialogModule,
        //MatDividerModule,
        //MatExpansionModule,
        MatGridListModule,
        MatIconModule,
        MatInputModule,
        MatListModule,
        // MatMenuModule,
        MatNativeDateModule,
        //MatPaginatorModule,
        //MatProgressBarModule,
        //MatProgressSpinnerModule,
        //MatRadioModule,
        MatRippleModule,
        MatSelectModule,
        //MatSidenavModule,
        //MatSliderModule,
        //MatSlideToggleModule,
        //MatSnackBarModule,
        MatSortModule,
        MatTableModule,
        MatTabsModule,
        //MatToolbarModule,
        MatTooltipModule,
        MatTreeModule], providers: [
        provideAnimationsAsync(),
        provideHttpClient(withInterceptorsFromDi())
    ] })
export class AppModule { }
