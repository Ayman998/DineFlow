import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';

import { FloorPlan, Location, LocationType, MerchantApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { QrCode, qrDataUrl } from '../../shared/qr-code';
import { LiveTables } from './live-tables';
import { TABLES } from './tables.i18n';

type Panel = 'none' | 'area' | 'tables';

interface Section {
  id: string | null;
  title: string;
  titleAr: string | null;
  /** False for a closed area; "No area" is always open. */
  active: boolean;
  locations: Location[];
}

export const LOCATION_TYPES: LocationType[] = ['Table', 'Counter', 'HotelRoom', 'PoolBeach'];

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
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(TABLES);

  protected readonly types = LOCATION_TYPES;
  protected readonly plan = signal<FloorPlan | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly panel = signal<Panel>('none');
  protected readonly busy = signal<ReadonlySet<string>>(new Set());
  /** The area the area form renames; null while it adds a new one. */
  protected readonly editingArea = signal<Section | null>(null);

  protected readonly areaForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    nameAr: ['', Validators.maxLength(80)]
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
    const sections: Section[] = plan.areas.map(a => ({ id: a.id, title: a.name, titleAr: a.nameAr, active: a.isActive, locations: a.locations }));
    if (plan.unassigned.length) sections.push({ id: null, title: this.i18n.t().noArea, titleAr: null, active: true, locations: plan.unassigned });
    return sections;
  });

  protected readonly totals = computed(() => {
    const all = this.sections().flatMap(s => s.locations);
    const open = this.sections().filter(s => s.active).flatMap(s => s.locations);
    return { locations: all.length, active: open.filter(l => l.qr?.status === 'Active').length };
  });

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => (venue ? this.refresh() : this.fail(this.i18n.t().noVenue)),
      error: error => this.fail(errorMessage(error))
    });
  }

  protected togglePanel(panel: Panel): void {
    // "Add area" while renaming one switches the form to adding rather than closing it.
    const switching = panel === 'area' && this.editingArea() !== null;
    this.panel.set(this.panel() === panel && !switching ? 'none' : panel);
    this.error.set(null);
    if (this.editingArea()) {
      this.editingArea.set(null);
      this.areaForm.reset();
    }
  }

  /** Opens the area form on an existing area, e.g. to add its Arabic name. */
  protected renameArea(section: Section): void {
    this.editingArea.set(section);
    this.areaForm.reset({ name: section.title, nameAr: section.titleAr ?? '' });
    this.panel.set('area');
    this.error.set(null);
  }

  protected saveArea(): void {
    const venue = this.currentVenue.venue();
    if (!venue || this.areaForm.invalid) return this.areaForm.markAllAsTouched();

    const { name, nameAr } = this.areaForm.getRawValue();
    const editing = this.editingArea();
    const request = editing?.id
      ? this.api.updateArea(venue.id, editing.id, name.trim(), nameAr.trim() || null)
      : this.api.createArea(venue.id, name.trim(), nameAr.trim() || null);
    this.run(request, area => {
      this.areaForm.reset();
      this.editingArea.set(null);
      this.panel.set('none');
      this.notice.set(editing ? this.t().renamedArea(area.name) : this.t().addedArea(area.name));
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
        this.notice.set(created.length ? this.t().addedTables(created.length) : this.t().tablesExist);
        this.refresh();
      });
    } else {
      if (!form.label.trim()) return this.tablesForm.controls.label.setErrors({ required: true });
      this.run(this.api.createLocation(venue.id, areaId, form.type, form.label.trim()), created => {
        this.tablesForm.controls.label.reset('');
        this.panel.set('none');
        this.notice.set(this.t().addedLocation(this.place(created)));
        this.refresh();
      });
    }
  }

  protected rotate(location: Location): void {
    if (!confirm(this.t().confirmRotate(this.place(location)))) return;

    this.runFor(location, this.api.rotateQr(location.id), updated =>
      this.notice.set(this.t().rotated(this.place(updated), updated.qr?.version ?? 1))
    );
  }

  protected setEnabled(location: Location, enabled: boolean): void {
    this.runFor(location, this.api.setQrEnabled(location.id, enabled), updated =>
      this.notice.set(enabled ? this.t().qrEnabled(this.place(updated)) : this.t().qrDisabled(this.place(updated)))
    );
  }

  /** Closes or reopens every table in an area at once, e.g. the terrace for the winter. */
  protected setAreaActive(section: Section, active: boolean): void {
    const venue = this.currentVenue.venue();
    if (!venue || !section.id) return;
    if (!active && !confirm(this.t().confirmCloseArea(section.title, section.locations.length))) return;

    const id = section.id;
    this.busy.update(ids => new Set(ids).add(id));
    const finish = () => this.busy.update(ids => {
      const next = new Set(ids);
      next.delete(id);
      return next;
    });

    this.run(this.api.setAreaActive(venue.id, id, active), area => {
      this.plan.update(plan => plan && { ...plan, areas: plan.areas.map(a => (a.id === area.id ? area : a)) });
      this.notice.set(active ? this.t().areaOpened(area.name) : this.t().areaClosed(area.name));
    }, finish);
  }

  protected isAreaBusy(section: Section): boolean {
    return !!section.id && this.busy().has(section.id);
  }

  protected async download(location: Location): Promise<void> {
    if (!location.qr) return;
    const link = document.createElement('a');
    link.href = await qrDataUrl(location.qr.url, 1024);
    link.download = `${this.plan()?.venueName ?? 'venue'} - ${this.place(location)} - v${location.qr.version}.png`;
    link.click();
  }

  protected print(): void {
    window.print();
  }

  /** "Table 12" in the portal's language (the API's displayName is English). */
  protected place(location: Location): string {
    return `${this.i18n.t().locationTypes[location.type] ?? location.type} ${location.label}`;
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
