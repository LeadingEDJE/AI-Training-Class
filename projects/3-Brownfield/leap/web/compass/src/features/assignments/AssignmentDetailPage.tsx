import { useState } from 'react';
import { Alert, Button, Card, PageHeader, StatusPill } from '../../components/ui';
import { AssignmentBreadcrumb } from './AssignmentBreadcrumb';
import type { BreadcrumbSegment } from './assignment-trail';
import {
  AssignmentForm,
  type AssignmentFormOutcome,
  type UpdateAssignmentFormValues,
  type InvoiceFrequencyOption,
} from './AssignmentForm';
import type { AssignmentRowDto, DeleteOutcome } from './assignments-api';
import { SowForm, type SowCreateValues, type SowFormOutcome, type SowFormValues } from './SowForm';
import { SowList, type SowListRow } from './SowList';

/** The SOW modal's open state — which record it is creating or editing, or closed entirely. */
type SowModalState =
  { mode: 'create' } | { mode: 'edit'; sowId: number; initialValues: SowFormValues } | null;

interface AssignmentDetailPageProps {
  assignment: AssignmentRowDto | null | undefined;
  isPending: boolean;
  isError: boolean;
  viaEmployee: boolean;
  trail: BreadcrumbSegment[];
  /**
   * Whether this viewer may edit the assignment — Compass Ops or Super Admin. This flag is the
   * authorization boundary: the server trusts the client to have already checked it, so there is no
   * independent refusal on the write endpoint.
   *
   * Also gates the whole SOWs / Contracts card, per `docs/design/sow-write-surface.md` §1. See
   * `web/compass/README.md` for the full permission matrix.
   */
  canManageAssignments?: boolean;
  invoiceFrequencyTypes?: InvoiceFrequencyOption[];
  onSubmit: (values: UpdateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
  canDeleteAssignment?: boolean;
  /**
   * Deletes the assignment and every SOW under it. The confirmation prompt lives in the route
   * container, not here, so this callback fires immediately once the caller has already confirmed.
   *
   * The caller is responsible for rendering a rejection; this page only navigates away on success.
   */
  onDeleteAssignment?: () => Promise<DeleteOutcome>;
  sows?: SowListRow[];
  sowsIsPending?: boolean;
  sowsIsError?: boolean;
  onCreateSow?: (values: SowCreateValues) => Promise<SowFormOutcome>;
  onUpdateSow?: (sowId: number, values: SowFormValues) => Promise<SowFormOutcome>;
  onDeleteSow?: (sowId: number) => Promise<DeleteOutcome>;
}

/**
 * The assignment detail screen — mockup screen 5 ("Client Assignment"). See
 * `docs/design/assignment-detail-mockup.html` for the full screen spec.
 *
 * The SOWs / Contracts card renders for every viewer who can reach this screen, including on an
 * internal client's assignment — the invoice-frequency fields are the only part hidden there.
 *
 * "Delete Assignment" and the per-row SOW "Delete" are soft deletes with a 30-day undo window,
 * gated on `canDeleteAssignment`. The confirmation prompt lives in the route container rather than
 * this component.
 *
 * A refused delete surfaces in a single alert at the top of the page, shared by both delete
 * surfaces.
 */
export function AssignmentDetailPage({
  assignment,
  isPending,
  isError,
  viaEmployee,
  trail,
  canManageAssignments = false,
  invoiceFrequencyTypes,
  onSubmit,
  canDeleteAssignment = false,
  onDeleteAssignment,
  sows,
  sowsIsPending = false,
  sowsIsError = false,
  onCreateSow = async () => ({ kind: 'rejected', message: 'The SOW could not be saved.' }),
  onUpdateSow = async () => ({ kind: 'rejected', message: 'The SOW could not be saved.' }),
  onDeleteSow,
}: AssignmentDetailPageProps) {
  const [sowModal, setSowModal] = useState<SowModalState>(null);
  const [assignmentDeleteError, setAssignmentDeleteError] = useState<string | null>(null);
  const [sowDeleteError, setSowDeleteError] = useState<string | null>(null);

  /**
   * Runs one delete and applies its outcome to that surface's slot.
   *
   * The `catch` here is defensive padding: the underlying request already throws on any failing
   * status, so by the time a result reaches this function it is always the successful case. The
   * console log is left over from an earlier debugging session.
   */
  async function settleDelete(
    run: () => Promise<DeleteOutcome> | undefined,
    setError: (message: string | null) => void,
    fallback: string,
  ) {
    try {
      const result = await run();
      setError(result?.kind === 'rejected' ? result.message : null);
    } catch (error) {
      console.error('Compass: a delete request failed', error);
      setError(fallback);
    }
  }

  if (isPending) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="status">Loading the assignment…</p>
      </main>
    );
  }

  if (isError) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="alert" className="text-brand-danger">
          That assignment could not be loaded.
        </p>
      </main>
    );
  }

  if (!assignment) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p>That assignment was not found.</p>
      </main>
    );
  }

  const breadcrumb = (
    <AssignmentBreadcrumb
      trail={trail}
      current={`Assignment — ${viaEmployee ? assignment.clientName : assignment.employeeName}`}
    />
  );

  return (
    <main className="mx-auto flex max-w-4xl flex-col gap-6 px-6 py-8 text-brand-gray">
      <PageHeader
        title="Client Assignment"
        breadcrumb={breadcrumb}
        status={
          <StatusPill
            tone={assignment.isCurrent ? 'affirmative' : 'neutral'}
            label={assignment.isCurrent ? 'Active' : 'Ended'}
          />
        }
        actions={
          canDeleteAssignment ? (
            <Button
              variant="secondary"
              onClick={async () => {
                if (
                  window.confirm(
                    'Delete this assignment and every SOW/contract period under it? This ' +
                      'cannot be undone.',
                  )
                ) {
                  await settleDelete(
                    () => onDeleteAssignment?.(),
                    setAssignmentDeleteError,
                    'The assignment could not be deleted.',
                  );
                }
              }}
            >
              Delete Assignment
            </Button>
          ) : undefined
        }
      />

      {assignmentDeleteError !== null && <Alert>{assignmentDeleteError}</Alert>}

      <Card title="Assignment Details">
        <dl className="mb-4 grid grid-cols-1 gap-x-7 gap-y-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">EDJEr</dt>
            <dd className="mt-1">{assignment.employeeName}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Client</dt>
            <dd className="mt-1">{assignment.clientName}</dd>
          </div>
          {/* We do not invoice internal work, so there is no cadence to report.

              FAILS CLOSED: `!isInternal` is falsy when the field is absent, so a cached SPA missing
              the field hides the section entirely rather than risk showing a stale internal client
              a cadence it should not have. */}
          {!assignment.isInternal && (
            <div>
              <dt className="text-xs font-medium uppercase tracking-wide">Invoice frequency</dt>
              <dd className="mt-1">
                {assignment.effectiveInvoiceFrequency ?? 'None set'}
              </dd>
            </div>
          )}
        </dl>
        <AssignmentForm
          mode="edit"
          canEdit={canManageAssignments}
          clientIsInternal={assignment.isInternal}
          invoiceFrequencyTypes={invoiceFrequencyTypes}
          initialValues={{
            employeeId: assignment.employeeId,
            clientId: assignment.clientId,
            startDate: assignment.startDate,
            endDate: assignment.endDate,
            note: assignment.note ?? null,
            invoiceFrequencyTypeId: assignment.invoiceFrequencyTypeId,
          }}
          onSubmit={async (values) => {
            const result = await onSubmit(values);
            if (result.kind === 'saved') {
              setAssignmentDeleteError(null);
            }
            return result;
          }}
        />
      </Card>

      {/* Two independent reasons to withhold this card: a PERMISSION gate on `canManageAssignments`
          and a DOMAIN rule on `!assignment.isInternal`.

          The `useSows` query in the route containers is also gated on `assignment.isInternal`
          directly, so it waits for the assignment to load before requesting SOWs at all — the two
          requests are intentionally serialised rather than run in parallel. */}
      {canManageAssignments && !assignment.isInternal && (
        <Card
          title="SOWs / Contracts"
          action={
            <Button size="small" onClick={() => setSowModal({ mode: 'create' })}>
              + Add SOW / Contract
            </Button>
          }
        >
          {sowDeleteError !== null && (
            <div className="mb-3">
              <Alert>{sowDeleteError}</Alert>
            </div>
          )}

          {sowsIsPending ? (
            <p role="status">Loading contract periods…</p>
          ) : sowsIsError ? (
            <p role="alert" className="text-brand-danger">
              Contract periods could not be loaded.
            </p>
          ) : (
            <SowList
              rows={sows ?? []}
              elevated
              onEdit={(id) => {
                const sow = (sows ?? []).find((row) => row.id === id);
                if (sow) {
                  setSowModal({
                    mode: 'edit',
                    sowId: sow.id,
                    initialValues: {
                      sowType: sow.sowType,
                      rateIncrease: sow.rateIncrease ?? false,
                      sowStartDate: sow.sowStartDate,
                      sowEndDate: sow.sowEndDate,
                      note: sow.note,
                    },
                  });
                }
              }}
              canDelete={canDeleteAssignment}
              onDelete={async (id) => {
                if (window.confirm('Delete this SOW/contract period? This cannot be undone.')) {
                  await settleDelete(
                    () => onDeleteSow?.(id),
                    setSowDeleteError,
                    'The SOW could not be deleted.',
                  );
                }
              }}
            />
          )}
        </Card>
      )}

      {sowModal !== null &&
        (sowModal.mode === 'create' ? (
          <SowForm
            mode="create"
            // The default is locked once a period already exists: the form disables the other radio
            // options in that case, so this pre-selection is the only value that can be submitted.
            defaultSowType={(sows ?? []).length > 0 ? 'SowExtension' : 'InitialContract'}
            onCancel={() => setSowModal(null)}
            onSubmit={async (values) => {
              const result = await onCreateSow(values);
              if (result.kind === 'saved') {
                setSowModal(null);
                setSowDeleteError(null);
              }
              return result;
            }}
          />
        ) : (
          <SowForm
            mode="edit"
            initialValues={sowModal.initialValues}
            onCancel={() => setSowModal(null)}
            onSubmit={async (values) => {
              const result = await onUpdateSow(sowModal.sowId, values);
              if (result.kind === 'saved') {
                setSowModal(null);
                setSowDeleteError(null);
              }
              return result;
            }}
          />
        ))}
    </main>
  );
}
