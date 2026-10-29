const menu = document.querySelector('.menu-toggle');
const navigation = document.querySelector('#navigation');
function closeMenu() {
  menu?.setAttribute('aria-expanded', 'false');
  menu?.setAttribute('aria-label', 'Open navigation');
  navigation?.classList.remove('is-open');
}
menu?.addEventListener('click', () => {
  const open = menu.getAttribute('aria-expanded') !== 'true';
  menu.setAttribute('aria-expanded', String(open));
  menu.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
  navigation.classList.toggle('is-open', open);
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && menu?.getAttribute('aria-expanded') === 'true') {
    closeMenu(); menu.focus();
  }
});
document.addEventListener('click', event => {
  if (!event.target.closest('.header')) closeMenu();
});
matchMedia('(min-width: 761px)').addEventListener('change', closeMenu);
document.querySelector('#contact-form')?.addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  if (!form.reportValidity()) return;
  const values = new FormData(form);
  const subject = `Velo enquiry: ${values.get('topic')}`;
  const body = `Name: ${values.get('name')}\nEmail: ${values.get('email')}\n\n${values.get('message')}`;
  const status = document.querySelector('#form-status');
  const href = `mailto:${form.dataset.email}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  status.hidden = false;
  status.textContent = 'Your email is ready. If your email app didn’t open, ';
  const link = document.createElement('a');
  link.href = href; link.textContent = 'open the prepared email';
  status.append(link, ` or email ${form.dataset.email} directly. Your message has not been sent by this website.`);
  window.location.href = href;
});
