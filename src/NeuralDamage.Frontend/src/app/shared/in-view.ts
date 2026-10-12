import { afterNextRender, DestroyRef, Directive, ElementRef, inject, input, output } from '@angular/core';

type Watcher = (visible: boolean) => void;

interface Shared {
  observer: IntersectionObserver;
  watchers: Map<Element, Watcher>;
}

/** One observer per scroll container (and one for the viewport), however many elements it watches. */
const byRoot = new WeakMap<object, Shared>();
const viewport = {};

/**
 * Calls `watcher` whenever `element` scrolls into or out of view inside `root` (the viewport when
 * null), and returns the function that stops watching. Where the browser has no
 * IntersectionObserver, everything counts as visible.
 */
export function watchInView(element: Element, watcher: Watcher, root: Element | null = null, margin = '200px'): () => void {
  if (typeof IntersectionObserver === 'undefined') {
    watcher(true);
    return () => {};
  }
  const key = root ?? viewport;
  let shared = byRoot.get(key);
  if (!shared) {
    const watchers = new Map<Element, Watcher>();
    const observer = new IntersectionObserver(
      (entries) => entries.forEach((entry) => watchers.get(entry.target)?.(entry.isIntersecting)),
      { root, rootMargin: margin },
    );
    shared = { observer, watchers };
    byRoot.set(key, shared);
  }
  const { observer, watchers } = shared;
  watchers.set(element, watcher);
  observer.observe(element);
  return () => {
    watchers.delete(element);
    observer.unobserve(element);
  };
}

/** Emits each time the element scrolls into view, e.g. a sentinel at the end of a list that loads more. */
@Directive({ selector: '[appInView]' })
export class InView {
  /** The scroll container; the viewport when not set. */
  readonly inViewRoot = input<Element | null>(null);
  readonly appInView = output<void>();

  constructor() {
    const element = inject(ElementRef<Element>).nativeElement as Element;
    let stop = () => {};
    afterNextRender(() => {
      stop = watchInView(element, (visible) => visible && this.appInView.emit(), this.inViewRoot());
    });
    inject(DestroyRef).onDestroy(() => stop());
  }
}
