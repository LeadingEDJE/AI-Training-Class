import { useState } from 'react';
import { useNavigate, useParams } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, FormField, FormGrid, PageHeader, Table, Toggle } from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';
import { ClientAssignmentHistory } from './ClientAssignmentHistory';
import { fetchLookups, lookupQueryKey } from '../lookups/lookup-api';
import {
  addBillableTimeCategory,
  clientQueryKey,
  clientsQueryKey,
  createClient,
  fetchClient,
  updateBillableTimeCategory,
  updateClient,
  type BillableTimeCategory,
  type CompassClient,
  type CompassClientRequest,
} from './client-api';

const LIST_ROUTE = '/admin/clients';

/**
 * The values the form holds while it is being edited.
 *
 * The cadence is a string, because that is what a `<select>` yields — converted to a number (or null)
 * on submit, which is where the API's `int?` matters. The two dates are strings for the same reason a
 * native date input yields one, and become `null` when blank rather than `""`.
 *
 * **There is no status field, and there is no place for one.** A client's Active/Inactive is derived
 * from its assignments (FR-021, FR-035). The server refuses a request carrying one outright, so a value
 * here would turn every save into a 400 — the type is the first place that rule is enforced.
 */
interface ClientFormValues {
  clientName: string;
  msaSignedDate: string;
  ndaSignedDate: string;
  isInternal: boolean;
  invoiceFrequencyTypeId: string;
}

const BLANK: ClientFormValues = {
  clientName: '',
  msaSignedDate: '',
  ndaSignedDate: '',
  isInternal: false,
  invoiceFrequencyTypeId: '',
};

/**
 * Client configuration — add and update (issue #60, AC-21, AC-22, AC-23).
 *
 * **The most important thing about this screen is what it does not have.** There is no active/inactive
 * control, for any role, in either mode, and no client-deactivation action anywhere (FR-021, AC-42). A
 * client's status is DERIVED from its assignments and is never stored or set; a control that appeared to
 * set it would be lying about what it does. It also never disables anything: a client with zero
 * assignments — which every brand-new client is — stays fully editable and fully assignable (FR-022,
 * FR-037).
 *
 * **Client-side validation is for the administrator's benefit and is never the control** (FR-041). The
 * server refuses a retired invoice frequency, a duplicate client name, and a duplicate category name on
 * this client regardless of what this form allows — which is why the rejection paths matter more here
 * than the `required` attributes.
 *
 * Structured as a waiting parent plus an inner form for the same reason `EdjerFormPage` is: a controlled
 * form cannot adopt values from a query that resolves after first render without an effect that calls
 * `setState`, which `react-hooks/set-state-in-effect` rejects — correctly, since it silently overwrites
 * anything typed in the meantime.
 */
export function ClientFormPage() {
  const params = useParams({ strict: false }) as { clientId?: string };
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const clientId = readClientId(params.clientId);
  const isEditing = clientId !== null;

  /**
   * EVERY cadence, active and retired — filtered to active for the select below.
   *
   * **Why not `activeOnly`, which this API supports (issue #277).** Only active cadences may be OFFERED
   * (FR-023), and that rule is enforced by the filter a few lines down. But a client whose default has
   * since been retired still has to NAME it (FR-007), and the active-only list is precisely the list
   * that value is missing from — so asking for it here left the form with an id it could not label, and
   * it fell back to a generic "Current cadence (retired, no longer offered)". Reading the whole
   * collection keeps the name resolvable without denormalising it onto the record's own payload.
   */
  const frequencyTypes = useQuery({
    queryKey: [...lookupQueryKey('invoice-frequency-types'), 'all'],
    queryFn: () => fetchLookups('invoice-frequency-types'),
  });

  const record = useQuery({
    queryKey: clientQueryKey(clientId ?? 0),
    queryFn: () => fetchClient(clientId ?? 0),
    enabled: isEditing,
  });

  // `Array.isArray` rather than a bare cast — see EdjerFormPage's `asArray` for the failure this
  // prevents (a 200 carrying a non-list reaches `.map` and blanks the screen with no error state).
  const allTypes = asArray(
    frequencyTypes.data?.kind === 'loaded' ? frequencyTypes.data.values : [],
  );
  /** Only active cadences may be OFFERED (FR-023). The retired ones are read for naming only. */
  const activeTypes = allTypes.filter((type) => type.isActive);

  if (isEditing && record.data?.kind === 'refused') {
    return <LoadFailure message="You do not have permission to manage client records." />;
  }

  if (isEditing && record.data?.kind === 'failed') {
    return <LoadFailure message="That client could not be loaded. Try again." />;
  }

  const loaded = record.data?.kind === 'loaded' ? record.data.value : null;

  if (isEditing && loaded === null) {
    return (
      <p role="status" className="text-sm text-brand-gray-muted">
        Loading client…
      </p>
    );
  }

  return (
    <ClientForm
      // Remounting on identity change is what resets the fields — React's documented answer to "reset
      // state when an input changes", and the reason this component holds no values of its own.
      key={clientId ?? 'new'}
      clientId={clientId}
      initialValues={loaded === null ? BLANK : toValues(loaded)}
      categories={loaded?.billableTimeCategories ?? []}
      storedFrequencyName={
        allTypes.find((type) => type.id === loaded?.invoiceFrequencyTypeId)?.typeName ?? null
      }
      activeTypes={activeTypes}
      // Takes the id from the caller rather than closing over `clientId`, which is nullable here. The
      // category manager only exists once the client does, so it always has a real one — and a
      // `clientId ?? 0` fallback would be an unreachable branch pretending to be a safe default.
      onCategoriesChanged={async (changedClientId) => {
        await queryClient.invalidateQueries({ queryKey: clientQueryKey(changedClientId) });
      }}
      onSaved={async () => {
        await queryClient.invalidateQueries({ queryKey: clientsQueryKey() });
        void navigate({ to: LIST_ROUTE });
      }}
      onCancel={() => void navigate({ to: LIST_ROUTE })}
    />
  );
}

