import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';

import { OrderDto, OrdersAdminApi, PaymentNotice, ReviewNotice } from './api';
import { Auth } from './auth';
import { CurrentVenue } from './current-venue';
import { OrderCountsStore } from './order-counts';

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline';

/**
 * Real-time staff alerts (blueprint E10, P1-STAFF-01): a SignalR connection that announces new
 * and updated orders and confirmed payments for the current venue, keeps the "new orders" count
 * current and plays a chime for each new order and payment.
 */
@Injectable({ providedIn: 'root' })
export class LiveOrders {
  private readonly auth = inject(Auth);
  private readonly venue = inject(CurrentVenue);
  private readonly api = inject(OrdersAdminApi);
  private connection: HubConnection | null = null;
  private audio: AudioContext | null = null;

  readonly state = signal<LiveState>('offline');
  readonly counts = inject(OrderCountsStore).counts;
  readonly placed = new Subject<OrderDto>();
  readonly changed = new Subject<OrderDto>();
  readonly paid = new Subject<PaymentNotice>();
  readonly reviewed = new Subject<ReviewNotice>();
  /** Fires after a reconnect, so pages can reload anything they missed. */
  readonly reconnected = new Subject<void>();

  start(): void {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/merchant', { accessTokenFactory: () => this.auth.token() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('orderPlaced', (order: OrderDto) => {
      if (!this.isMine(order)) return;
      this.chime();
      this.refreshCounts();
      this.placed.next(order);
    });
    connection.on('orderChanged', (order: OrderDto) => {
      if (!this.isMine(order)) return;
      this.refreshCounts();
      this.changed.next(order);
    });
    connection.on('reviewSubmitted', (review: ReviewNotice) => {
      if (review.venueId === this.venue.venue()?.id) this.reviewed.next(review);
    });
    connection.on('paymentReceived', (payment: PaymentNotice) => {
      if (payment.venueId !== this.venue.venue()?.id) return;
      this.chime();
      this.paid.next(payment);
    });
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => {
      this.state.set('live');
      this.refreshCounts();
      this.reconnected.next();
    });
    connection.onclose(() => this.state.set('offline'));

    this.connection = connection;
    this.state.set('connecting');
    connection.start()
      .then(() => {
        this.state.set('live');
        this.refreshCounts();
      })
      .catch(() => this.state.set('offline'));

    // Browsers only allow sound after the user has interacted with the page once.
    document.addEventListener('pointerdown', () => this.unlockAudio(), { once: true });
  }

  stop(): void {
    void this.connection?.stop();
    this.connection = null;
  }

  refreshCounts(): void {
    const venueId = this.venue.venue()?.id;
    if (venueId) this.api.counts(venueId).subscribe({ next: counts => this.counts.set(counts), error: () => undefined });
  }

  private isMine(order: { venueId: string }): boolean {
    return order.venueId === this.venue.venue()?.id;
  }

  private unlockAudio(): void {
    try {
      this.audio ??= new AudioContext();
      void this.audio.resume();
    } catch {
      // No Web Audio: alerts stay visual only.
    }
  }

  /** Two short tones: noticeable in a busy kitchen without being harsh. */
  private chime(): void {
    const audio = this.audio;
    if (!audio || audio.state !== 'running') return;
    [880, 1175].forEach((frequency, i) => {
      const start = audio.currentTime + i * 0.18;
      const osc = audio.createOscillator();
      const gain = audio.createGain();
      osc.frequency.value = frequency;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.25, start + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.16);
      osc.connect(gain).connect(audio.destination);
      osc.start(start);
      osc.stop(start + 0.17);
    });
  }
}
