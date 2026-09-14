import { useState } from 'react';
import { useNavigate, useParams } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  FormField,
  FormGrid,
  PageHeader,
  StatusPill,
  Toggle,
} from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import { US_STATES } from '../../lib/us-states';
import { US_TIMEZONES } from '../../lib/us-timezones';
import { fetchLookups, lookupQueryKey } from '../lookups/lookup-api';
import { EdjerAssignmentHistory } from './EdjerAssignmentHistory';
import {
  createEdjer,
  edjerQueryKey,
  edjersQueryKey,
  fetchEdjer,
  fetchEdjers,
  updateEdjer,
  type BlockingAssignment,
  type CompassEdjer,
  type CompassEdjerRequest,
} from './edjer-api';

const LIST_ROUTE = '/admin/edjers';

/** Where a successful ADD lands, so the first assignment can be added from the record. */
const EDIT_ROUTE = '/admin/edjers/$edjerId';

/**
 * The values the form holds while it is being edited.
 *
 * Strings for the two selects, because that is what a `<select>` yields — converted to numbers on submit,
 * which is where the API's `int` matters. Sending `"1"` where an int is expected is a 400 nobody can
 * explain from the form, so the conversion has a test of its own.
 */
interface EdjerFormValues {
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  employeeTypeId: string;
  coachEmployeeId: string;
  stateOfResidence: string;
  timezone: string;
  isDeliveryTeam: boolean;
  isActive: boolean;
  timesheetRequired: boolean;
  canSubmitUnder40: boolean;
  includeInPayroll: boolean;
}

/**
 * The mockups' defaults for a new EDJEr: timesheet required and payroll on, under-40 off.
 */
const BLANK: EdjerFormValues = {
  firstName: '',
  lastName: '',
  hireDate: '',
  email: '',
  employeeTypeId: '',
  coachEmployeeId: '',
  stateOfResidence: '',
  // Blank, NOT Eastern. FR-8.1 makes the zone required, and a pre-selected default is a value the
  // administrator never chose — the failure mode #420 shipped a transitional entity initialiser for and
  // that this field exists to close. Unchosen is refused by the server, which is where the control lives.
  timezone: '',
  // On, because most EDJErs are on the delivery team and the server defaults the same way. An
  // administrator turns it off for the exceptions rather than setting it for everyone else.
  isDeliveryTeam: true,
  isActive: true,
  timesheetRequired: true,
  canSubmitUnder40: false,
  includeInPayroll: true,
};

/**
 * EDJEr configuration — add and update (issue #59, AC-17, AC-18, AC-19).
 *
 * **Field inventory, labels, order, grouping and hint text come from the mockups' `#s-edjer` screen.**
 * Three of its details deliberately do not ship, each because an acceptance criterion or a scope boundary
 * says otherwise — the mockups' own banner establishes that order of precedence:
 *
 * - **Coach is optional.** The mockup marks it required; AC-17 says optional and the column is nullable.
 * - **The Client Assignment History card (`EC-3`) ships on EDIT only** — AC-20, issue #223. An EDJEr
 *   being added has no id to assign against, which is why a successful ADD lands on the new record
 *   rather than the list. See `EdjerAssignmentHistory`.
 * - **No annotation chips.** Mockup scaffolding.
 *
 * Two fields the mockup does NOT show also ship: Timezone (FR-8.1, issue #421) and Delivery Team
 * (FR-004, issue #502). The mockup predates Compass becoming the system of record for either — a value
 * OOTO reads, and delivery-team membership. Timezone sits beside State of Residence because the two are
 * the same kind of fact about a person. Delivery Team sits in Profile rather than Time Tracking
 * Settings because it is published to every tier rather than withheld from the lesser ones.
 *
 * **Client-side validation is for the administrator's benefit and is never the control** (FR-041). The
 * server refuses an inactive employee type, a state outside the 50 plus DC, and a colliding email
 * regardless of what this form allows — which is why the rejection paths matter more here than the
 * `required` attributes.
 */
