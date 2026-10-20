import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { Auth } from '../core/auth';
import { CurrentVenue } from '../core/current-venue';
import { NAV_ITEMS } from './nav';

/** Signed-in layout: sidebar navigation (blueprint 4.4), venue and user in the top bar. */
@Component({
  selector: 'df-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html'
})
export class Shell implements OnInit {
  protected readonly nav = NAV_ITEMS;
  protected readonly auth = inject(Auth);
  protected readonly currentVenue = inject(CurrentVenue);

  ngOnInit(): void {
    this.currentVenue.load().subscribe();
  }
}
