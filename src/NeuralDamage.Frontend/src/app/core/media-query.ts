import { Signal, signal } from '@angular/core';

/** A media query as a signal that follows the viewport. */
export function mediaQuery(query: string): Signal<boolean> {
  const list = matchMedia(query);
  const matches = signal(list.matches);
  list.addEventListener('change', (event) => matches.set(event.matches));
  return matches.asReadonly();
}
