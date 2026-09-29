import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpHeaders, HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';

type View = 'overview' | 'medicines' | 'purchase' | 'sales' | 'reports';
type Mode = 'none' | 'demo' | 'api';
interface Medicine { id: number; name: string; sku: string; stock: number; reorderLevel: number; salePrice: number; costPrice: number; }
interface Line { medicineId: number; medicineName: string; quantity: number; unitPrice: number; unitCost?: number; }
interface Sale { id: number; customer: string; total: number; createdAtUtc: string; items: Line[]; }
interface Purchase { id: number; supplier: string; total: number; createdAtUtc: string; items: Line[]; }
interface Summary { days: number; medicines: number; unitsInStock: number; lowStock: number; stockValue: number; salesCount: number; revenue: number; costOfGoods: number; purchasesTotal: number; }
interface Auth { token: string; pharmacyName: string; email: string; }
interface DemoState { medicines: Medicine[]; sales: Sale[]; purchases: Purchase[]; }

// A deployment can set window.PHARMACY_API_URL in public/config.js. The hosted
// preview works locally without an API; that state never leaves this browser.
declare global { interface Window { PHARMACY_API_URL?: string; } }
const configuredApi = window.PHARMACY_API_URL?.replace(/\/$/, '') ||
  (location.hostname === 'localhost' ? 'http://localhost:5075' : '');
const demoKey = 'pharmacy-mvp-demo-v2';
const sample: DemoState = {
  medicines: [
    { id: 1, name: 'Paracetamol 500 mg', sku: 'MED-101', stock: 42, reorderLevel: 10, salePrice: 2.5, costPrice: 1.8 },
    { id: 2, name: 'Cetirizine 10 mg', sku: 'MED-102', stock: 8, reorderLevel: 10, salePrice: 5, costPrice: 3.5 },
    { id: 3, name: 'Oral Rehydration Salts', sku: 'MED-103', stock: 3, reorderLevel: 8, salePrice: 12, costPrice: 8 },
    { id: 4, name: 'Omeprazole 20 mg', sku: 'MED-104', stock: 0, reorderLevel: 10, salePrice: 8, costPrice: 5.25 }
  ],
  sales: [], purchases: []
};

