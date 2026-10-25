import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';

import { PosApi, PosCommandPage, PosCommandStatus, PosSettings, PosSimulator, SimulatorMode } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { formatMoney } from '../../core/money';

/**
 * Settings → POS (blueprint E3, E9): which POS the venue uses and its credentials, a connection
 * check, table and product mapping, and what's been sent to the POS, is waiting, or was refused,
 * with a retry. With the simulated POS, its checks and a failure switch for trying the hard cases.
 */
@Component({
  selector: 'df-pos-settings',
  templateUrl: './pos-settings.html'
})
export class PosSettingsPanel implements OnInit {
  private readonly api = inject(PosApi);
  private readonly venue = inject(CurrentVenue);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(Auth);

  protected readonly settings = signal<PosSettings | null>(null);
  protected readonly commands = signal<PosCommandPage | null>(null);
  protected readonly simulator = signal<PosSimulator | null>(null);
  protected readonly filter = signal<PosCommandStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  // Connection form
  protected readonly provider = signal('simulator');
  protected readonly branchRef = signal('');
  protected readonly accessToken = signal('');
  protected readonly active = signal(true);
  protected readonly autoClose = signal(true);

  // Mapping edits, by location or item id
  protected readonly tableRefs = signal<Record<string, string | undefined>>({});
  protected readonly productRefs = signal<Record<string, string | undefined>>({});
  protected readonly showProducts = signal(false);

  protected readonly modes: { value: SimulatorMode; label: string }[] = [
    { value: 'Online', label: 'Online' },
    { value: 'Offline', label: 'Offline (nothing gets through)' },
    { value: 'TimeoutAfterApplying', label: 'Time out after applying (once)' },
    { value: 'Refuse', label: 'Refuse everything' }
  ];

  protected readonly chosen = computed(() => this.settings()?.providers.find(p => p.provider === this.provider()) ?? null);
  protected readonly unmapped = computed(() => this.settings()?.tables.filter(t => !t.externalRef).length ?? 0);

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());
    // Sending happens in the background: keep the queue current while the page is open.
    const timer = setInterval(() => this.loadCommands(), 10_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  protected save(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    const token = this.accessToken().trim();
    this.run(this.api.save(venueId, {
      provider: this.provider(),
      branchRef: this.branchRef().trim() || null,
      accessToken: token ? token : null,
      isActive: this.active(),
      autoClose: this.autoClose()
    }), 'Saved.');
  }

  protected removeToken(): void {
    const venueId = this.venue.venue()?.id;
    const c = this.settings()?.connection;
    if (!venueId || !c) return;
    this.run(this.api.save(venueId, { provider: c.provider, branchRef: c.branchRef, accessToken: '', isActive: false, autoClose: c.autoClose }), 'Token removed and the connection paused.');
  }

  protected check(): void {
    const venueId = this.venue.venue()?.id;
    if (venueId) this.run(this.api.check(venueId), null);
  }

  protected saveTables(): void {
    const venueId = this.venue.venue()?.id;
    const edits = this.tableRefs();
    if (!venueId) return;
    this.run(this.api.map(venueId, Object.entries(edits).map(([locationId, ref]) => ({ locationId, externalRef: ref?.trim() || null })), null), 'Tables saved.');
  }

  protected saveProducts(): void {
    const venueId = this.venue.venue()?.id;
    const edits = this.productRefs();
    if (!venueId) return;
    this.run(this.api.map(venueId, null, Object.entries(edits).map(([itemId, ref]) => ({ itemId, posProductRef: ref?.trim() || null }))), 'Products saved.');
  }

  protected setTableRef(id: string, value: string): void {
    this.tableRefs.update(refs => ({ ...refs, [id]: value }));
  }

  protected setProductRef(id: string, value: string): void {
    this.productRefs.update(refs => ({ ...refs, [id]: value }));
  }

  protected setFilter(status: PosCommandStatus | null): void {
    this.filter.set(status);
    this.loadCommands();
  }

  protected retry(id: string): void {
    this.api.retry(id).subscribe({
      next: () => {
        this.notice.set('Queued again; it goes out within a few seconds.');
        setTimeout(() => this.loadCommands(), 6_000);
        this.loadCommands();
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected setMode(mode: SimulatorMode): void {
    const venueId = this.venue.venue()?.id;
    if (venueId) this.api.setSimulator(venueId, mode).subscribe({ next: s => this.simulator.set(s), error: error => this.error.set(errorMessage(error)) });
  }

  protected refreshSimulator(): void {
    const venueId = this.venue.venue()?.id;
    if (venueId && this.settings()?.connection?.provider === 'simulator')
      this.api.simulator(venueId).subscribe({ next: s => this.simulator.set(s), error: () => this.simulator.set(null) });
  }

  protected kindLabel(kind: string): string {
    return { SendOrder: 'Order', PostPayment: 'Payment', CloseCheck: 'Close' }[kind] ?? kind;
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.venue.venue()?.currency ?? 'AED');
  }

  protected when(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '–';
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.settings(venueId).subscribe({ next: s => this.apply(s), error: error => this.error.set(errorMessage(error)) });
    this.loadCommands();
  }

  private loadCommands(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.commands(venueId, this.filter() ?? undefined).subscribe({ next: c => this.commands.set(c), error: () => undefined });
    this.refreshSimulator();
  }

  private apply(settings: PosSettings): void {
    this.settings.set(settings);
    const c = settings.connection;
    this.provider.set(c?.provider ?? settings.providers[0]?.provider ?? 'simulator');
    this.branchRef.set(c?.branchRef ?? '');
    this.accessToken.set('');
    this.active.set(c?.isActive ?? true);
    this.autoClose.set(c?.autoClose ?? true);
    this.tableRefs.set({});
    this.productRefs.set({});
    this.refreshSimulator();
  }

  private run(request: ReturnType<PosApi['settings']>, done: string | null): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: s => {
        this.busy.set(false);
        this.apply(s);
        if (done) this.notice.set(done);
        this.loadCommands();
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
