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
  /**
   * Which parent record this screen was reached from (AC-1, AC-2). Names the current screen after the
   * OTHER party, and nothing else; the ancestry above it comes from {@link trail}.
   */
  viaEmployee: boolean;
  /** Supplied by the route container, which is the only place that knows the origin. */
  trail: BreadcrumbSegment[];
  /**
   * Whether this viewer may edit the assignment — Compass Ops or Super Admin (same check
   * `EmployeeDetailPage`/`ClientViewPage` use for "New assignment"). Compass Admin and Sales can
   * reach this screen too (AC-16/FR-025), but only to VIEW: with this false, dates and note render
   * as read-only text and there is no Save button — matching `EmployeeDetailPage`'s "no edit control
   * for any viewer" idiom, scoped here to viewers who genuinely cannot write rather than everyone.
   * The server refuses the write independently either way (FR-006); this is a usability affordance,
   * not the authorization boundary.
   *
   * **Also gates the whole SOWs / Contracts card** (US3, #66): unlike the assignment surface,
   * `contracts/sow-write-surface.md` §1 grants NO read exception for Admin/Sales — the SOW route
   * refuses anyone who is not Compass Ops or the root, reads included. Rendering the card for a
   * viewer who can only ever be refused would show a permanent error, so it does not render at all.
   * It is no longer the ONLY gate on that card: issue #518 hides it for an internal client too, on
   * domain grounds rather than permission grounds — see the class docstring.
   */
  canManageAssignments?: boolean;
  /**
   * The selectable invoice-frequency cadences (US6, #64), supplied by the route container. Optional
   * and defaulting to empty so a caller with no cadence data still renders — the override select
   * then offers only "Use the client default", which is the correct behaviour, not a broken screen.
   */
  invoiceFrequencyTypes?: InvoiceFrequencyOption[];
  onSubmit: (values: UpdateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
  /**
   * Whether this viewer may PERMANENTLY DELETE this assignment or any of its SOWs (issue #593) —
   * Compass Super Admin only, narrower than {@link canManageAssignments}. Gates BOTH the "Delete
   * Assignment" header action and the per-row "Delete" action in the SOWs card.
   */
  canDeleteAssignment?: boolean;
  /**
   * Deletes the assignment and every SOW under it. The confirmation prompt (issue #593: "a simple
   * popup will suffice") lives HERE, not in the route container, so it fires before the mutation
   * regardless of caller.
   *
   * **Returns the outcome, and a rejection is rendered by this page** — the caller's only visible
   * effect is navigating away on `'deleted'`, so a discarded `'rejected'` is a silent failure.
   */
  onDeleteAssignment?: () => Promise<DeleteOutcome>;
  /** Every contract period on this assignment (US3, #66). `undefined` while `sowsIsPending`. */
  sows?: SowListRow[];
  sowsIsPending?: boolean;
  sowsIsError?: boolean;
  onCreateSow?: (values: SowCreateValues) => Promise<SowFormOutcome>;
  onUpdateSow?: (sowId: number, values: SowFormValues) => Promise<SowFormOutcome>;
  /** Deletes ONE contract period. Same prompt placement and return contract as above. */
  onDeleteSow?: (sowId: number) => Promise<DeleteOutcome>;
}

