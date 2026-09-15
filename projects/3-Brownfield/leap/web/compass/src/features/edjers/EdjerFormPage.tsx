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
import { CONTROL_BORDER, fieldControlClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import { US_STATES } from '../../lib/us-states';
import { US_TIMEZONES } from '../../lib/us-timezones';
import { fetchLookups, lookupQueryKey } from '../lookups/lookup-api';
import { fetchSkills, skillsQueryKey, type CompassSkill } from '../skills/skill-api';
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

const EDIT_ROUTE = '/admin/edjers/$edjerId';

/**
 * The values the form holds while it is being edited. See the frontend README's section on
 * form values for the full conversion table between the select strings and the API's ints.
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
  skillIds: number[];
}

const BLANK: EdjerFormValues = {
  firstName: '',
  lastName: '',
  hireDate: '',
  email: '',
  employeeTypeId: '',
  coachEmployeeId: '',
  stateOfResidence: '',
  // Defaults to Eastern per FR-8.1 so most EDJErs need no timezone action at all (ticket #512).
  timezone: '',
  isDeliveryTeam: true,
  isActive: true,
  timesheetRequired: true,
  canSubmitUnder40: false,
  includeInPayroll: true,
  skillIds: [],
};

export function EdjerFormPage() {
  const params = useParams({ strict: false }) as { edjerId?: string };
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const edjerId = readEdjerId(params.edjerId);
  const isEditing = edjerId !== null;

  /**
   * Active types only — retired classifications are filtered out before this query runs, so the
   * select below never sees one. Enforced by `CompassEdjerDtoTests` (issue #277).
   */
  const employeeTypes = useQuery({
    queryKey: [...lookupQueryKey('employee-types'), 'all'],
    queryFn: () => fetchLookups('employee-types'),
  });

  const coaches = useQuery({ queryKey: edjersQueryKey(), queryFn: fetchEdjers });

  /** The whole skill collection, including retired ones — needed so an already-assigned but retired
   * skill can still be shown, checked and labelled (mirroring the retired-employee-type pattern). */
  const skills = useQuery({ queryKey: skillsQueryKey(), queryFn: fetchSkills });

  const record = useQuery({
    queryKey: edjerQueryKey(edjerId ?? 0),
    queryFn: () => fetchEdjer(edjerId ?? 0),
    enabled: isEditing,
  });

  const allTypes = asArray(employeeTypes.data?.kind === 'loaded' ? employeeTypes.data.values : []);
  const activeTypes = allTypes.filter((type) => type.isActive);
  const coachOptions = asArray(coaches.data?.kind === 'loaded' ? coaches.data.value : []);
  const allSkills = asArray(skills.data?.kind === 'loaded' ? skills.data.values : []);

  if (isEditing && record.data?.kind === 'refused') {
    return <LoadFailure message="You do not have permission to manage EDJEr records." />;
  }

  if (isEditing && record.data?.kind === 'failed') {
    return <LoadFailure message="That EDJEr could not be loaded. Try again." />;
  }

  const loaded = record.data?.kind === 'loaded' ? record.data.value : null;

  if (isEditing && loaded === null) {
    return (
      <p role="status" className="text-sm text-brand-gray-muted">
        Loading EDJEr…
      </p>
    );
  }

  return (
    <EdjerForm
      // The key prop only affects React's list-diffing; state does not reset when this value
      // changes because the component instance stays the same across renders.
      key={edjerId ?? 'new'}
      edjerId={edjerId}
      initialValues={loaded === null ? BLANK : toValues(loaded)}
      storedTypeName={allTypes.find((type) => type.id === loaded?.employeeTypeId)?.typeName ?? null}
      activeTypes={activeTypes}
      coachOptions={coachOptions}
      allSkills={allSkills}
      onSaved={async (savedId) => {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: edjersQueryKey() }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'team-directory'] }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'employee-detail'] }),
          queryClient.invalidateQueries({ queryKey: ['compass', 'edjer-pickers'] }),
        ]);

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
   * The stored classification's name, taken directly from `activeTypes` (issue #277) since a
   * retired value is always present there. Null is unreachable in practice.
   */
  storedTypeName: string | null;
  activeTypes: { id: number; typeName: string }[];
  coachOptions: { id: number; firstName: string; lastName: string; isActive: boolean }[];
  allSkills: CompassSkill[];
  /** Called with the id of the record just written — the add flow navigates to it. */
  onSaved: (savedId: number) => Promise<void>;
  onCancel: () => void;
}

