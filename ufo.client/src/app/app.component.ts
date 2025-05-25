import { Component, OnInit, Input, ViewChild, ViewEncapsulation, ViewChildren, QueryList } from '@angular/core';
import { MatTabGroup } from '@angular/material/tabs';
import { MatTabChangeEvent } from '@angular/material/tabs';
import { TabChangeService } from './services/tab-change.service';
import { SnapshotComponent } from './components/snapshot/snapshot.component';

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
    standalone: false
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