/**
 * The assignment detail screen (AC-1, AC-2) — mockup screen 5 ("Client Assignment"), reachable from
 * either the EDJEr's or the Client's record rather than a standalone list (feature 006, owner
 * direction 2026-08-14, superseding the earlier standalone-route decision).
 *
 * **The SOWs / Contracts card is live** (US3, #66): real periods from `sows`, an "+ Add SOW /
 * Contract" action opening {@link SowForm} as a modal for create, and an Edit action per row opening
 * the same modal pre-populated. The card renders only for `canManageAssignments` — the SOW route
 * grants no read exception the way the assignment surface does for Admin/Sales (contract §1), so a
 * non-elevated viewer would only ever see a refusal here.
 *
 * **It is additionally hidden for an INTERNAL client** (issue #518), for a different reason than the
 * permission gate: internal work is EDJE on EDJE, so there is no counterparty to sign a statement of
 * work with and nothing to invoice. The section is meaningless rather than merely empty or refused —
 * and the server now refuses the create too, so an Add action here could only produce a rejection.
 * The Invoice frequency surfaces go with it, on this screen and inside {@link AssignmentForm}. Same
 * rule as `ClientViewPage`'s handling of an internal client's SOW column (issue #243).
 *
 * **A "Delete Assignment" header action and a per-row "Delete" action on the SOWs card are live**
 * (issue #593), gated on `canDeleteAssignment` — Compass Super Admin only, narrower than
 * `canManageAssignments`. Both are TRUE, permanent deletes with no undo; the confirmation prompt
 * (`window.confirm`, per the owner's "a simple popup will suffice") lives in this component so it
 * fires regardless of caller. The route container performs the actual mutation and, for the
 * assignment delete, navigates away — this page's own subject no longer exists afterward.
 *
 * **A refused delete is surfaced here too, for the same reason the prompt is** — in TWO independent
 * slots, one per delete surface, so an assistive technology user is not hunting among alerts, each
 * beside its own trigger (the assignment one under the header, the SOW one inside the card). Two
 * rather than one because the SOWs card sits far down the page: a single top-of-page alert would be
 * off-screen for a per-row Delete. Each is cleared only by a successful write in its own section.
 *
 * **Every route INTO this section is gated on the same flag**, or the rule would just relocate the
 * problem to a link that lands on a page with nothing on it: `ClientViewPage` (#243),
 * `ClientAssignmentHistory` — which drops the column outright, since an internal client has none —
 * and `EdjerAssignmentHistory`, which keeps the column and withholds the link per row, because an
 * EDJEr's history mixes a beach allocation with real engagements. `EmployeeDetailPage`'s link is
 * deliberately untouched: it reads "View assignment" and promises nothing about contracts.
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
  // Defaulted, not left optional-called (`onCreateSow?.(...)`): the card that opens this modal
  // only renders when `canManageAssignments` is true, so a real caller always supplies both — the
  // fallback exists for tests that render the modal in isolation, not a reachable production path.
  onCreateSow = async () => ({ kind: 'rejected', message: 'The SOW could not be saved.' }),
  onUpdateSow = async () => ({ kind: 'rejected', message: 'The SOW could not be saved.' }),
  onDeleteSow,
}: AssignmentDetailPageProps) {
  const [sowModal, setSowModal] = useState<SowModalState>(null);
  /**
   * The two delete surfaces' error slots. Cleared only by a successful write in the SAME section —
   * the assignment slot by an `AssignmentForm` save, the SOW slot by a `SowForm` create or edit, or
   * either by a later successful delete on its own surface. Why two: the class docstring.
   */
  const [assignmentDeleteError, setAssignmentDeleteError] = useState<string | null>(null);
  const [sowDeleteError, setSowDeleteError] = useState<string | null>(null);

  /**
   * Runs one delete and applies its outcome to that surface's slot.
   *
   * **The `catch` is the point, not padding.** `apiFetch` chains `.then` onto a promise rather than
   * throwing on status, so a 4xx RESOLVES and arrives here as `{ kind: 'rejected' }` carrying the
   * server's message; a transport failure REJECTS instead, and letting that propagate leaves the
   * user having confirmed the popup and seen nothing happen at all. It logs as well, because this
   * same catch swallows a plain programming bug, which a friendly sentence alone makes
   * undiagnosable.
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
                // Issue #593: "a simple popup will suffice" — the owner's own words. A true delete,
                // so the copy says so rather than something softer like "remove".
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

      {/* The assignment-delete slot, beside its own trigger in the PageHeader above. */}
      {assignmentDeleteError !== null && <Alert>{assignmentDeleteError}</Alert>}

      <Card title="Assignment Details">
        {/* AssignmentForm's edit mode omits EDJEr/Client entirely (contract §2 — neither is
            movable), so they are shown here as plain read-only context instead, above the
            editable fields — matching mockup screen 5's field order. */}
        <dl className="mb-4 grid grid-cols-1 gap-x-7 gap-y-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">EDJEr</dt>
            <dd className="mt-1">{assignment.employeeName}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Client</dt>
            <dd className="mt-1">{assignment.clientName}</dd>
          </div>
          {/* Issue #518 — we do not invoice internal work, so there is no cadence to report.

              FAILS OPEN, and both guards on this screen do: `!isInternal` is truthy when the field
              is absent, so an OLD SPA cached against a NEW API renders the section and the "+ Add
              SOW / Contract" button whose POST now answers 400. Deliberate, and the right
              direction — the opposite default would hide contracts from every external client the
              moment a payload lost the field, and an old server has no guard either. */}
          {!assignment.isInternal && (
            <div>
              <dt className="text-xs font-medium uppercase tracking-wide">Invoice frequency</dt>
              <dd className="mt-1">
                {/* The EFFECTIVE cadence the server resolved (override, else client default), never
                    re-derived from the two ids here — the precedence rule lives in one place
                    (FR-037). "None set" is a real answer, not missing data (FR-039). */}
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
            // A successful write retires this section's stale delete message, and only this one.
            if (result.kind === 'saved') {
              setAssignmentDeleteError(null);
            }
            return result;
          }}
        />
      </Card>

      {/* Two independent reasons to withhold this card, and they are not the same reason.
          `canManageAssignments` is a PERMISSION gate (contract §1 — no read exception for
          Admin/Sales). `!assignment.isInternal` is a DOMAIN rule (issue #518) — internal work has no
          statements of work for anyone, at any tier.

          The `useSows` query in the route containers is deliberately left ungated on `isInternal`.
          It is already gated on `canManageAssignments`, and a read of zero-or-more rows for a card
          that does not render is harmless; gating it on `assignment.isInternal` would mean waiting
          for the assignment to load before the SOWs could be asked for, serialising two requests to
          save a request that costs nothing. */}
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
                // A LegacyMigrated row is included: editing one is the AC-41 grandfathering exit, and
                // its type travels back to the server unchanged (US8/#67, issue #404).
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
                // Issue #593: the same confirmation copy pattern as the assignment delete above,
                // scoped to this one period.
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
            // Issue #626: any existing period at all — including a LegacyMigrated one — means the
            // next one added is almost always an extension of it, so pre-select that. Still just a
            // starting point; the radios below remain freely switchable.
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
