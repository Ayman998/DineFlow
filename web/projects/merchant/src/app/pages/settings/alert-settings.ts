import { Component, OnInit, inject, signal, viewChild } from '@angular/core';

import { AlertSettings, NotificationsApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { DeliveryLog } from '../../shared/delivery-log';

/**
 * Settings → Notifications (blueprint P1-STAFF-02/03): WhatsApp payment alerts on or off, the
 * numbers that get them, a test message, and what was delivered.
 */
@Component({
  selector: 'df-alert-settings',
  imports: [DeliveryLog],
  template: `
    @let s = settings();
    <div class="grid max-w-4xl gap-6 lg:grid-cols-[1fr_340px]">
      <section class="card flex flex-col gap-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <h2 class="font-semibold">WhatsApp payment alerts</h2>
            <p class="text-sm text-ink-muted">A short message for every successful payment: table, amount and tip, and whether the bill is paid or what's left. It reads well on a smartwatch.</p>
          </div>
          <input type="checkbox" class="toggle mt-1 shrink-0" aria-label="WhatsApp payment alerts" [checked]="s?.enabled ?? false"
                 [disabled]="!s || !auth.canManage() || busy()" (change)="toggle($any($event.target).checked)" />
        </div>

        @if (s?.provider === 'outbox') {
          <p class="rounded-lg bg-surface px-3 py-2 text-xs text-ink-muted">
            Development: messages are written to <span class="font-mono">App_Data/whatsapp</span> instead of being sent. Configure the WhatsApp Cloud API to send them for real.
          </p>
        }
        @if (error()) { <p class="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
        @if (notice()) { <p class="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800" role="status">{{ notice() }}</p> }

        <div>
          <p class="text-xs font-bold tracking-wide text-ink-muted uppercase">Numbers</p>
          @if (s && !s.contacts.length) { <p class="mt-2 text-sm text-ink-muted">No numbers yet.</p> }
          <ul class="mt-2 flex flex-col divide-y divide-line">
            @for (c of s?.contacts ?? []; track c.id) {
              <li class="flex items-center justify-between gap-3 py-2 text-sm">
                <span><span class="font-medium">{{ c.name }}</span> <span class="ms-2 font-mono text-ink-muted">{{ c.phoneNumber }}</span></span>
                @if (auth.canManage()) { <button type="button" class="btn-ghost px-2 py-1 text-red-700" [disabled]="busy()" (click)="remove(c.id)">Remove</button> }
              </li>
            }
          </ul>
        </div>

        @if (auth.canManage()) {
          <form class="grid gap-3 sm:grid-cols-[1fr_1fr_auto]" (submit)="$event.preventDefault(); add()">
            <label><span class="field-label">Name</span>
              <input class="field" maxlength="60" placeholder="e.g. Floor manager" [value]="name()" (input)="name.set($any($event.target).value)" /></label>
            <label><span class="field-label">WhatsApp number</span>
              <input class="field" type="tel" maxlength="30" placeholder="+971 50 123 4567" [value]="phone()" (input)="phone.set($any($event.target).value)" /></label>
            <button type="submit" class="btn-secondary self-end" [disabled]="busy() || !name().trim() || !phone().trim()">Add</button>
          </form>
          <div>
            <button type="button" class="btn-secondary" [disabled]="busy() || !s?.contacts?.length" (click)="test()">Send a test message</button>
          </div>
        } @else {
          <p class="text-sm text-ink-muted">Only owners and managers can change who gets alerts.</p>
        }
      </section>

      <df-delivery-log channel="WhatsApp" title="Sent on WhatsApp" />
    </div>
  `
})
export class AlertSettingsPanel implements OnInit {
  private readonly api = inject(NotificationsApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  private readonly log = viewChild(DeliveryLog);

  protected readonly settings = signal<AlertSettings | null>(null);
  protected readonly name = signal('');
  protected readonly phone = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  ngOnInit(): void {
    this.venue.load().subscribe(venue => {
      if (venue) this.run(this.api.alerts(venue.id));
    });
  }

  protected toggle(enabled: boolean): void {
    const id = this.venue.venue()?.id;
    if (id) this.run(this.api.setAlerts(id, enabled));
  }

  protected add(): void {
    const id = this.venue.venue()?.id;
    if (!id) return;
    this.run(this.api.addContact(id, this.name().trim(), this.phone().trim()), () => {
      this.name.set('');
      this.phone.set('');
    });
  }

  protected remove(contactId: string): void {
    this.run(this.api.removeContact(contactId));
  }

  protected test(): void {
    const id = this.venue.venue()?.id;
    if (!id) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.test(id).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('Test message queued. It goes out within a few seconds; check the log.');
        setTimeout(() => this.log()?.load(), 6_000);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  private run(request: ReturnType<NotificationsApi['alerts']>, done?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: settings => {
        this.busy.set(false);
        this.settings.set(settings);
        done?.();
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
