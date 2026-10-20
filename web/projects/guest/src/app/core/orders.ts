import { HttpClient } from '@angular/common/http';
import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { GuestSession } from './guest-session';

export type OrderStatus = 'Submitted' | 'Accepted' | 'Preparing' | 'Ready' | 'Completed' | 'Rejected';

export interface OrderModifier {
  optionId: string;
  groupName: string;
  groupNameAr: string | null;
  name: string;
  nameAr: string | null;
  priceDeltaMinor: number;
}

export interface CartLineRequest {
  itemId: string;
  quantity: number;
  optionIds: string[];
  note: string | null;
}

export interface CartIssue {
  index: number;
  code: 'item_not_found' | 'item_unavailable' | 'menu_closed' | 'option_not_found' | 'option_unavailable' | 'invalid_choices';
  message: string;
}

export interface QuoteLine {
  index: number;
  itemId: string;
  name: string;
  nameAr: string | null;
  quantity: number;
  unitPriceMinor: number;
  lineTotalMinor: number;
  modifiers: OrderModifier[];
  note: string | null;
}

/** The server's price for the cart; the total here is what the guest is asked to confirm. */
export interface CartQuote {
  currency: string;
  lines: QuoteLine[];
  subtotalMinor: number;
  serviceChargeMinor: number;
  serviceChargeBasisPoints: number;
  taxMinor: number;
  taxBasisPoints: number;
  taxIncluded: boolean;
  totalMinor: number;
  issues: CartIssue[];
  canOrder: boolean;
}

export interface Order {
  id: string;
  number: number;
  status: OrderStatus;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  currency: string;
  lines: {
    itemId: string;
    name: string;
    nameAr: string | null;
    quantity: number;
    unitPriceMinor: number;
    lineTotalMinor: number;
    modifiers: OrderModifier[];
    note: string | null;
  }[];
  note: string | null;
  subtotalMinor: number;
  serviceChargeMinor: number;
  taxMinor: number;
  taxIncluded: boolean;
  totalMinor: number;
  rejectReason: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Guest order endpoints (blueprint 9.1). */
@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  quote(sessionId: string, lines: CartLineRequest[]): Observable<CartQuote> {
    return this.http.post<CartQuote>(`/v1/public/sessions/${sessionId}/cart/validate`, { lines });
  }

  place(sessionId: string, clientRequestId: string, lines: CartLineRequest[], note: string | null, expectedTotalMinor: number): Observable<Order> {
    return this.http.post<Order>(`/v1/public/sessions/${sessionId}/orders`, { clientRequestId, lines, note, expectedTotalMinor });
  }

  list(sessionId: string): Observable<Order[]> {
    return this.http.get<Order[]>(`/v1/public/sessions/${sessionId}/orders`);
  }
}

/** Sent to everyone at the table when an order or payment changes what the table owes. */
export interface BillChanged {
  serviceLocationId: string;
  reason: 'order' | 'payment';
  remainingMinor: number | null;
  fullyPaid: boolean | null;
}

/**
 * Live updates over SignalR (P1-ORD-05): each of the guest's orders whenever staff move it
 * along, and a nudge whenever the table's bill changes. Reconnects by itself, and pages reload
 * after a reconnect.
 */
@Injectable({ providedIn: 'root' })
export class OrderUpdates implements OnDestroy {
  private readonly session = inject(GuestSession);
  private connection: HubConnection | null = null;

  readonly changed = new Subject<Order>();
  readonly billChanged = new Subject<BillChanged>();
  /** Fires after the connection comes back, so pages can catch up on anything missed. */
  readonly reconnected = new Subject<void>();
  readonly connected = signal(false);

  start(): void {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/guest', { accessTokenFactory: () => this.session.token() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('orderChanged', (order: Order) => this.changed.next(order));
    connection.on('billChanged', (event: BillChanged) => this.billChanged.next(event));
    connection.onreconnecting(() => this.connected.set(false));
    connection.onreconnected(() => {
      this.connected.set(true);
      this.reconnected.next();
    });
    connection.onclose(() => this.connected.set(false));

    this.connection = connection;
    connection.start().then(() => this.connected.set(true)).catch(() => this.connected.set(false));
  }

  ngOnDestroy(): void {
    void this.connection?.stop();
  }
}

/** Order tracker stage (Selection, Checkout, Preparing, Delivered), 0-based; -1 when rejected. */
export function trackerStage(status: OrderStatus | 'Cart'): number {
  switch (status) {
    case 'Cart': return 0;
    case 'Submitted': return 1;
    case 'Accepted':
    case 'Preparing':
    case 'Ready': return 2;
    case 'Completed': return 3;
    default: return -1;
  }
}
