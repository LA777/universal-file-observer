import { bootstrapApplication } from '@angular/platform-browser';
import { provideZoneChangeDetection } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient, withInterceptorsFromDi, HTTP_INTERCEPTORS, withXhr } from '@angular/common/http';
import { provideRouter, Routes } from '@angular/router';
import { AppComponent } from './app/app.component';
import { LoginComponent } from './app/components/login/login.component';
import { RegisterComponent } from './app/components/register/register.component';
import { DashboardComponent } from './app/components/dashboard/dashboard.component';
import { SettingsComponent } from './app/components/settings/settings.component';
import { AuthGuard } from './app/guards/auth.guard';
import { RootRedirectGuard } from './app/guards/root-redirect.guard';
import { JwtInterceptor } from './app/interceptors/jwt.interceptor';

const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'dashboard', canActivate: [AuthGuard], component: DashboardComponent },
  { path: 'settings', canActivate: [AuthGuard], component: SettingsComponent },
  // Componentless, and the guard always answers with a UrlTree: "/" is a
  // decision about where to send someone, not a page of its own.
  { path: '', pathMatch: 'full', canActivate: [RootRedirectGuard], children: [] }
];

bootstrapApplication(AppComponent, {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideAnimationsAsync(),
    provideHttpClient(withXhr(), withInterceptorsFromDi()),
    provideRouter(routes),
    { provide: HTTP_INTERCEPTORS, useClass: JwtInterceptor, multi: true },
    AuthGuard
  ]
}).catch(err => console.error(err));