export function EdjerFormPage() {
  const params = useParams({ strict: false }) as { edjerId?: string };
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const edjerId = readEdjerId(params.edjerId);
  const isEditing = edjerId !== null;

  /**
   * EVERY type, active and retired — filtered to active for the select below.
   *
   * **Why not `activeOnly`, which this API supports (issue #277).** Only active types may be OFFERED
   * (AC-25, FR-005), and that rule is enforced by the filter a few lines down. But a record whose
   * classification has since been retired still has to NAME it (FR-007), and the active-only list is
   * precisely the list that value is missing from — so asking for it here left the form with an id it
   * could not label, and it fell back to a generic "Current type (retired, no longer offered)".
   * Reading the whole collection keeps the name resolvable without denormalising it onto the record's
   * own payload, which `CompassEdjerDtoTests` deliberately forbids.
   */
  const employeeTypes = useQuery({
    queryKey: [...lookupQueryKey('employee-types'), 'all'],
    queryFn: () => fetchLookups('employee-types'),
  });

  /** The coach picker's options. Every EDJEr may coach, so this is the same collection the list shows. */
  const coaches = useQuery({ queryKey: edjersQueryKey(), queryFn: fetchEdjers });

  const record = useQuery({
    queryKey: edjerQueryKey(edjerId ?? 0),
    queryFn: () => fetchEdjer(edjerId ?? 0),
    enabled: isEditing,
  });

  // `Array.isArray` rather than a bare cast, because a collection read that answered 200 with something
  // that is not a list would otherwise crash the render — `.map is not a function`, a white screen, and no
  // error state at all. Found by the accessibility suite, whose fetch double answers every request with
  // one object. Treating it as empty degrades to the screen's own "nothing to offer" path instead.
  const allTypes = asArray(employeeTypes.data?.kind === 'loaded' ? employeeTypes.data.values : []);
  /** Only active types may be OFFERED (AC-25, FR-005). The retired ones are read for naming only. */
  const activeTypes = allTypes.filter((type) => type.isActive);
  const coachOptions = asArray(coaches.data?.kind === 'loaded' ? coaches.data.value : []);

  if (isEditing && record.data?.kind === 'refused') {
    return <LoadFailure message="You do not have permission to manage EDJEr records." />;
  }

  if (isEditing && record.data?.kind === 'failed') {
    return <LoadFailure message="That EDJEr could not be loaded. Try again." />;
  }

  const loaded = record.data?.kind === 'loaded' ? record.data.value : null;

  // Editing waits for the record before the fields exist, which is what removes the need to copy a
  // late-arriving value into state.
  if (isEditing && loaded === null) {
    return (
      <p role="status" className="text-sm text-brand-gray-muted">
        Loading EDJEr…
      </p>
    );
  }

  return (
    <EdjerForm
      // Remounting on identity change is what resets the fields — React's documented answer to "reset
      // state when an input changes", and the reason this component holds no values of its own.
      key={edjerId ?? 'new'}
      edjerId={edjerId}
      initialValues={loaded === null ? BLANK : toValues(loaded)}
      storedTypeName={allTypes.find((type) => type.id === loaded?.employeeTypeId)?.typeName ?? null}
      activeTypes={activeTypes}
      coachOptions={coachOptions}
      onSaved={async (savedId) => {
        // Every surface the record appears on, not just the admin list. Bare prefixes, so the
        // parameterised keys are covered too.
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: edjersQueryKey() }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'team-directory'] }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'employee-detail'] }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'edjer-pickers'] }),
        ]);

        // ADD lands on the record, where the assignment affordances are; EDIT returns to the list.
        void navigate(
          isEditing ? { to: LIST_ROUTE } : { to: EDIT_ROUTE, params: { edjerId: String(savedId) } },
        );
      }}
      onCancel={() => void navigate({ to: LIST_ROUTE })}
    />
  );
}

