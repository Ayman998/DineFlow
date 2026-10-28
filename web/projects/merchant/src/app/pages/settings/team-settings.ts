import { Component, OnInit, inject, signal } from '@angular/core';

import { StaffMember, StaffPassword, StaffRole, TeamApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { TEAM } from './settings.i18n';

/** Matches StaffUser.MinPasswordLength on the API. */
const MIN_PASSWORD = 10;

/**
 * Settings → Team: everyone changes their own password here (e.g. the temporary one they were
 * given); owners and managers add people, change roles, switch accounts off and reset passwords.
 */
@Component({
  selector: 'df-team-settings',
  templateUrl: './team-settings.html'
})
export class TeamSettingsPanel implements OnInit {
  private readonly api = inject(TeamApi);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(TEAM);
  protected readonly minPassword = MIN_PASSWORD;

  protected readonly members = signal<StaffMember[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** The temporary password just issued, shown once. */
  protected readonly issued = signal<StaffPassword | null>(null);
  protected readonly copied = signal(false);

  protected readonly adding = signal(false);
  protected readonly newName = signal('');
  protected readonly newEmail = signal('');
  protected readonly newRole = signal<StaffRole>('Staff');

  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordNotice = signal<string | null>(null);

  ngOnInit(): void {
    if (this.auth.canManage()) this.load();
  }

  /** Owners give any role; managers only add and manage staff (the API enforces the same). */
  protected roles(): StaffRole[] {
    return this.auth.profile()?.role === 'Owner' ? ['Staff', 'Manager', 'Owner'] : ['Staff'];
  }

  protected mayManage(m: StaffMember): boolean {
    return !m.isYou && (this.auth.profile()?.role === 'Owner' || m.role === 'Staff');
  }

  protected changePassword(): void {
    this.passwordError.set(null);
    this.passwordNotice.set(null);
    if (this.newPassword().length < MIN_PASSWORD) return this.passwordError.set(this.t().passwordTooShort(MIN_PASSWORD));
    if (this.newPassword() !== this.confirmPassword()) return this.passwordError.set(this.t().passwordsDiffer);
    this.busy.set(true);
    this.api.changePassword(this.currentPassword(), this.newPassword()).subscribe({
      next: () => {
        this.busy.set(false);
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.passwordNotice.set(this.t().passwordChanged);
      },
      error: error => {
        this.busy.set(false);
        this.passwordError.set(errorMessage(error));
      }
    });
  }

  protected startAdd(): void {
    this.adding.set(true);
    this.newName.set('');
    this.newEmail.set('');
    this.newRole.set('Staff');
  }

  protected add(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.add({ email: this.newEmail().trim(), displayName: this.newName().trim(), role: this.newRole() }).subscribe({
      next: result => {
        this.busy.set(false);
        this.adding.set(false);
        this.show(result);
        this.load();
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected setRole(m: StaffMember, role: StaffRole): void {
    this.update(m, { displayName: m.displayName, role, isActive: m.isActive });
  }

  protected toggleActive(m: StaffMember): void {
    if (m.isActive && !confirm(this.t().confirmSwitchOff(m.displayName))) return;
    this.update(m, { displayName: m.displayName, role: m.role, isActive: !m.isActive });
  }

  protected resetPassword(m: StaffMember): void {
    if (!confirm(this.t().confirmReset(m.displayName))) return;
    this.api.resetPassword(m.id).subscribe({ next: result => this.show(result), error: error => this.error.set(errorMessage(error)) });
  }

  protected copy(): void {
    const password = this.issued()?.temporaryPassword;
    if (!password) return;
    navigator.clipboard?.writeText(password).then(() => this.copied.set(true), () => undefined);
  }

  private show(result: StaffPassword): void {
    this.copied.set(false);
    this.issued.set(result);
  }

  private update(m: StaffMember, request: { displayName: string; role: StaffRole; isActive: boolean }): void {
    this.error.set(null);
    this.api.update(m.id, request).subscribe({ next: () => this.load(), error: error => { this.error.set(errorMessage(error)); this.load(); } });
  }

  private load(): void {
    this.api.list().subscribe({ next: list => this.members.set(list), error: error => this.error.set(errorMessage(error)) });
  }
}
