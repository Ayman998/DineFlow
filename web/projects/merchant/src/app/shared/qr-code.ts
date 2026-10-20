import { Component, effect, input, signal } from '@angular/core';
import QRCode from 'qrcode';

const RENDER_OPTIONS: QRCode.QRCodeToDataURLOptions = {
  margin: 1,
  width: 360,
  errorCorrectionLevel: 'M',
  color: { dark: '#101828', light: '#ffffff' }
};

/** Renders a URL as a QR code image, generated in the browser. */
@Component({
  selector: 'df-qr-code',
  template: `
    @if (dataUrl(); as src) {
      <img [src]="src" [alt]="alt()" class="block size-full" />
    } @else {
      <div class="size-full animate-pulse rounded bg-surface"></div>
    }
  `,
  host: { class: 'block' }
})
export class QrCode {
  readonly url = input.required<string>();
  readonly alt = input('');

  protected readonly dataUrl = signal<string | null>(null);

  constructor() {
    effect(() => {
      const url = this.url();
      this.dataUrl.set(null);
      qrDataUrl(url).then(dataUrl => {
        if (this.url() === url) this.dataUrl.set(dataUrl);
      });
    });
  }
}

/** PNG data URL of a QR code, e.g. for downloading a single code. */
export function qrDataUrl(url: string, width = RENDER_OPTIONS.width): Promise<string> {
  return QRCode.toDataURL(url, { ...RENDER_OPTIONS, width });
}