interface EdjerFormProps {
  /** The record being edited, or null when adding. */
  edjerId: number | null;
  initialValues: EdjerFormValues;
  /**
   * The stored classification's name, resolved from the FULL lookup list (issue #277).
   *
   * Separate from `activeTypes` because that list is filtered to active values, so a retired
   * classification is by definition absent from it — resolving the name there would always miss the one
   * case this exists for. Null when no name can be resolved, which is the generic-label fallback.
   */
  storedTypeName: string | null;
  activeTypes: { id: number; typeName: string }[];
  coachOptions: { id: number; firstName: string; lastName: string; isActive: boolean }[];
  /** Called with the id of the record just written — the add flow navigates to it. */
  onSaved: (savedId: number) => Promise<void>;
  onCancel: () => void;
}

/**
 * The fields themselves, mounted only once their initial values are known.
 *
 * **Why this is a separate component.** A controlled form cannot take its values from a query that
 * resolves after the first render without being told to, and the obvious way to tell it — an effect that
 * calls `setValues` when the data arrives — is the shape `react-hooks/set-state-in-effect` rejects, for
 * good reason: it renders once with the wrong values, then again with the right ones, and any field the
 * administrator touched in between is silently overwritten. Splitting the component removes the problem
 * rather than suppressing the warning: the parent waits, this one initialises from props, and the `key`
 * resets it if the identity changes.
 */
