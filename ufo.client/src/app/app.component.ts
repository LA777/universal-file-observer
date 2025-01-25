import { Component, OnInit, Input, ViewChild } from '@angular/core';
import { MatTabGroup } from '@angular/material/tabs';

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  public forecasts: WeatherForecast[] = [];
  title = 'ufo.client';
  @Input() selectedIndex: number | 0;// The index of the active tab.
  @ViewChild('tabGroup') tabGroup: MatTabGroup;

  constructor() {}

  ngOnInit() {
  }

  onTabChange(index: number) {
    this.tabGroup.selectedIndex = index;
  }
}
