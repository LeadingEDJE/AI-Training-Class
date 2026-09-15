import { useState } from 'react';
import { useSowExtensionLookup } from '../useSowExtensionLookup';
import { SowExtensionPage } from './SowExtensionPage';
import type { SowExtensionRange } from './SowExtensionPage';

/**
 * Connects the SOW Extension Report to the read surface (issue #534) — mirrors
 * `AssignmentStartRoute`, this report's closest sibling.
 *
 * **The range lives here, and it starts at a default 90-day window.** The query runs immediately
 * on mount with that default so the report has data to show before the user picks their own range.
 */
export function SowExtensionRoute() {
  const [range, setRange] = useState<SowExtensionRange | null>(null);
  const query = useSowExtensionLookup(range);

  return (
    <SowExtensionPage
      report={query.data}
      isPending={range !== null && query.isPending}
      // Only `query.isError` matters here — `kind === 'failed'` is inherited from
      // `AssignmentStartRoute`'s shape but this report never actually produces it.
      isError={query.isError || query.data?.kind === 'failed'}
      onRun={setRange}
    />
  );
}
