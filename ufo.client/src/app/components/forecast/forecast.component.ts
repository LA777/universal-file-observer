import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-forecast',
    templateUrl: './forecast.component.html',
    styleUrl: './forecast.component.css',
    standalone: true,
    imports: [CommonModule]
})
export class ForecastComponent implements OnInit {
  public forecasts: WeatherForecast[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.getForecasts();
  }

  getForecasts() {
    this.http.get<WeatherForecast[]>('/api/weatherforecast').subscribe(
      (result) => {
        this.forecasts = result;
        console.log(result);
      },
      (error) => {
        console.error(error);
      }
    );
  }
}

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}
