import { LookupSection } from './LookupSection';
import { PageHeader } from '../../components/ui';

/**
 * Lookup administration — the Compass reference data a Super Admin can change without a code change or
 * a migration.
 *
 * Both lookups live on one screen since their rules are identical: a unique name, and an active flag
 * that controls whether the value is offered on new records. Each is rendered by its own
 * {@link LookupSection} bound to its own resource.
 *
 * Every change made here is written to the audit trail alongside every other Compass configuration
 * surface — the same server-side enforcement that requires the Compass root policy also logs the
 * write, so the `admin-config` nav gate is sufficient on its own to keep this screen safe.
 */
export function LookupAdminPage() {
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title="Lookup Administration" />

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
        <LookupSection kind="employee-types" title="Employee Types" singular="Employee Type" />

        <LookupSection
          kind="invoice-frequency-types"
          title="Invoice Frequency Types"
          singular="Invoice Frequency Type"
        />
      </div>
    </div>
  );
}
