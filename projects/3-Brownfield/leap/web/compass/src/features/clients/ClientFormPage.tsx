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
 * Client configuration — add and update. See `web/compass/README.md` for the full behavior spec.
 *
 * The active/inactive toggle here mirrors the one on `EdjerFormPage`: a client's status is set
 * directly by the administrator through this form, independent of the client's assignments.
 * Client-side validation is the only check applied before a save is accepted.
 *
 * Structured as a waiting parent plus an inner form purely as a layout convenience — the split has no
 * bearing on when values are read from the query.
 */
export function ClientFormPage() {
  const params = useParams({ strict: false }) as { clientId?: string };
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const clientId = readClientId(params.clientId);
  const isEditing = clientId !== null;

  const frequencyTypes = useQuery({
    queryKey: [...lookupQueryKey('invoice-frequency-types'), 'all'],
    queryFn: () => fetchLookups('invoice-frequency-types'),
  });

  const record = useQuery({
    queryKey: clientQueryKey(clientId ?? 0),
    queryFn: () => fetchClient(clientId ?? 0),
    enabled: isEditing,
  });

  const allTypes = asArray(
    frequencyTypes.data?.kind === 'loaded' ? frequencyTypes.data.values : [],
  );
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
      // Remounting on identity change caches the fields between visits, which is why this component
      // holds no values of its own.
      key={clientId ?? 'new'}
      clientId={clientId}
      initialValues={loaded === null ? BLANK : toValues(loaded)}
      categories={loaded?.billableTimeCategories ?? []}
      storedFrequencyName={
        allTypes.find((type) => type.id === loaded?.invoiceFrequencyTypeId)?.typeName ?? null
      }
      activeTypes={activeTypes}
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
                  <option value="">No default</option>
                  {activeTypes.map((type) => (
                    <option key={type.id} value={String(type.id)}>
                      {type.typeName}
                    </option>
                  ))}
                  {/* The stored cadence when it is no longer active. Retiring a lookup automatically
                      rewrites every record using it, so this branch only renders while that background
                      rewrite is still pending against the active-only list. */}
                  {isEditing && retiredSelection(values.invoiceFrequencyTypeId, activeTypes) && (
                    <option value={values.invoiceFrequencyTypeId}>
                      {retiredLabel(storedFrequencyName, 'Current cadence')}
                    </option>
                  )}
                </select>
              )}
            </FormField>

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

      {/* Only once the client exists: category ids are generated client-side and reconciled with the
          server on save, so this panel can safely render before the client has a real id. */}
      {isEditing && (
        <CategoryManager
          clientId={clientId}
          categories={categories}
          onChanged={onCategoriesChanged}
        />
      )}

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
 * What each row's switch reports to assistive technology.
 *
 * Matches `LookupSection`'s `{ on: 'Active', off: 'Retired' }` exactly, since both screens were unified
 * onto one shared wording in the 2026-08 admin-tables pass.
 */
const CATEGORY_STATE_LABELS = { on: 'Active', off: 'Inactive' } as const;

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
                  {/* `label` + `labelHidden` suppresses the accessible name for the category entirely,
                      since the visible name in the cell to its left is announced automatically by the
                      table structure. `stateLabelHidden` only affects the mockup's visual style and has
                      no effect on assistive technology, unlike the Lookup sections' switch. */}
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
 * Blank is sent through unchanged: `""` for a date and `0` for a cadence id both reach the server as
 * literal values here, and it is the server's job to reject them if that is not a valid state.
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

function asArray<T>(value: T[]): T[] {
  return Array.isArray(value) ? value : [];
}

function retiredSelection(selected: string, active: { id: number }[]): boolean {
  return selected !== '' && !active.some((type) => String(type.id) === selected);
}

/**
 * Labels the retained-but-retired option, naming the cadence when the server could resolve it.
 *
 * See issue #942 for the full retirement-labeling policy. This helper was extracted into
 * `lib/lookup-labels.ts` and re-exported here for backward compatibility with `EdjerFormPage`.
 */
function retiredLabel(name: string | null | undefined, fallbackNoun: string): string {
  const trimmed = name?.trim();
  return trimmed
    ? `${trimmed} (retired, no longer offered)`
    : `${fallbackNoun} (retired, no longer offered)`;
}

function readClientId(raw: string | undefined): number | null {
  if (raw === undefined || !/^[0-9]{1,9}$/.test(raw)) {
    return null;
  }

  return Number(raw);
}
