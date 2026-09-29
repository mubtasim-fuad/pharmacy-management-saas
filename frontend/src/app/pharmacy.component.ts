import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';

type View = 'overview' | 'medicines' | 'purchase' | 'sales' | 'reports';
interface Medicine { id: number; name: string; sku: string; stock: number; reorderLevel: number; salePrice: number; costPrice: number; }
interface Line { medicineId: number; medicineName: string; quantity: number; unitPrice: number; }
interface Sale { id: number; customer: string; total: number; createdAtUtc: string; items: Line[]; }
interface Purchase { id: number; supplier: string; total: number; createdAtUtc: string; items: Line[]; }
interface Summary { days: number; medicines: number; unitsInStock: number; lowStock: number; stockValue: number; salesCount: number; revenue: number; costOfGoods: number; purchasesTotal: number; }

const api = 'http://localhost:5075';

@Component({ selector: 'pharmacy-app', standalone: true, imports: [ReactiveFormsModule], templateUrl: './pharmacy.component.html' })
export class PharmacyComponent {
  private readonly http = inject(HttpClient);
  readonly today = new Date().toISOString();
  readonly view = signal<View>('overview');
  readonly medicines = signal<Medicine[]>([]);
  readonly sales = signal<Sale[]>([]);
  readonly purchases = signal<Purchase[]>([]);
  readonly reportDays = signal(30);
  readonly report = signal<Summary | null>(null);
  readonly error = signal('');
  readonly notice = signal('');
  readonly busy = signal(false);
  readonly loading = signal(true);
  readonly query = signal('');
  readonly editingId = signal<number | null>(null);
  readonly filtered = computed(() => this.medicines().filter(m =>
    `${m.name} ${m.sku}`.toLowerCase().includes(this.query().trim().toLowerCase())));
  readonly lowItems = computed(() => this.medicines().filter(m => m.stock <= m.reorderLevel));
  readonly totalStock = computed(() => this.medicines().reduce((sum, m) => sum + m.stock, 0));
  readonly recent = computed(() => [...this.sales().map(s => ({ kind: 'Sale', id: s.id, title: s.customer, total: s.total, date: s.createdAtUtc })),
    ...this.purchases().map(p => ({ kind: 'Purchase', id: p.id, title: p.supplier, total: p.total, date: p.createdAtUtc }))]
    .sort((a, b) => b.date.localeCompare(a.date)).slice(0, 6));
  readonly medicineForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(160)] }),
    sku: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    salePrice: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0)] }),
    reorderLevel: new FormControl(5, { nonNullable: true, validators: [Validators.required, Validators.min(0)] })
  });
  readonly purchaseForm = new FormGroup({
    supplier: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(160)] }),
    medicineId: new FormControl(0, { nonNullable: true, validators: [Validators.min(1)] }),
    quantity: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1)] }),
    unitCost: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0)] })
  });
  readonly saleForm = new FormGroup({
    customer: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(160)] }),
    medicineId: new FormControl(0, { nonNullable: true, validators: [Validators.min(1)] }),
    quantity: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1)] })
  });
  saleChoice() { return this.medicines().find(m => m.id === Number(this.saleForm.controls.medicineId.value)); }

  constructor() { this.reload(); }

  money(amount: number) { return '৳' + Number(amount).toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
  date(value: string) { return new Date(value).toLocaleString('en-BD', { dateStyle: 'medium', timeStyle: 'short' }); }
  search(event: Event) { this.query.set((event.target as HTMLInputElement).value); }
  show(view: View) { this.view.set(view); this.error.set(''); this.notice.set(''); }
  chooseDays(event: Event) { this.reportDays.set(Number((event.target as HTMLSelectElement).value)); this.loadReport(); }
  refresh() { this.error.set(''); this.notice.set(''); this.reload(); }
  private message(err: unknown, fallback: string) {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 0) return 'Cannot connect to the API. Start it with dotnet run in the backend folder, then select Retry.';
      return typeof err.error?.error === 'string' ? err.error.error : fallback;
    }
    return fallback;
  }
  private fail(err: unknown, fallback: string) { this.busy.set(false); this.error.set(this.message(err, fallback)); }
  private success(message: string) { this.busy.set(false); this.error.set(''); this.notice.set(message); }

  reload() {
    this.loading.set(true);
    forkJoin({
      medicines: this.http.get<Medicine[]>(`${api}/api/medicines`),
      sales: this.http.get<Sale[]>(`${api}/api/sales`),
      purchases: this.http.get<Purchase[]>(`${api}/api/purchases`)
    }).subscribe({
      next: data => {
        this.medicines.set(data.medicines);
        this.sales.set(data.sales);
        this.purchases.set(data.purchases);
        this.loading.set(false);
        this.error.set('');
        this.loadReport();
      },
      error: err => { this.loading.set(false); this.fail(err, 'Could not load pharmacy records.'); }
    });
  }
  loadReport() {
    this.http.get<Summary>(`${api}/api/reports/summary?days=${this.reportDays()}`)
      .subscribe({ next: data => this.report.set(data), error: err => this.fail(err, 'Could not load report.') });
  }
  edit(m: Medicine) {
    this.editingId.set(m.id);
    this.medicineForm.setValue({ name: m.name, sku: m.sku, salePrice: m.salePrice, reorderLevel: m.reorderLevel });
    this.view.set('medicines');
  }
  cancelEdit() { this.editingId.set(null); this.medicineForm.reset({ name: '', sku: '', salePrice: 0, reorderLevel: 5 }); }
  saveMedicine() {
    if (this.medicineForm.invalid || this.busy()) return;
    const value = this.medicineForm.getRawValue();
    if (!Number.isInteger(value.reorderLevel) || value.reorderLevel > 1_000_000 ||
        value.salePrice > 9_999_999_999.99 || !value.name.trim()) {
      this.error.set('Check the name, price and reorder level.'); return;
    }
    const payload = { ...value, name: value.name.trim(), sku: value.sku.trim() };
    const id = this.editingId();
    this.busy.set(true);
    const request = id === null
      ? this.http.post<Medicine>(`${api}/api/medicines`, payload)
      : this.http.put<Medicine>(`${api}/api/medicines/${id}`, payload);
    request.subscribe({
      next: () => { this.cancelEdit(); this.success(id === null ? 'Medicine added.' : 'Medicine updated.'); this.reload(); },
      error: err => this.fail(err, 'Could not save medicine.')
    });
  }
  savePurchase() {
    if (this.purchaseForm.invalid || this.busy()) return;
    const { supplier, medicineId, quantity, unitCost } = this.purchaseForm.getRawValue();
    const id = Number(medicineId);
    if (!this.medicines().some(m => m.id === id) || !Number.isInteger(quantity) || quantity > 100_000 ||
        unitCost * quantity > 9_999_999_999.99) {
      this.error.set('Choose a medicine and valid quantity and cost.'); return;
    }
    this.busy.set(true);
    this.http.post<Purchase>(`${api}/api/purchases`, { supplier: supplier.trim(), items: [{ medicineId: id, quantity, unitCost }] }).subscribe({
      next: () => { this.purchaseForm.reset({ supplier: '', medicineId: 0, quantity: 1, unitCost: 0 }); this.success('Purchase recorded and stock increased.'); this.reload(); },
      error: err => this.fail(err, 'Could not record purchase.')
    });
  }
  saveSale() {
    if (this.saleForm.invalid || this.busy()) return;
    const { customer, medicineId, quantity } = this.saleForm.getRawValue();
    const id = Number(medicineId), medicine = this.saleChoice();
    if (!medicine || !Number.isInteger(quantity) || quantity > 100_000 || medicine.stock < quantity) {
      this.error.set('Choose an in-stock medicine and a valid quantity.'); return;
    }
    this.busy.set(true);
    this.http.post<Sale>(`${api}/api/sales`, { customer: customer.trim(), items: [{ medicineId: id, quantity }] }).subscribe({
      next: () => { this.saleForm.reset({ customer: '', medicineId: 0, quantity: 1 }); this.success('Sale recorded and stock deducted.'); this.reload(); },
      error: err => this.fail(err, 'Could not record sale.')
    });
  }
}
