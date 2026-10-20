import { Component, OnInit, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';

import { MerchantApi, Venue, VenueSettings } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';

const CURRENCIES = ['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'JOD', 'EGP', 'USD', 'EUR', 'GBP'];
const TIME_ZONES = [
  'Asia/Dubai', 'Asia/Riyadh', 'Asia/Qatar', 'Asia/Kuwait', 'Asia/Bahrain', 'Asia/Muscat',
  'Asia/Amman', 'Africa/Cairo', 'Europe/London', 'UTC'
];

/** Venue profile, branding and guest features (P1-MER-01). */
@Component({
  selector: 'df-settings',
  imports: [ReactiveFormsModule],
  templateUrl: './settings.html'
})
export class Settings implements OnInit {
  private readonly api = inject(MerchantApi);
  private readonly currentVenue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);

  protected readonly currencies = CURRENCIES;
  protected readonly timeZones = TIME_ZONES;
  protected readonly venue = signal<Venue | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
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
    mostCommonTip: ['10']
  });

  /** The presets as typed, for the "Most common" picker. */
  protected tipPresetOptions(): number[] {
    return parsePercents(this.form.controls.tipPresets.value) ?? [];
  }

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => {
        if (!venue) {
          this.error.set('This account has no venue yet.');
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
      this.error.set('Turn on the menu, Pay at Table, or both, so guests have something to do when they scan.');
      return;
    }

    const { serviceChargePercent, taxPercent, tipPresets, mostCommonTip, ...rest } = value;
    const presets = (parsePercents(tipPresets) ?? []).map(p => Math.round(p * 100));
    const mostCommon = mostCommonTip === '' ? null : Math.round(Number(mostCommonTip) * 100);
    const settings: VenueSettings = {
      ...rest,
      currency: value.currency.toUpperCase(),
      logoUrl: value.logoUrl.trim() || null,
      serviceChargeBasisPoints: Math.round(serviceChargePercent * 100),
      taxBasisPoints: Math.round(taxPercent * 100),
      tipPresetsBasisPoints: presets,
      mostCommonTipBasisPoints: mostCommon !== null && presets.includes(mostCommon) ? mostCommon : null
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
    logoUrl: venue.logoUrl ?? '',
    serviceChargePercent: venue.serviceChargeBasisPoints / 100,
    taxPercent: venue.taxBasisPoints / 100,
    tipPresets: venue.tipPresetsBasisPoints.map(bp => bp / 100).join(', '),
    mostCommonTip: venue.mostCommonTipBasisPoints === null ? '' : String(venue.mostCommonTipBasisPoints / 100)
  };
}

/** "5, 10, 15" → [5, 10, 15], sorted and without repeats; null when it isn't 1–4 percentages of 0.01–30. */
function parsePercents(text: string): number[] | null {
  const parts = text.split(/[,\s]+/).map(p => p.replace('%', '')).filter(Boolean);
  if (!parts.length || parts.some(p => !/^\d+(\.\d{1,2})?$/.test(p))) return null;
  const values = [...new Set(parts.map(Number))].sort((a, b) => a - b);
  return values.length <= 4 && values.every(v => v >= 0.01 && v <= 30) ? values : null;
}

function tipPresetsValidator(control: AbstractControl<string>): ValidationErrors | null {
  return parsePercents(control.value) ? null : { tipPresets: true };
}