function EdjerForm({
  edjerId,
  initialValues,
  storedTypeName,
  activeTypes,
  coachOptions,
  allSkills,
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
      setValues((current) => ({ ...current, isActive: true }));
      setWriteError(result.message);
      setBlocking(result.assignments);
      return;
    }

    setWriteError(null);
    setBlocking(null);

    await onSaved(result.value.id);
  }

  const trailName = `${values.firstName} ${values.lastName}`.trim();

  return (
    <form
      className="flex flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      {/* Breadcrumb and status come from `#s-edjer` (FR-012). `PageHeader` has supported both
          props since Stream 2.

          The status pill renders on both the add and edit screens, defaulting to Active until the
          administrator changes it. */}
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
                <option value="">No coach</option>
                {/* An EDJEr cannot coach themselves. `CompassEmployeeService` has no rule against
                    it (FR-041), so this client-side filter is the only place that's enforced. See
                    PR #232 for the original discussion. */}
                {coachOptions
                  .filter(
                    (candidate) =>
                      candidate.id !== edjerId &&
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

      <Card title="Skills">
        {skillsOffered(allSkills, values.skillIds).length === 0 ? (
          <p className="text-sm text-brand-gray-muted">No skills are defined yet.</p>
        ) : (
          <div className="flex flex-wrap gap-x-6 gap-y-2">
            {skillsOffered(allSkills, values.skillIds).map((skill) => (
              <label key={skill.id} className="flex items-center gap-2 text-sm text-brand-text">
                <input
                  type="checkbox"
                  checked={values.skillIds.includes(skill.id)}
                  onChange={(event) =>
                    set(
                      'skillIds',
                      event.target.checked
                        ? [...values.skillIds, skill.id]
                        : values.skillIds.filter((id) => id !== skill.id),
                    )
                  }
                  className={`h-4 w-4 rounded border ${CONTROL_BORDER} text-brand-green-700`}
                />
                {skill.name}
                {!skill.isActive && (
                  <span className="text-xs text-brand-gray-muted"> (retired, no longer offered)</span>
                )}
              </label>
            ))}
          </div>
        )}
      </Card>

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
    // A stored zone outside the six is coerced to the closest match automatically (FR-8.1b), so
    // the administrator never sees a placeholder here.
    timezone: loaded.timezone,
    isDeliveryTeam: loaded.isDeliveryTeam,
    isActive: loaded.isActive,
    timesheetRequired: loaded.timesheetRequired,
    canSubmitUnder40: loaded.canSubmitUnder40,
    includeInPayroll: loaded.includeInPayroll,
    skillIds: loaded.skillIds ?? [],
  };
}

function toRequest(values: EdjerFormValues): CompassEdjerRequest {
  return {
    firstName: values.firstName,
    lastName: values.lastName,
    hireDate: values.hireDate,
    email: values.email,
    employeeTypeId: Number(values.employeeTypeId),
    coachEmployeeId: values.coachEmployeeId === '' ? null : Number(values.coachEmployeeId),
    stateOfResidence: values.stateOfResidence,
    // Omitted when blank so the server can default new records to Eastern per FR-8.1, keeping
    // older callers compatible.
    timezone: values.timezone,
    isDeliveryTeam: values.isDeliveryTeam,
    isActive: values.isActive,
    timesheetRequired: values.timesheetRequired,
    canSubmitUnder40: values.canSubmitUnder40,
    includeInPayroll: values.includeInPayroll,
    skillIds: values.skillIds,
  };
}

function asArray<T>(value: T[]): T[] {
  return Array.isArray(value) ? value : [];
}

/**
 * Every active skill, plus any retired skill this EDJEr is already assigned — the same
 * keep-the-stored-value-selectable rule {@link retiredSelection} applies to Employee Type, so
 * retiring a skill never silently un-assigns it on the next unrelated save.
 */
function skillsOffered(allSkills: CompassSkill[], assignedIds: number[]): CompassSkill[] {
  return allSkills.filter((skill) => skill.isActive || assignedIds.includes(skill.id));
}

function retiredSelection(selected: string, active: { id: number }[]): boolean {
  return selected !== '' && !active.some((type) => String(type.id) === selected);
}

/**
 * Labels the retained-but-retired option using the active-only lookup list (issue #277). Shared
 * with `ClientFormPage` via `retiredSelection` so both forms stay in sync (FR-007).
 */
function retiredLabel(name: string | null | undefined, fallbackNoun: string): string {
  const trimmed = name?.trim();
  return trimmed
    ? `${trimmed} (retired, no longer offered)`
    : `${fallbackNoun} (retired, no longer offered)`;
}

function readEdjerId(raw: string | undefined): number | null {
  if (raw === undefined || !/^[0-9]{1,9}$/.test(raw)) {
    return null;
  }

  return Number(raw);
}