@Component({ selector: 'pharmacy-app', standalone: true, imports: [ReactiveFormsModule], templateUrl: './pharmacy.component.html' })
export class PharmacyComponent {
  private readonly http = inject(HttpClient);
  readonly today = new Date().toISOString();
  readonly apiAvailable = !!configuredApi;
  readonly mode = signal<Mode>('none');
  readonly authTab = signal<'login' | 'register'>('login');
  readonly view = signal<View>('overview');
  readonly pharmacyName = signal('');
  readonly email = signal('');
  readonly token = signal('');
  readonly medicines = signal<Medicine[]>([]);
  readonly sales = signal<Sale[]>([]);
  readonly purchases = signal<Purchase[]>([]);
  readonly reportDays = signal(30);
  readonly report = signal<Summary | null>(null);
  readonly error = signal('');
  readonly notice = signal('');
  readonly busy = signal(false);
  readonly query = signal('');
  readonly editingId = signal<number | null>(null);
  readonly filtered = computed(() => this.medicines().filter(m =>
    `${m.name} ${m.sku}`.toLowerCase().includes(this.query().trim().toLowerCase())));
  readonly lowItems = computed(() => this.medicines().filter(m => m.stock <= m.reorderLevel));
  readonly totalStock = computed(() => this.medicines().reduce((sum, m) => sum + m.stock, 0));
  readonly recent = computed(() => [...this.sales().map(s => ({ kind: 'Sale', id: s.id, title: s.customer, total: s.total, date: s.createdAtUtc })),
    ...this.purchases().map(p => ({ kind: 'Purchase', id: p.id, title: p.supplier, total: p.total, date: p.createdAtUtc }))]
    .sort((a, b) => b.date.localeCompare(a.date)).slice(0, 6));
  readonly authForm = new FormGroup({
    pharmacyName: new FormControl('', { nonNullable: true }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] })
  });
  readonly medicineForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(160)] }),
    sku: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    salePrice: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0)] }),
    reorderLevel: new FormControl(5, { nonNullable: true, validators: [Validators.required, Validators.min(0)] })
  });
  readonly purchaseForm = new FormGroup({
    supplier: new FormControl('', { nonNullable: true }),
    medicineId: new FormControl(0, { nonNullable: true, validators: [Validators.min(1)] }),
    quantity: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1)] }),
    unitCost: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0)] })
  });
  readonly saleForm = new FormGroup({
    customer: new FormControl('', { nonNullable: true }),
    medicineId: new FormControl(0, { nonNullable: true, validators: [Validators.min(1)] }),
    quantity: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1)] })
  });
  saleChoice() { return this.medicines().find(m => m.id === Number(this.saleForm.controls.medicineId.value)); }

  constructor() {
    try {
      const stored = JSON.parse(sessionStorage.getItem('pharmacy-session') || 'null') as Auth | null;
      if (stored?.token && configuredApi) this.acceptAuth(stored);
    } catch { sessionStorage.removeItem('pharmacy-session'); }
  }

  money(amount: number) { return '৳' + Number(amount).toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
  date(value: string) { return new Date(value).toLocaleString('en-BD', { dateStyle: 'medium', timeStyle: 'short' }); }
  search(event: Event) { this.query.set((event.target as HTMLInputElement).value); }
  show(view: View) { this.view.set(view); this.error.set(''); this.notice.set(''); }
  chooseDays(event: Event) { this.reportDays.set(Number((event.target as HTMLSelectElement).value)); this.loadReport(); }
  private headers() { return { headers: new HttpHeaders({ Authorization: `Bearer ${this.token()}` }) }; }
  private message(err: unknown, fallback: string) {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 401) return 'Session expired. Sign in again.';
      return typeof err.error?.error === 'string' ? err.error.error : fallback;
    }
    return fallback;
  }
  private fail(err: unknown, fallback: string) { this.busy.set(false); this.error.set(this.message(err, fallback)); }
  private success(message: string) { this.busy.set(false); this.error.set(''); this.notice.set(message); }

  startDemo() {
    this.mode.set('demo'); this.pharmacyName.set('Demo pharmacy'); this.email.set('');
    try {
      const saved = JSON.parse(localStorage.getItem(demoKey) || 'null') as DemoState | null;
      this.medicines.set(Array.isArray(saved?.medicines) ? saved.medicines : structuredClone(sample.medicines));
      this.sales.set(Array.isArray(saved?.sales) ? saved.sales : []);
      this.purchases.set(Array.isArray(saved?.purchases) ? saved.purchases : []);
    } catch { this.medicines.set(structuredClone(sample.medicines)); this.sales.set([]); this.purchases.set([]); }
    this.view.set('overview'); this.loadReport(); this.notice.set('Demo changes are saved only in this browser.');
  }
  resetDemo() {
    localStorage.removeItem(demoKey);
    this.medicines.set(structuredClone(sample.medicines)); this.sales.set([]); this.purchases.set([]);
    this.loadReport(); this.success('Sample data restored.');
  }
  private persistDemo() {
    localStorage.setItem(demoKey, JSON.stringify({ medicines: this.medicines(), sales: this.sales(), purchases: this.purchases() }));
    this.loadReport();
  }
  signOut() {
    sessionStorage.removeItem('pharmacy-session'); this.token.set(''); this.mode.set('none');
    this.medicines.set([]); this.sales.set([]); this.purchases.set([]); this.report.set(null);
    this.error.set(''); this.notice.set('');
  }
  submitAuth() {
    if (!configuredApi || this.authForm.invalid || this.busy()) return;
    const { pharmacyName, email, password } = this.authForm.getRawValue();
    if (this.authTab() === 'register' && (!pharmacyName.trim() || password.length < 12)) {
      this.error.set('Enter a pharmacy name and a password of at least 12 characters.'); return;
    }
    this.busy.set(true); this.error.set('');
    const path = this.authTab() === 'register' ? 'register' : 'login';
    const payload = this.authTab() === 'register' ? { pharmacyName, email, password } : { email, password };
    this.http.post<Auth>(`${configuredApi}/api/auth/${path}`, payload).subscribe({
      next: auth => { this.busy.set(false); this.acceptAuth(auth); this.authForm.controls.password.setValue(''); },
      error: err => this.fail(err, 'Could not sign in. Check the API and credentials.')
    });
  }
  private acceptAuth(auth: Auth) {
    this.token.set(auth.token); this.pharmacyName.set(auth.pharmacyName); this.email.set(auth.email);
    this.mode.set('api'); this.view.set('overview');
    sessionStorage.setItem('pharmacy-session', JSON.stringify(auth));
    this.reload();
  }
  reload() {
    if (this.mode() !== 'api') return;
    forkJoin({
      medicines: this.http.get<Medicine[]>(`${configuredApi}/api/medicines`, this.headers()),
      sales: this.http.get<Sale[]>(`${configuredApi}/api/sales`, this.headers()),
      purchases: this.http.get<Purchase[]>(`${configuredApi}/api/purchases`, this.headers())
    }).subscribe({
      next: data => { this.medicines.set(data.medicines); this.sales.set(data.sales); this.purchases.set(data.purchases); this.error.set(''); this.loadReport(); },
      error: err => this.fail(err, 'Could not load pharmacy records.')
    });
  }
  loadReport() {
    if (this.mode() === 'demo') {
      const days = this.reportDays();
      const start = new Date(); start.setHours(0, 0, 0, 0); start.setDate(start.getDate() + 1 - days);
      const sales = this.sales().filter(s => new Date(s.createdAtUtc) >= start);
      const purchases = this.purchases().filter(p => new Date(p.createdAtUtc) >= start);
      this.report.set({ days, medicines: this.medicines().length, unitsInStock: this.totalStock(),
        lowStock: this.lowItems().length, stockValue: this.medicines().reduce((n, m) => n + m.stock * m.costPrice, 0),
        salesCount: sales.length, revenue: sales.reduce((n, s) => n + s.total, 0),
        costOfGoods: sales.reduce((n, s) => n + s.items.reduce((c, i) => c + i.quantity * (i.unitCost || 0), 0), 0),
        purchasesTotal: purchases.reduce((n, p) => n + p.total, 0) });
      return;
    }
    if (this.mode() === 'api') this.http.get<Summary>(`${configuredApi}/api/reports/summary?days=${this.reportDays()}`, this.headers())
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
    if (!Number.isInteger(value.reorderLevel) || value.salePrice < 0 || !value.name.trim()) return;
    const payload = { ...value, name: value.name.trim(), sku: value.sku.trim() };
    const id = this.editingId(); this.busy.set(true);
    if (this.mode() === 'demo') {
      if (id !== null && this.medicines().some(m => m.id !== id && m.sku && m.sku === payload.sku.toUpperCase())) {
        this.busy.set(false); this.error.set('SKU already exists.'); return;
      }
      if (id === null && payload.sku && this.medicines().some(m => m.sku === payload.sku.toUpperCase())) {
        this.busy.set(false); this.error.set('SKU already exists.'); return;
      }
      this.medicines.update(rows => id === null
        ? [...rows, { ...payload, sku: payload.sku.toUpperCase(), id: Math.max(0, ...rows.map(m => m.id)) + 1, stock: 0, costPrice: 0 }]
        : rows.map(m => m.id === id ? { ...m, ...payload, sku: payload.sku.toUpperCase() } : m));
      this.persistDemo(); this.cancelEdit(); this.success(id === null ? 'Medicine added. Record a purchase to add stock.' : 'Medicine updated.'); return;
    }
    const request = id === null
      ? this.http.post<Medicine>(`${configuredApi}/api/medicines`, payload, this.headers())
      : this.http.put<Medicine>(`${configuredApi}/api/medicines/${id}`, payload, this.headers());
    request.subscribe({ next: () => { this.cancelEdit(); this.success(id === null ? 'Medicine added.' : 'Medicine updated.'); this.reload(); },
      error: err => this.fail(err, 'Could not save medicine.') });
  }
  savePurchase() {
    if (this.purchaseForm.invalid || this.busy()) return;
    const { supplier, medicineId, quantity, unitCost } = this.purchaseForm.getRawValue();
    const id = Number(medicineId), m = this.medicines().find(row => row.id === id);
    if (!m || !Number.isInteger(quantity) || quantity < 1 || unitCost < 0) { this.error.set('Choose a medicine and valid quantity and cost.'); return; }
    const payload = { supplier: supplier.trim(), items: [{ medicineId: id, quantity, unitCost }] };
    this.busy.set(true);
    if (this.mode() === 'demo') {
      if (m.stock + quantity > 2147483647) { this.busy.set(false); this.error.set('Stock limit exceeded.'); return; }
      this.medicines.update(rows => rows.map(row => row.id === id ? { ...row, stock: row.stock + quantity, costPrice: unitCost } : row));
      this.purchases.update(rows => [{ id: Math.max(0, ...rows.map(p => p.id)) + 1, supplier: supplier.trim() || 'Unspecified',
        total: quantity * unitCost, createdAtUtc: new Date().toISOString(),
        items: [{ medicineId: id, medicineName: m.name, quantity, unitPrice: unitCost }] }, ...rows]);
      this.persistDemo(); this.purchaseForm.reset({ supplier: '', medicineId: 0, quantity: 1, unitCost: 0 });
      this.success('Purchase recorded and stock increased.'); return;
    }
    this.http.post<Purchase>(`${configuredApi}/api/purchases`, payload, this.headers()).subscribe({
      next: () => { this.purchaseForm.reset({ supplier: '', medicineId: 0, quantity: 1, unitCost: 0 }); this.success('Purchase recorded and stock increased.'); this.reload(); },
      error: err => this.fail(err, 'Could not record purchase.')
    });
  }
  saveSale() {
    if (this.saleForm.invalid || this.busy()) return;
    const { customer, medicineId, quantity } = this.saleForm.getRawValue();
    const id = Number(medicineId), m = this.medicines().find(row => row.id === id);
    if (!m || !Number.isInteger(quantity) || quantity < 1 || m.stock < quantity) { this.error.set('Choose an in-stock medicine and a valid quantity.'); return; }
    const payload = { customer: customer.trim(), items: [{ medicineId: id, quantity }] };
    this.busy.set(true);
    if (this.mode() === 'demo') {
      this.medicines.update(rows => rows.map(row => row.id === id ? { ...row, stock: row.stock - quantity } : row));
      this.sales.update(rows => [{ id: Math.max(0, ...rows.map(s => s.id)) + 1, customer: customer.trim() || 'Walk-in',
        total: quantity * m.salePrice, createdAtUtc: new Date().toISOString(),
        items: [{ medicineId: id, medicineName: m.name, quantity, unitPrice: m.salePrice, unitCost: m.costPrice }] }, ...rows]);
      this.persistDemo(); this.saleForm.reset({ customer: '', medicineId: 0, quantity: 1 });
      this.success('Sale recorded and stock deducted.'); return;
    }
    this.http.post<Sale>(`${configuredApi}/api/sales`, payload, this.headers()).subscribe({
      next: () => { this.saleForm.reset({ customer: '', medicineId: 0, quantity: 1 }); this.success('Sale recorded and stock deducted.'); this.reload(); },
      error: err => { this.fail(err, 'Could not record sale.'); this.reload(); }
    });
  }
}
