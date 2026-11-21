import { filter, fromEvent, merge, timer } from 'rxjs';

export function visiblePurchasePolls(document: Document) {
  return merge(timer(5_000, 5_000), fromEvent(document, 'visibilitychange')).pipe(
    filter(() => document.visibilityState === 'visible')
  );
}
