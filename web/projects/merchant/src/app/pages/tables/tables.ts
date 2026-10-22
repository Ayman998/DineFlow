import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';

import { FloorPlan, Location, LocationType, MerchantApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { QrCode, qrDataUrl } from '../../shared/qr-code';
import { LiveTables } from './live-tables';

type Panel = 'none' | 'area' | 'tables';

interface Section {
  id: string | null;
  title: string;
  locations: Location[];
}

export const LOCATION_TYPES: { value: LocationType; label: string }[] = [
  { value: 'Table', label: 'Table' },
  { value: 'Counter', label: 'Counter' },
  { value: 'HotelRoom', label: 'Hotel room' },
  { value: 'PoolBeach', label: 'Pool/Beach' }
];

/**
 * Tables & QR (P1-MER-02): dining areas, tables and other service locations, each with its
 * QR code. Codes can be rotated, disabled, downloaded and printed as a sheet.
 */
@Component({
  selector: 'df-tables',
  imports: [ReactiveFormsModule, QrCode, LiveTables],
  templateUrl: './tables.html'
})
export class Tables implements OnInit {
  /** The live view of what each table owes, or managing tables and their QR codes. */
  protected readonly view = signal<'live' | 'qr'>('live');

  private readonly api = inject(MerchantApi);
  private readonly currentVenue = inject(CurrentVenue);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly auth = inject(Auth);

  protected readonly types = LOCATION_TYPES;
  protected readonly plan = signal<FloorPlan | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly panel = signal<Panel>('none');
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  protected readonly areaForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(80)]]
  });

  protected readonly tablesForm = this.fb.group({
    mode: ['range' as 'range' | 'single'],
    areaId: [''],
    from: [1, [Validators.required, Validators.min(1), Validators.max(999)]],
    to: [10, [Validators.required, Validators.min(1), Validators.max(999)]],
    type: ['Table' as LocationType],
    label: ['', Validators.maxLength(20)]
  });

  protected readonly sections = computed<Section[]>(() => {
    const plan = this.plan();
    if (!plan) return [];
    const sections: Section[] = plan.areas.map(a => ({ id: a.id, title: a.name, locations: a.locations }));
    if (plan.unassigned.length) sections.push({ id: null, title: 'No area', locations: plan.unassigned });
    return sections;
  });

  protected readonly totals = computed(() => {
    const all = this.sections().flatMap(s => s.locations);
    return { locations: all.length, active: all.filter(l => l.qr?.status === 'Active').length };
  });

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => (venue ? this.refresh() : this.fail('This account has no venue yet.')),
      error: error => this.fail(errorMessage(error))
    });
  }

  protected togglePanel(panel: Panel): void {
    this.panel.set(this.panel() === panel ? 'none' : panel);
    this.error.set(null);
  }

  protected addArea(): void {
    const venue = this.currentVenue.venue();
    if (!venue || this.areaForm.invalid) return this.areaForm.markAllAsTouched();

    this.run(this.api.createArea(venue.id, this.areaForm.getRawValue().name), area => {
      this.areaForm.reset();
      this.panel.set('none');
      this.notice.set(`Added area "${area.name}".`);
      this.refresh();
    });
  }

  protected addTables(): void {
    const venue = this.currentVenue.venue();
    const form = this.tablesForm.getRawValue();
    const areaId = form.areaId || null;
    if (!venue) return;

    if (form.mode === 'range') {
      if (this.tablesForm.controls.from.invalid || this.tablesForm.controls.to.invalid) {
        return this.tablesForm.markAllAsTouched();
      }
      this.run(this.api.bulkCreateTables(venue.id, areaId, form.from, form.to), created => {
        this.panel.set('none');
        this.notice.set(created.length
          ? `Added ${created.length} table${created.length === 1 ? '' : 's'} with QR codes.`
          : 'Those table numbers already exist.');
        this.refresh();
      });
    } else {
      if (!form.label.trim()) return this.tablesForm.controls.label.setErrors({ required: true });
      this.run(this.api.createLocation(venue.id, areaId, form.type, form.label.trim()), created => {
        this.tablesForm.controls.label.reset('');
        this.panel.set('none');
        this.notice.set(`Added ${created.displayName} with a QR code.`);
        this.refresh();
      });
    }
  }

  protected rotate(location: Location): void {
    const ok = confirm(
      `Issue a new QR code for ${location.displayName}?\n\nThe printed code stops working straight away, so print and place the new one.`
    );
    if (!ok) return;

    this.runFor(location, this.api.rotateQr(location.id), updated =>
      this.notice.set(`${updated.displayName} now has QR version ${updated.qr?.version}. Print it to replace the old one.`)
    );
  }

  protected setEnabled(location: Location, enabled: boolean): void {
    this.runFor(location, this.api.setQrEnabled(location.id, enabled), updated =>
      this.notice.set(`${updated.displayName}'s QR code is ${enabled ? 'active again' : 'disabled'}.`)
    );
  }

  protected async download(location: Location): Promise<void> {
    if (!location.qr) return;
    const link = document.createElement('a');
    link.href = await qrDataUrl(location.qr.url, 1024);
    link.download = `${this.plan()?.venueName ?? 'venue'} - ${location.displayName} - v${location.qr.version}.png`;
    link.click();
  }

  protected print(): void {
    window.print();
  }

  protected isBusy(location: Location): boolean {
    return this.busy().has(location.id);
  }

  protected brandName(): string {
    return this.auth.profile()?.tenantName ?? '';
  }

  private refresh(): void {
    const venue = this.currentVenue.venue();
    if (!venue) return;

    this.api.floorPlan(venue.id).subscribe({
      next: plan => {
        this.plan.set(plan);
        this.loading.set(false);
      },
      error: error => this.fail(errorMessage(error))
    });
  }

  private run<T>(request: Observable<T>, done: (result: T) => void, always?: () => void): void {
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: result => {
        always?.();
        done(result);
      },
      error: error => {
        always?.();
        this.error.set(errorMessage(error));
      }
    });
  }

  /** Runs a per-location action and swaps the updated location into the plan. */
  private runFor(location: Location, request: Observable<Location>, done: (updated: Location) => void): void {
    this.busy.update(ids => new Set(ids).add(location.id));
    const finish = () => this.busy.update(ids => {
      const next = new Set(ids);
      next.delete(location.id);
      return next;
    });

    this.run(request, updated => {
      this.replace(updated);
      done(updated);
    }, finish);
  }

  private replace(updated: Location): void {
    this.plan.update(plan => {
      if (!plan) return plan;
      const swap = (list: Location[]) => list.map(l => (l.id === updated.id ? updated : l));
      return {
        ...plan,
        areas: plan.areas.map(a => ({ ...a, locations: swap(a.locations) })),
        unassigned: swap(plan.unassigned)
      };
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.loading.set(false);
  }
}
