import { Location } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';

import { I18n } from '../../core/i18n';
import { LegalInfo } from '../../core/legal';
import { LegalLinks } from '../../shared/legal-links';
import { Logo } from '../../shared/logo';
import { LEGAL, LEGAL_UPDATED_ON, LegalDoc, Section } from './legal.i18n';

/**
 * /terms, /privacy, /refunds and /contact: what guests accept when they pay, and what an acquirer
 * checks before approving the merchant. The document comes from the route's data.
 */
@Component({
  selector: 'hz-legal-page',
  imports: [LegalLinks, Logo],
  templateUrl: './legal-page.html'
})
export class LegalPage {
  protected readonly i18n = inject(I18n);
  private readonly location = inject(Location);
  private readonly legal = inject(LegalInfo);

  protected readonly doc = toSignal(inject(ActivatedRoute).data.pipe(map(d => d['doc'] as LegalDoc)), { initialValue: 'terms' as LegalDoc });
  protected readonly t = computed(() => LEGAL[this.i18n.lang()]);
  protected readonly operator = this.legal.operator;
  protected readonly updatedOn = LEGAL_UPDATED_ON;

  /** The document's sections, worded with the operator's details once they've loaded. */
  protected readonly sections = computed<Section[]>(() => {
    const operator = this.operator();
    const doc = this.doc();
    if (!operator || doc === 'contact') return [];
    return this.t()[doc](operator);
  });

  protected readonly hasContact = computed(() => {
    const o = this.operator();
    return !!o && !!(o.email || o.phone || o.address);
  });

  /** Back to wherever the guest came from; the bill opens these in a new tab, which has nowhere to go back to. */
  protected back(): void {
    if (window.history.length > 1) this.location.back();
    else window.location.assign('/');
  }

  protected tel(phone: string): string {
    return 'tel:' + phone.replace(/[^\d+]/g, '');
  }
}
