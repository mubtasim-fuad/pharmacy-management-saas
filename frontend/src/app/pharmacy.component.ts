import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';

interface Medicine { id: number; name: string; stock: number; }
const api = 'http://localhost:5075/api/medicines';
const samples: Medicine[] = [
  { id: 1, name: 'Paracetamol', stock: 12 },
  { id: 2, name: 'Cetirizine', stock: 3 },
  { id: 3, name: 'ORS', stock: 0 }
];

@Component({ selector: 'pharmacy-app', standalone: true, imports: [ReactiveFormsModule], templateUrl: './pharmacy.component.html' })
export class PharmacyComponent {
  private readonly http = inject(HttpClient);
  readonly medicines = signal<Medicine[]>(samples);
  readonly demoMode = signal(true);
  readonly error = signal('');
  readonly query = signal('');
  readonly filtered = computed(() => this.medicines().filter(m => m.name.toLowerCase().includes(this.query().trim().toLowerCase())));
  readonly totalStock = computed(() => this.medicines().reduce((sum, m) => sum + m.stock, 0));
  readonly lowStock = computed(() => this.medicines().filter(m => m.stock < 5).length);
  readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(160), Validators.pattern(/.*\S.*/)] }),
    stock: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)] })
  });

  constructor() { this.reload(); }
  reload() {
    this.http.get<Medicine[]>(api).subscribe({
      next: data => { this.medicines.set(data); this.demoMode.set(false); this.error.set(''); },
      error: () => { this.demoMode.set(true); this.error.set(''); }
    });
  }
  search(event: Event) { this.query.set((event.target as HTMLInputElement).value); }
  addMedicine() {
    if (this.form.invalid) return;
    const { name, stock } = this.form.getRawValue();
    if (!name.trim() || !Number.isInteger(stock) || stock < 0) return;
    const request = { name: name.trim(), stock };
    if (this.demoMode()) {
      this.medicines.update(rows => [...rows, { id: Math.max(0, ...rows.map(m => m.id)) + 1, ...request }]);
      this.form.reset({ name: '', stock: 0 });
      return;
    }
    this.http.post<Medicine>(api, request).subscribe({
      next: medicine => { this.medicines.update(rows => [...rows, medicine]); this.form.reset({ name: '', stock: 0 }); this.error.set(''); },
      error: () => this.error.set('Could not add medicine. Check the API and database.')
    });
  }
  sell(id: number) {
    const medicine = this.medicines().find(m => m.id === id);
    if (!medicine || medicine.stock === 0) return;
    if (this.demoMode()) {
      this.medicines.update(rows => rows.map(m => m.id === id ? { ...m, stock: m.stock - 1 } : m));
      return;
    }
    this.http.patch<Medicine>(`${api}/${id}/sell`, {}).subscribe({
      next: updated => { this.medicines.update(rows => rows.map(m => m.id === id ? updated : m)); this.error.set(''); },
      error: () => this.error.set('Could not complete the sale. Refresh and try again.')
    });
  }
}