function EdjerForm({
  edjerId,
  initialValues,
  storedTypeName,
  activeTypes,
  coachOptions,
  onSaved,
  onCancel,
}: EdjerFormProps) {
  const isEditing = edjerId !== null;

  const [values, setValues] = useState<EdjerFormValues>(initialValues);
  const [writeError, setWriteError] = useState<string | null>(null);
  const [blocking, setBlocking] = useState<BlockingAssignment[] | null>(null);

  const save = useMutation({
    mutationFn: (request: CompassEdjerRequest) =>
      isEditing ? updateEdjer(edjerId, request) : createEdjer(request),
  });

  async function submit() {
    const result = await save.mutateAsync(toRequest(values));

    if (result.kind === 'rejected') {
      setBlocking(null);
      setWriteError(result.message);
      return;
    }

    if (result.kind === 'blocked') {
      // The server refused, so the stored record is unchanged — and the form must say so. Leaving the
      // status toggle showing OFF would tell the administrator the opposite of what happened.
      setValues((current) => ({ ...current, isActive: true }));
      setWriteError(result.message);
      setBlocking(result.assignments);
      return;
    }

    setWriteError(null);
    setBlocking(null);

    await onSaved(result.value.id);
  }

  // The record's name for the breadcrumb trail. Empty until the record loads, which is why the
  // segment is conditional rather than rendering a bare separator.
  const trailName = `${values.firstName} ${values.lastName}`.trim();

  return (
    <form
      className="flex flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      {/* Breadcrumb and status come from `#s-edjer`, which draws
          `Admin / EDJEr Configuration / Maya Alvarez` above the title with an `Active` pill beside
          it (FR-012). `PageHeader` has supported both props since Stream 2; they were never passed.

          The status pill appears only when EDITING. On the add screen there is no record to have a
          status yet, and rendering the form's default as though it were one would state a fact that
          is not true until the first save. */}
      <PageHeader
        title={isEditing ? 'Edit EDJEr' : 'Add EDJEr'}
        breadcrumb={
          <>
            Admin / EDJEr Configuration
            {isEditing && trailName !== '' ? ` / ${trailName}` : ''}
          </>
        }
        status={
          isEditing ? (
            <StatusPill
              tone={values.isActive ? 'affirmative' : 'neutral'}
              label={values.isActive ? 'Active' : 'Former'}
            />
          ) : undefined
        }
      />

      {writeError !== null && (
        <Alert>
          <p>{writeError}</p>

          {blocking !== null && blocking.length > 0 && (
            <>
              {/* The clients BY NAME, because an assignment id is not actionable by a human. And no
                  control to end them: AC-19 forbids auto-ending, since an assignment's true end date
                  frequently differs from the deactivation date. The recovery path needs Stream 3's
                  assignment surface, so the copy must not imply Compass can do it here. */}
              <p className="mt-2 font-medium">End-date these assignments first, then try again:</p>
              <ul className="mt-1 list-inside list-disc">
                {blocking.map((assignment) => (
                  <li key={assignment.assignmentId}>
                    {assignment.clientName} — started {formatDate(assignment.startDate)}
                  </li>
                ))}
              </ul>
            </>
          )}
        </Alert>
      )}

      <Card title="Profile">
        <FormGrid>
          <FormField label="First Name" required>
            {(id) => (
              <input
                id={id}
                value={values.firstName}
                onChange={(event) => set('firstName', event.target.value)}
                className={fieldControlClass}
              />
            )}
          </FormField>

          <FormField label="Last Name" required>
            {(id) => (
              <input
                id={id}
                value={values.lastName}
                onChange={(event) => set('lastName', event.target.value)}
                className={fieldControlClass}
              />
            )}
          </FormField>

          <FormField label="Hire Date" required>
            {(id) => (
              <input
                id={id}
                // A native date input, which is the mockups' "Calendar control" hint without shipping a
                // date-picker component — and it yields the ISO form the API's `date` column wants.
                type="date"
                value={values.hireDate}
                onChange={(event) => set('hireDate', event.target.value)}
                className={fieldControlClass}
              />
            )}
          </FormField>

          <FormField label="Email Address" required hint="Must be unique.">
            {(id) => (
              <input
                id={id}
                type="email"
                value={values.email}
                onChange={(event) => set('email', event.target.value)}
                className={fieldControlClass}
              />
            )}
          </FormField>

          <FormField label="Employee Type" required>
            {(id) => (
              <select
                id={id}
                value={values.employeeTypeId}
                onChange={(event) => set('employeeTypeId', event.target.value)}
                className={fieldControlClass}
              >
                <option value="">Select a type</option>
                {activeTypes.map((type) => (
                  <option key={type.id} value={String(type.id)}>
                    {type.typeName}
                  </option>
                ))}
                {/* The stored type when it is no longer active. FR-007: retiring a lookup must not rewrite
                    the records using it, so an edit to some OTHER field must not silently reclassify this
                    EDJEr by dropping a value the active-only list omits. */}
                {isEditing && retiredSelection(values.employeeTypeId, activeTypes) && (
                  <option value={values.employeeTypeId}>
                    {retiredLabel(storedTypeName, 'Current type')}
                  </option>
                )}
              </select>
            )}
          </FormField>

          <FormField label="Coach">
            {(id) => (
              <select
                id={id}
                value={values.coachEmployeeId}
                onChange={(event) => set('coachEmployeeId', event.target.value)}
                className={fieldControlClass}
              >
                {/* Optional means selectable-as-nothing, not merely un-asterisked. */}
                <option value="">No coach</option>
                {/* An EDJEr cannot coach themselves. This filter is a CONVENIENCE — it keeps a choice
                    that would be refused out of the list — and `CompassEmployeeService` enforces the
                    same rule, answering 400 to a caller that bypasses this form. FR-041: client-side
                    validation is never the control. Review of PR #232 found the filter here with no
                    server-side counterpart, which is the one arrangement worse than either alone. */}
                {coachOptions
                  .filter(
                    (candidate) =>
                      candidate.id !== edjerId &&
                      // Former EDJErs are not offered (#400). The exception is the coach ALREADY
                      // assigned to this record: dropping it would leave the select with no matching
                      // option, so the browser would show "No coach" while `values.coachEmployeeId`
                      // still held the old id and submitted it. Retained and labelled instead, and
                      // still unreachable as a NEW assignment -- the escape hatch matches only the
                      // current value, so choosing anyone else drops it on the same render.
                      (candidate.isActive || String(candidate.id) === values.coachEmployeeId),
                  )
                  .map((candidate) => (
                    <option key={candidate.id} value={String(candidate.id)}>
                      {candidate.firstName} {candidate.lastName}
                      {candidate.isActive ? '' : ' (Former)'}
                    </option>
                  ))}
              </select>
            )}
          </FormField>

          <FormField label="State of Residence" required hint="US states only.">
            {(id) => (
              <select
                id={id}
                value={values.stateOfResidence}
                onChange={(event) => set('stateOfResidence', event.target.value)}
                className={fieldControlClass}
              >
                <option value="">Select a state</option>
                {US_STATES.map((state) => (
                  <option key={state.code} value={state.code}>
                    {state.name}
                  </option>
                ))}
              </select>
            )}
          </FormField>

          {/* FR-8.1 / FR-8.1b. SIX options and no seventh: the list is `US_TIMEZONES`, not a
              worldwide IANA enumeration and not the browser's zone list. The label reads "Eastern";
              the value submitted is `America/New_York`, which is what the column stores and what OOTO
              does date arithmetic with.

              No retained-unrecognised-value option, unlike the two selects above — those exist because
              a lookup can be retired out from under a record, and FR-8.1b forbids the equivalent here
              outright. A record carrying a zone outside the six renders unchosen, and the server
              refuses the save until one is picked. */}
          <FormField label="Timezone" required>
            {(id) => (
              <select
                id={id}
                value={values.timezone}
                onChange={(event) => set('timezone', event.target.value)}
                className={fieldControlClass}
              >
                <option value="">Select a timezone</option>
                {US_TIMEZONES.map((zone) => (
                  <option key={zone.id} value={zone.id}>
                    {zone.label}
                  </option>
                ))}
              </select>
            )}
          </FormField>

          {/* In Profile, not in Time Tracking Settings. That card's three flags are Super-Admin-only
              and absent from any lesser tier's payload; this one is published to every tier, so
              grouping it there would file it under an entitlement it does not share (FR-004). */}
          <Toggle
            label="Delivery Team"
            checked={values.isDeliveryTeam}
            onChange={(next) => set('isDeliveryTeam', next)}
          />

          <Toggle
            label="Status"
            checked={values.isActive}
            onChange={(next) => set('isActive', next)}
            hint="Termination dates are managed in HiBob, not here."
          />
        </FormGrid>
      </Card>

      {/* BR-1 makes these Super-Admin-only. Every route serving this screen already requires that policy,
          and the flags are absent from the DTO any lesser role can reach — so the grouping here is for the
          administrator's benefit, not the enforcement. */}
      <Card title="Time Tracking Settings">
        <div className="flex flex-wrap gap-6">
          <Toggle
            label="Timesheet Required"
            checked={values.timesheetRequired}
            onChange={(next) => set('timesheetRequired', next)}
          />
          <Toggle
            label="Can Submit < 40 Hours"
            checked={values.canSubmitUnder40}
            onChange={(next) => set('canSubmitUnder40', next)}
          />
          <Toggle
            label="Include in Payroll"
            checked={values.includeInPayroll}
            onChange={(next) => set('includeInPayroll', next)}
          />
        </div>
      </Card>

      {/* EDIT only: an EDJEr being added has no id to read a history for, and no history to read.

          BELOW Time Tracking Settings and ABOVE the actions (owner request 2026-08-18). It used to sit
          after the actions, which put a read-only panel between the Save button and the end of the
          form — so the form's own controls did not read as the last thing on the page, and on a long
          record the actions were stranded mid-screen. It stays inside the `form` element: it carries
          links, never form controls, so it cannot interfere with submission, and taking it out would
          break the single-column flow the cards are laid out in. */}
      {isEditing && <EdjerAssignmentHistory edjerId={edjerId} />}

      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={save.isPending}>
          Save EDJEr
        </Button>
      </div>
    </form>
  );

  function set<TKey extends keyof EdjerFormValues>(key: TKey, value: EdjerFormValues[TKey]) {
    setValues((current) => ({ ...current, [key]: value }));
  }
}

