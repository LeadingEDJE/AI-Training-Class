import { useState } from 'react';
import { useSowExtensionLookup } from '../useSowExtensionLookup';
import { SowExtensionPage } from './SowExtensionPage';
import type { SowExtensionRange } from './SowExtensionPage';

/**
 * Connects the SOW Extension Report to the read surface (issue #534) — mirrors
 * `AssignmentStartRoute`, this report's closest sibling.
 *
 * **The range lives here, and it starts as `null`.** That null is what keeps the query from running on
 * mount: this screen answers a question the user has to ask first.
 *
 * `SowExtensionPage` only calls `onRun` with a range it has already validated, so an inverted range
 * never reaches this state and therefore never becomes a request.
 */
export function SowExtensionRoute() {
  const [range, setRange] = useState<SowExtensionRange | null>(null);
  const query = useSowExtensionLookup(range);

  return (
    <SowExtensionPage
      report={query.data}
      // `isPending` is true for an idle disabled query as well as an in-flight one, so it is only
      // meaningful once a range exists.
      isPending={range !== null && query.isPending}
      // TWO sources, matching `AssignmentStartRoute`: `isError` covers what still throws (dead network,
      // unparseable body), and `kind === 'failed'` covers a non-OK status, which resolves so a 403 can
      // arrive as its own state.
      isError={query.isError || query.data?.kind === 'failed'}
      onRun={setRange}
    />
  );
}
