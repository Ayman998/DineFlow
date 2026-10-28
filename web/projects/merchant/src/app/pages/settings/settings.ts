import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';

import { MerchantApi, ReviewPlatform, Venue, VenueSettings } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { Features } from '../../core/features';
import { I18n } from '../../core/i18n';
import { AlertSettingsPanel } from './alert-settings';
import { AuditLog } from './audit-log';
import { PosSettingsPanel } from './pos-settings';
import { TeamSettingsPanel } from './team-settings';
import { SETTINGS } from './settings.i18n';

// Jordan first: DineFlow's venues are in Amman.
const CURRENCIES = ['JOD', 'AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'EGP', 'USD', 'EUR', 'GBP'];
const TIME_ZONES = [
  'Asia/Amman', 'Asia/Dubai', 'Asia/Riyadh', 'Asia/Qatar', 'Asia/Kuwait', 'Asia/Bahrain', 'Asia/Muscat',
  'Africa/Cairo', 'Europe/London', 'UTC'
];

/** Venue profile, branding and guest features (P1-MER-01); the team and passwords; WhatsApp alerts; the audit log for managers. */
@Component({
  selector: 'df-settings',
  imports: [ReactiveFormsModule, AlertSettingsPanel, AuditLog, PosSettingsPanel, TeamSettingsPanel],
  templateUrl: './settings.html'
})
export class Settings implements OnInit {
  private readonly api = inject(MerchantApi);
  private readonly currentVenue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  private readonly features = inject(Features);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(SETTINGS);

  protected readonly view = signal<'venue' | 'team' | 'pos' | 'notifications' | 'activity'>('venue');
  protected readonly tabs = computed(() => {
    const labels = this.t().tabs;
    return [
      { value: 'venue' as const, label: labels.venue },
      { value: 'team' as const, label: labels.team },
      { value: 'pos' as const, label: labels.pos },
      ...(this.features.current().whatsAppAlerts ? [{ value: 'notifications' as const, label: labels.notifications }] : []),
      ...(this.auth.canManage() ? [{ value: 'activity' as const, label: labels.activity }] : [])
    ];
  });

  protected readonly currencies = CURRENCIES;
  protected readonly timeZones = TIME_ZONES;
  protected readonly venue = signal<Venue | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    nameAr: ['', Validators.maxLength(120)],
    currency: ['', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
    timeZone: ['', [Validators.required, Validators.maxLength(64)]],
    defaultLocale: ['en' as 'en' | 'ar', Validators.required],
    logoUrl: ['', Validators.maxLength(500)],
    brandColor: ['#0B3B38', [Validators.required, Validators.pattern(/^#[0-9A-Fa-f]{6}$/)]],
    menuEnabled: [true],
    payAtTableEnabled: [true],
    serviceChargePercent: [0, [Validators.required, Validators.min(0), Validators.max(30)]],
    taxPercent: [0, [Validators.required, Validators.min(0), Validators.max(30)]],
    pricesIncludeTax: [true],
    tipsEnabled: [true],
    /** Comma-separated percentages, e.g. "5, 10, 15". */
    tipPresets: ['5, 10, 15', [Validators.required, tipPresetsValidator]],
    /** One of the presets as a percentage, or '' for none. */
    mostCommonTip: ['10'],
    reviewsEnabled: [true],
    /** '' for no public review platform. */
    reviewPlatform: ['' as ReviewPlatform | ''],
    reviewUrl: ['', [Validators.maxLength(500), Validators.pattern(/^(https:\/\/\S+)?$/)]],
    feedbackTitle: ['', Validators.maxLength(80)]
  });

  protected readonly platforms: ReviewPlatform[] = ['Google', 'TripAdvisor', 'Foursquare'];

  /** The presets as typed, for the "Most common" picker. */
  protected tipPresetOptions(): number[] {
    return parsePercents(this.form.controls.tipPresets.value) ?? [];
  }

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => {
        if (!venue) {
          this.error.set(this.i18n.t().noVenue);
          return;
        }
        this.venue.set(venue);
        this.form.reset(toForm(venue));
        if (!this.auth.canManage()) this.form.disable();
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected save(): void {
    const venue = this.venue();
    if (!venue) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    if (!value.menuEnabled && !value.payAtTableEnabled) {
      this.error.set(this.t().menuOrPayRequired);
      return;
    }

    const { serviceChargePercent, taxPercent, tipPresets, mostCommonTip, reviewPlatform, reviewUrl, feedbackTitle, nameAr, ...rest } = value;
    if (reviewPlatform && !reviewUrl.trim()) {
      this.error.set(this.t().reviewUrlRequired);
      return;
    }
    const presets = (parsePercents(tipPresets) ?? []).map(p => Math.round(p * 100));
    const mostCommon = mostCommonTip === '' ? null : Math.round(Number(mostCommonTip) * 100);
    const settings: VenueSettings = {
      ...rest,
      nameAr: nameAr.trim() || null,
      currency: value.currency.toUpperCase(),
      logoUrl: value.logoUrl.trim() || null,
      serviceChargeBasisPoints: Math.round(serviceChargePercent * 100),
      taxBasisPoints: Math.round(taxPercent * 100),
      tipPresetsBasisPoints: presets,
      mostCommonTipBasisPoints: mostCommon !== null && presets.includes(mostCommon) ? mostCommon : null,
      reviewPlatform: reviewPlatform || null,
      reviewUrl: reviewPlatform ? reviewUrl.trim() : null,
      feedbackTitle: feedbackTitle.trim() || null
    };

    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    this.api.updateVenueSettings(venue.id, settings).subscribe({
      next: updated => {
        this.venue.set(updated);
        this.currentVenue.venue.set(updated);
        this.form.reset(toForm(updated));
        this.saving.set(false);
        this.saved.set(true);
      },
      error: error => {
        this.error.set(errorMessage(error));
        this.saving.set(false);
      }
    });
  }

  protected invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }
}

/** Rates are edited as percentages and stored as basis points (10% = 1000). */
function toForm(venue: Venue) {
  return {
    ...venue,
    nameAr: venue.nameAr ?? '',
    logoUrl: venue.logoUrl ?? '',
    serviceChargePercent: venue.serviceChargeBasisPoints / 100,
    taxPercent: venue.taxBasisPoints / 100,
    tipPresets: venue.tipPresetsBasisPoints.map(bp => bp / 100).join(', '),
    mostCommonTip: venue.mostCommonTipBasisPoints === null ? '' : String(venue.mostCommonTipBasisPoints / 100),
    reviewPlatform: venue.reviewPlatform ?? ('' as const),
    reviewUrl: venue.reviewUrl ?? '',
    feedbackTitle: venue.feedbackTitle ?? ''
  };
}

/** "5, 10, 15" → [5, 10, 15], sorted and without repeats; null when it isn't 1–4 percentages of 0.01–20. */
function parsePercents(text: string): number[] | null {
  const parts = text.split(/[,،\s]+/).map(p => p.replace(/[%٪]/, '')).filter(Boolean);
  if (!parts.length || parts.some(p => !/^\d+(\.\d{1,2})?$/.test(p))) return null;
  const values = [...new Set(parts.map(Number))].sort((a, b) => a - b);
  return values.length <= 4 && values.every(v => v >= 0.01 && v <= 20) ? values : null;
}

function tipPresetsValidator(control: AbstractControl<string>): ValidationErrors | null {
  return parsePercents(control.value) ? null : { tipPresets: true };
}