function LoadFailure({ message }: { message: string }) {
  return <Alert>{message}</Alert>;
}

/** Maps a loaded record onto the form's value shape — the two selects become strings. */
function toValues(loaded: CompassEdjer): EdjerFormValues {
  return {
    firstName: loaded.firstName,
    lastName: loaded.lastName,
    hireDate: loaded.hireDate,
    email: loaded.email,
    employeeTypeId: String(loaded.employeeTypeId),
    coachEmployeeId: loaded.coachEmployeeId === null ? '' : String(loaded.coachEmployeeId),
    stateOfResidence: loaded.stateOfResidence,
    // A stored zone outside the six matches no option, so the select shows the placeholder and the
    // administrator must choose. Deliberate: FR-8.1b allows no seventh option to retain it in.
    timezone: loaded.timezone,
    isDeliveryTeam: loaded.isDeliveryTeam,
    isActive: loaded.isActive,
    timesheetRequired: loaded.timesheetRequired,
    canSubmitUnder40: loaded.canSubmitUnder40,
    includeInPayroll: loaded.includeInPayroll,
  };
}

/**
 * Converts the form's strings into the request's numbers.
 *
 * A `<select>`'s value is always a string, so this is where `"1"` becomes `1`. Sending the string is a
 * 400 that names a field the administrator filled in correctly, which is why it has its own test.
 */
