import { Lexicon } from '../../core/i18n';

const en = {
  signIn: 'Sign in',
  signingIn: 'Signing in…',
  intro: 'Manage your venue, tables and QR codes.',
  email: 'Email',
  emailInvalid: 'Enter a valid email address.',
  password: 'Password',
  passwordRequired: 'Enter your password.'
};

export const LOGIN: Lexicon<typeof en> = {
  en,
  ar: {
    signIn: 'تسجيل الدخول',
    signingIn: 'جارٍ تسجيل الدخول…',
    intro: 'أدِر فرعك وطاولاتك ورموز QR.',
    email: 'البريد الإلكتروني',
    emailInvalid: 'أدخل بريداً إلكترونياً صحيحاً.',
    password: 'كلمة المرور',
    passwordRequired: 'أدخل كلمة المرور.'
  }
};