interface ClientFormProps {
  /** The record being edited, or null when adding. */
  clientId: number | null;
  initialValues: ClientFormValues;
  categories: BillableTimeCategory[];
  /**
   * The stored cadence's name, resolved from the FULL lookup list (issue #277).
   *
   * Separate from `activeTypes` because that list is filtered to active values, so a retired cadence is
   * by definition absent from it — resolving the name there would always miss the one case this exists
   * for. Null when no name can be resolved, which is the generic-label fallback.
   */
  storedFrequencyName: string | null;
  activeTypes: { id: number; typeName: string }[];
  onCategoriesChanged: (clientId: number) => Promise<void>;
  onSaved: () => Promise<void>;
  onCancel: () => void;
}

function ClientForm({
  clientId,
  initialValues,
  categories,
  storedFrequencyName,
  activeTypes,
  onCategoriesChanged,
  onSaved,
  onCancel,
}: ClientFormProps) {
  const isEditing = clientId !== null;

  const [values, setValues] = useState<ClientFormValues>(initialValues);
  const [writeError, setWriteError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: (request: CompassClientRequest) =>
      isEditing ? updateClient(clientId, request) : createClient(request),
  });

  async function submit() {
    const result = await save.mutateAsync(toRequest(values));

    if (result.kind === 'rejected') {
      // The values stay exactly as they were: a rejection that clears the form makes the administrator
      // retype work the server has already seen.
      setWriteError(result.message);
      return;
    }

    setWriteError(null);
    await onSaved();
  }

  return (
    <div className="flex flex-col gap-6">
      <form
        className="flex flex-col gap-6"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <PageHeader title={isEditing ? 'Edit Client' : 'Add Client'} />

        {writeError !== null && (
          <div
            role="alert"
            className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
          >
            {writeError}
          </div>
        )}

        <Card title="Client Details">
          <FormGrid>
            <FormField label="Client Name" required hint="Must be unique.">
              {(id) => (
                <input
                  id={id}
                  value={values.clientName}
                  onChange={(event) => set('clientName', event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </FormField>

            <FormField label="MSA Signed Date">
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={values.msaSignedDate}
                  onChange={(event) => set('msaSignedDate', event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </FormField>

            <FormField label="NDA Signed Date">
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={values.ndaSignedDate}
                  onChange={(event) => set('ndaSignedDate', event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </FormField>

            <FormField label="Invoice Frequency Default">
              {(id) => (
                <select
                  id={id}
                  value={values.invoiceFrequencyTypeId}
                  onChange={(event) => set('invoiceFrequencyTypeId', event.target.value)}
                  className={fieldControlClass}
                >
                  {/* Optional means selectable-as-nothing. Without this option the field could not
                      express "this client has no default", which AC-22 makes an ordinary state. */}
                  <option value="">No default</option>
                  {activeTypes.map((type) => (
                    <option key={type.id} value={String(type.id)}>
                      {type.typeName}
                    </option>
                  ))}
                  {/* The stored cadence when it is no longer active. FR-007: retiring a lookup must not
                      rewrite the records using it, so an edit to some OTHER field must not silently
                      clear this client's default by dropping a value the active-only list omits. */}
                  {isEditing && retiredSelection(values.invoiceFrequencyTypeId, activeTypes) && (
                    <option value={values.invoiceFrequencyTypeId}>
                      {retiredLabel(storedFrequencyName, 'Current cadence')}
                    </option>
                  )}
                </select>
              )}
            </FormField>

            {/* The "beach" flag. Note this is the ONLY toggle on the screen — there is deliberately no
                status toggle beside it, which is the difference from the EDJEr form worth noticing. */}
            <Toggle
              label="Internal EDJE Client"
              checked={values.isInternal}
              onChange={(next) => set('isInternal', next)}
            />
          </FormGrid>
        </Card>

        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={save.isPending}>
            Save Client
          </Button>
        </div>
      </form>

      {/* Only once the client exists: a category is addressed under its client, so there is nowhere to
          put one before the client has an id. Adding the client first and its categories second is also
          the order the server's routes describe. */}
      {isEditing && (
        <CategoryManager
          clientId={clientId}
          categories={categories}
          onChanged={onCategoriesChanged}
        />
      )}

      {/* AC-24's history panel — issue #224, mockup `CC-3`. EDIT only: a client being added has no id
          to read a history for, and no history to read.

          Placed LAST, which is `CC-3`'s own order — the mockup puts the full-width history card below
          the two-column Client Details / Time Tracking Settings row. Its categories table belongs to
          `CC-2` inside that row rather than to a card of its own, so `CategoryManager` precedes it. */}
      {isEditing && <ClientAssignmentHistory clientId={clientId} />}
    </div>
  );

  function set<TKey extends keyof ClientFormValues>(key: TKey, value: ClientFormValues[TKey]) {
    setValues((current) => ({ ...current, [key]: value }));
  }
}

interface CategoryManagerProps {
  clientId: number;
  categories: BillableTimeCategory[];
  onChanged: (clientId: number) => Promise<void>;
}

/**
 * The client's billable time categories, managed inline (AC-23).
 *
 * **Retired categories stay listed.** An administrator who cannot see a retired category cannot
 * reactivate one, and the row is never removed in any case — timesheet records reference it
 * (Principle VIII), which is why the only retirement is a `PUT` with the flag off.
 *
 * **Laid out like the Lookup admin tables, and that resolves an old objection rather than ignoring it
 * (issue #471).** This card used to carry an "Offered" / "Retired" pill beside a Deactivate button, and
 * the words were chosen deliberately: a category's flag is STORED and set by hand, whereas a client's
 * status is DERIVED from its assignments and cannot be set at all, so reusing "Active" / "Inactive"
 * risked putting two unrelated meanings of the same word on adjacent screens — one an administrator can
 * toggle, one they cannot.
 *
 * That objection is now moot, because **no per-row state word is rendered at all.** The word "Active"
 * appears once, as a COLUMN HEADER, exactly as it does in `LookupSection`; each row carries a bare
 * switch whose position and track colour convey the state. There is nothing left on this screen for a
 * reader to confuse with the client list's derived Status column.
 *
 * **State still reaches assistive technology as a word**, through the switch's accessible name, so
 * `AC-NFR-5` holds: the state is never colour alone. The word there is "Active" / "Inactive" (owner
 * decision, 2026-09-02) rather than `LookupSection`'s "Active" / "Retired" — a deliberate divergence,
 * so do not "fix" it by reaching for that constant.
 *
 * **The switch is named for its category, and that is load-bearing.** The rows were previously
 * `<li aria-label={categoryName}>` so a screen-reader user moving through otherwise identical rows knew
 * which control belonged to which category. A table row does not name a control by itself, so that job
 * moved onto the switch via `label` + `labelHidden` — the same shape `LookupSection` uses.
 */
/**
 * What each row's switch reports to assistive technology.
 *
 * Deliberately NOT `LookupSection`'s `{ on: 'Active', off: 'Retired' }` — owner decision on #471. The
 * visible layout matches those sections; this word does not.
 */
const CATEGORY_STATE_LABELS = { on: 'Active', off: 'Inactive' } as const;

/** Header row, mirroring `LookupSection`'s `['Type', 'Active', 'Actions']`. */
const CATEGORY_COLUMNS = ['Category', 'Active'];

function CategoryManager({ clientId, categories, onChanged }: CategoryManagerProps) {
  const [newName, setNewName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const add = useMutation({
    mutationFn: (categoryName: string) => addBillableTimeCategory(clientId, categoryName),
  });

  const toggle = useMutation({
    mutationFn: (category: BillableTimeCategory) =>
      updateBillableTimeCategory(clientId, category.id, category.categoryName, !category.isActive),
  });

  async function submitNew() {
    const result = await add.mutateAsync(newName);

    if (result.kind === 'rejected') {
      setError(result.message);
      return;
    }

    setError(null);
    setNewName('');
    await onChanged(clientId);
  }

  async function submitToggle(category: BillableTimeCategory) {
    const result = await toggle.mutateAsync(category);

    if (result.kind === 'rejected') {
      setError(result.message);
      return;
    }

    setError(null);
    await onChanged(clientId);
  }

  return (
    <Card title="Billable Time Categories">
      <div className="flex flex-col gap-4">
        {error !== null && (
          <div
            role="alert"
            className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
          >
            {error}
          </div>
        )}

        {categories.length > 0 && (
          <Table caption="Billable Time Categories" columns={CATEGORY_COLUMNS}>
            {categories.map((category) => (
              <tr key={category.id} className="border-t border-brand-gray/40">
                <td className="px-3 py-2 font-medium">{category.categoryName}</td>
                <td className="px-3 py-2">
                  {/* `label` + `labelHidden` names the CONTROL for its category; the visible name is
                      the cell to its left, so showing it twice would only add noise. `stateLabelHidden`
                      keeps the state word out of sight and in the accessible name — the mockup's bare
                      switch, same as the Lookup sections. */}
                  <Toggle
                    label={category.categoryName}
                    labelHidden
                    stateLabels={CATEGORY_STATE_LABELS}
                    stateLabelHidden
                    checked={category.isActive}
                    disabled={toggle.isPending}
                    onChange={() => void submitToggle(category)}
                  />
                </td>
              </tr>
            ))}
          </Table>
        )}

        {/* Not a nested <form>: this card sits beside the client form, and nesting forms is invalid
            HTML — the inner one is dropped and its submit fires the outer, saving the client instead of
            adding the category. */}
        <div className="flex flex-wrap items-end gap-3">
          <FormField label="Category Name">
            {(id) => (
              <input
                id={id}
                value={newName}
                onChange={(event) => setNewName(event.target.value)}
                className={fieldControlClass}
              />
            )}
          </FormField>

          <Button
            variant="secondary"
            size="small"
            disabled={add.isPending}
            onClick={() => void submitNew()}
          >
            + Add Category
          </Button>
        </div>
      </div>
    </Card>
  );
}

function LoadFailure({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
    >
      {message}
    </p>
  );
}

/** Maps a loaded record onto the form's value shape — nulls become the empty string a control wants. */
function toValues(loaded: CompassClient): ClientFormValues {
  return {
    clientName: loaded.clientName,
    msaSignedDate: loaded.msaSignedDate ?? '',
    ndaSignedDate: loaded.ndaSignedDate ?? '',
    isInternal: loaded.isInternal,
    invoiceFrequencyTypeId:
      loaded.invoiceFrequencyTypeId === null ? '' : String(loaded.invoiceFrequencyTypeId),
  };
}

/**
 * Converts the form's strings into the request's numbers and nulls.
 *
 * Blank means ABSENT, not empty: `""` for a date would be a 400 against a nullable `date` column, and
 * `Number("")` is 0 — a cadence id that does not exist. Both are rejections that name a field the
 * administrator left blank on purpose.
 */
function toRequest(values: ClientFormValues): CompassClientRequest {
  return {
    clientName: values.clientName,
    msaSignedDate: values.msaSignedDate === '' ? null : values.msaSignedDate,
    ndaSignedDate: values.ndaSignedDate === '' ? null : values.ndaSignedDate,
    isInternal: values.isInternal,
    invoiceFrequencyTypeId:
      values.invoiceFrequencyTypeId === '' ? null : Number(values.invoiceFrequencyTypeId),
  };
}

/** A collection read's payload, or an empty list when it is not one. See `EdjerFormPage.asArray`. */
function asArray<T>(value: T[]): T[] {
  return Array.isArray(value) ? value : [];
}

/**
 * Whether the selected cadence is one the active-only list does not offer.
 *
 * True means the client carries a retired default, which the select has to keep as an option or the
 * next save of any unrelated field would silently clear it.
 */
function retiredSelection(selected: string, active: { id: number }[]): boolean {
  return selected !== '' && !active.some((type) => String(type.id) === selected);
}

/**
 * Labels the retained-but-retired option, naming the cadence when the server could resolve it.
 *
 * Issue #277. The active-only lookup list cannot supply this name — that list is exactly what the
 * retired value is missing from — so it comes from the record's own payload. The generic fallback
 * stays for the case where no name arrives: the option must exist regardless, because dropping it
 * would let the next save of any unrelated field silently clear a default nobody touched (FR-007).
 *
 * Local rather than shared, matching `retiredSelection` above, which this file and `EdjerFormPage`
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
 * Reads the route's client id, failing CLOSED.
 *
 * Validated rather than trusted: the value is interpolated into a request path, and a non-numeric
 * segment should render the add form rather than produce a request for `/admin/clients/NaN`.
 */
function readClientId(raw: string | undefined): number | null {
  if (raw === undefined || !/^[0-9]{1,9}$/.test(raw)) {
    return null;
  }

  return Number(raw);
}
