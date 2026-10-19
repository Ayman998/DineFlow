import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { NAV_ITEMS } from './app.routes';

@Component({
  selector: 'df-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html'
})
export class App {
  protected readonly nav = NAV_ITEMS;
}
