import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class TabChangeService {
  private tabChangedSource = new Subject<number>();
  tabChanged$ = this.tabChangedSource.asObservable();

  notifyTabChange(index: number) {
    this.tabChangedSource.next(index);
  }
}