function toRequest(values: EdjerFormValues): CompassEdjerRequest {
  return {
    firstName: values.firstName,
    lastName: values.lastName,
    hireDate: values.hireDate,
    email: values.email,
    employeeTypeId: Number(values.employeeTypeId),
    coachEmployeeId: values.coachEmployeeId === '' ? null : Number(values.coachEmployeeId),
    stateOfResidence: values.stateOfResidence,
    // Sent even when blank, and that is the point: the server reads a present-but-empty timezone as
    // "the form was submitted with nothing chosen" and refuses it, where OMITTING the field means
    // "this caller predates FR-8.1" and is quietly defaulted. Dropping the empty string here would
    // turn the required field back into an optional one that silently files people under Eastern.
    timezone: values.timezone,
    isDeliveryTeam: values.isDeliveryTeam,
    isActive: values.isActive,
    timesheetRequired: values.timesheetRequired,
    canSubmitUnder40: values.canSubmitUnder40,
    includeInPayroll: values.includeInPayroll,
  };
}

/**
 * A collection read's payload, or an empty list when it is not one.
 *
 * The transports cast their JSON rather than validating it, so a 200 carrying the wrong shape reaches a
 * `.map` and crashes the render — no error boundary, no error state, just a blank screen. This is not a
 * theoretical shape: the accessibility suite's fetch double answers every request with a single object,
 * and that is what surfaced it.
 *
 * Empty is the right degradation for a SELECT's options: the field renders with nothing to choose, which
 * is visible and recoverable. It would be the wrong degradation for a list SCREEN, which is why
 * `EdjerListPage` distinguishes `failed` from an empty collection instead.
 */
function asArray<T>(value: T[]): T[] {
  return Array.isArray(value) ? value : [];
}

/**
 * Whether the selected employee type is one the active-only list does not offer.
 *
 * True means the EDJEr carries a retired classification, which the select has to keep as an option or the
 * next save of any unrelated field would silently reclassify them.
 */
function retiredSelection(selected: string, active: { id: number }[]): boolean {
  return selected !== '' && !active.some((type) => String(type.id) === selected);
}

/**
 * Labels the retained-but-retired option, naming the value when the server could resolve it.
 *
 * Issue #277. The active-only lookup list cannot supply this name — that list is exactly what the
 * retired value is missing from — so it comes from the record's own payload. The generic fallback
 * stays for the case where no name arrives: the option must exist regardless, because dropping it
 * would let the next save of any unrelated field silently clear a value nobody touched (FR-007).
 *
 * Local rather than shared, matching `retiredSelection` above, which this file and `ClientFormPage`
 * already keep their own copies of. Two occurrences; extract all of them together if a third form
 * needs them.
 */
function retiredLabel(name: string | null | undefined, fallbackNoun: string): string {
  const trimmed = name?.trim();
  return trimmed
    ? `${trimmed} (retired, no longer offered)`
    : `${fallbackNoun} (retired, no longer offered)`;
}

/**
 * Reads the route's EDJEr id, failing CLOSED.
 *
 * Validated rather than trusted, the way `lib/employee-id.ts` validates its own: the value is interpolated
 * into a request path, and a non-numeric segment should render the add form rather than produce a request
 * for `/admin/edjers/NaN`.
 */
function readEdjerId(raw: string | undefined): number | null {
  if (raw === undefined || !/^[0-9]{1,9}$/.test(raw)) {
    return null;
  }

  return Number(raw);
}
