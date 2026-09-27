import { bootstrapApplication } from '@angular/platform-browser';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { PharmacyComponent } from './app/pharmacy.component';
bootstrapApplication(PharmacyComponent, { providers: [provideZonelessChangeDetection(), provideHttpClient()] }).catch(console.error);
