import { useId, useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Button, Card, Table, Toggle } from '../../components/ui';
import { fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import {
  createLookup,
  fetchLookups,
  lookupQueryKey,
  updateLookup,
  type CompassLookup,
  type LookupKind,
  type LookupWrite,
} from './lookup-api';

/**
 * The mockups' lookup table: the value, its state, and the row's actions.
 *
 * The third column reads "Actions" where the mockup leaves its `<th>` empty. An unnamed column header is
 * announced as nothing, so a screen-reader user navigating the table has no idea what the last cell is
 * for — the same reason the EDJEr list names its own action column.
 */
const COLUMNS = ['Type', 'Active', 'Actions'];

/**
 * What the Active column's switch reports in each state, to assistive technology.
 *
 * "Active"/"Retired" rather than the switch's mechanical On/Off, because that is what a lookup value's
 * state is called everywhere else in Compass.
 *
 * **Not rendered on screen since 2026-08-21** (owner request): the mockup's `.tswitch` carries no word,
 * and matching it is what these sections do. `stateLabelHidden` keeps the word in the accessible name —
 * a screen-reader user still hears "Full Time, Active" — so what changed is only what a sighted viewer
 * sees, which is the knob's position and the track's colour.
 */
const STATE_LABELS = { on: 'Active', off: 'Retired' } as const;

interface LookupSectionProps {
  kind: LookupKind;
  /** The plural heading, e.g. "Employee Types". Also the region's accessible name. */
  title: string;
  /**
   * The singular noun used in control labels, e.g. "Employee Type".
   *
   * **Title Case, because it is a control LABEL** (owner request 2026-08-18). Prose that embeds it
   * mid-sentence lowercases it at the point of use rather than taking a second prop — one noun, two
   * renderings, and a second prop would let the two drift apart.
   */
  singular: string;
}

/**
 * Administration of one Compass lookup: list, add, rename, retire and reinstate.
 *
 * Rendered once per lookup rather than written twice. The two lookups are structurally identical and
 * the acceptance criteria describe one screen covering both (AC-25, AC-26) — but they are separate
 * resources, so each instance is bound to its own `kind` and its own query key. Wiring both sections to
 * one endpoint is the mistake a shared service makes easy, and
 * `LookupAdminPage.test.tsx` asserts against it.
 *
 * **Built on the shared primitives since the mockup port.** It shipped as a `<ul>` of rows with outlined
 * buttons, which predated `components/ui.tsx` — the EDJEr screens introduced those primitives and this
 * screen sat one click away in the same admin shell looking like a different product. The information and
 * every action are unchanged by that port; what changed is the markup and where it comes from.
 *
 * **Carries no "offered when …" note (issue #408, 2026-08-26).** The mockup's bottom `.note` — "Only
 * active types appear when {classifying an EDJEr / setting a client's invoicing default}. Retiring one
 * leaves records already using it unchanged." — was an owner-requested removal: it told an administrator
 * nothing the screen itself, and each row's own Active/Retired state, did not already. `usedFor` existed
 * only to fill that sentence's clause and was removed with it.
 */
export function LookupSection({ kind, title, singular }: LookupSectionProps) {
  const addFieldId = useId();
  const queryClient = useQueryClient();
  const [editingId, setEditingId] = useState<number | null>(null);
  const [writeError, setWriteError] = useState<string | null>(null);
  /**
   * Whether the add row is showing.
   *
   * The mockup puts Add in the card heading, so the field is revealed rather than permanent. It closes on
   * a successful save and on cancel — but NOT on a rejection, which is the rule the rename path already
   * follows: closing it there would discard what was typed at the moment the user needs to fix it.
   */
  const [isAdding, setIsAdding] = useState(false);

  const { data, isPending } = useQuery({
    queryKey: lookupQueryKey(kind),
    // The administration screen asks for EVERY value, retired ones included: a retired value that
    // cannot be seen can never be reinstated.
    queryFn: () => fetchLookups(kind, false),
  });

  /** Applies a write's outcome: surface the rejection, or clear the error and refresh. */
  async function settle(result: LookupWrite): Promise<boolean> {
    if (result.kind === 'rejected') {
      setWriteError(result.message);
      return false;
    }

    setWriteError(null);
    await queryClient.invalidateQueries({ queryKey: lookupQueryKey(kind) });
    return true;
  }

  const addMutation = useMutation({
    mutationFn: (typeName: string) => createLookup(kind, typeName),
  });

  const editMutation = useMutation({
    mutationFn: (variables: { id: number; typeName: string; isActive: boolean }) =>
      updateLookup(kind, variables.id, variables.typeName, variables.isActive),
  });

  const addForm = useForm({
    defaultValues: { typeName: '' },
    onSubmit: async ({ value }) => {
      const result = await addMutation.mutateAsync(value.typeName);
      if (await settle(result)) {
        addForm.reset();
        setIsAdding(false);
      }
    },
  });

  async function saveRow(row: CompassLookup, typeName: string, isActive: boolean) {
    const result = await editMutation.mutateAsync({ id: row.id, typeName, isActive });
    if (await settle(result)) {
      setEditingId(null);
    }
  }

  const values = data?.kind === 'loaded' ? data.values : [];

  return (
    <Card
      title={title}
      action={
        <Button
          size="small"
          disabled={isAdding}
          onClick={() => {
            setWriteError(null);
            setIsAdding(true);
          }}
        >
          + Add Type
        </Button>
      }
    >
      {/* One error slot per section, so an assistive technology user is not hunting among alerts. */}
      {writeError !== null && (
        <div className="mb-3">
          <Alert>{writeError}</Alert>
        </div>
      )}

      {data?.kind === 'refused' && (
        <div className="mb-3">
          <Alert>You do not have permission to manage these values.</Alert>
        </div>
      )}

      {data?.kind === 'failed' && (
        <div className="mb-3">
          <Alert>These values could not be loaded. Try again.</Alert>
        </div>
      )}

      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading {title.toLowerCase()}…
        </p>
      )}

      {data?.kind === 'loaded' && (
        <Table
          caption={title}
          columns={COLUMNS}
          emptyMessage="No values yet. Add the first one below."
        >
          {values.map((row) => (
            <LookupRow
              key={row.id}
              row={row}
              isEditing={editingId === row.id}
              onEdit={() => {
                setWriteError(null);
                setEditingId(row.id);
              }}
              onCancel={() => setEditingId(null)}
              onSave={(typeName) => saveRow(row, typeName, row.isActive)}
              onToggleActive={() => saveRow(row, row.typeName, !row.isActive)}
            />
          ))}
        </Table>
      )}

      {isAdding && (
        <form
          className="mt-4 flex flex-wrap items-end gap-3 border-t border-brand-taupe/40 pt-4"
          onSubmit={(event) => {
            event.preventDefault();
            void addForm.handleSubmit();
          }}
        >
          <addForm.Field name="typeName">
            {(field) => (
              <span className="flex flex-col gap-1">
                <label htmlFor={addFieldId} className="text-sm font-medium text-brand-gray">
                  New {singular} Name
                </label>
                {/* autoFocus: the button that revealed this row is in the card heading, so without it a
                    keyboard user has to travel back down to the field they just asked for. Justified here
                    precisely because the field appeared in response to their own action. */}
                <input
                  id={addFieldId}
                  name={field.name}
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={`${fieldControlClass} max-w-xs`}
                  autoFocus
                />
              </span>
            )}
          </addForm.Field>
          <Button type="submit">
            <ActionLabel visible="Save" full={`Save New ${singular}`} />
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              addForm.reset();
              setWriteError(null);
              setIsAdding(false);
            }}
          >
            <ActionLabel visible="Cancel" full={`Cancel Adding ${article(singular)} ${singular}`} />
          </Button>
        </form>
      )}
    </Card>
  );
}

/**
 * "a" or "an" for a singular noun.
 *
 * A deliberately small helper with a deliberately narrow job. PR #213 shipped "configuring a an EDJEr" by
 * concatenating a hard-coded article onto a display string, and the fix elsewhere was to stop composing
 * sentences that way at all. Here a control label genuinely needs the article, so the choice is computed
 * from the word rather than assumed — and `LookupAdminPage.test.tsx` carries a general doubled-article
 * guard that fails on the whole screen if this is ever wrong.
 */
function article(noun: string): string {
  return /^[aeiou]/i.test(noun) ? 'an' : 'a';
}

/**
 * A control's label where the mockup shows one word and assistive technology needs the whole phrase.
 *
 * The mockup's row action is a bare "Edit" and its buttons are short; ours have to say WHICH value they
 * act on. Both spans render, and exactly one reaches each audience: the visible word is `aria-hidden`,
 * the full phrase is `sr-only`.
 *
 * **Two distinct ambiguities, and the second is easy to miss** — a reviewer of PR #326 read the per-row
 * case as the only one and concluded the add form's buttons had nothing to disambiguate:
 *
 * 1. **Per row.** A column of ten identical "Edit" controls is indistinguishable to a screen-reader
 *    user who does not have the row in view.
 * 2. **Per section.** `isAdding` is state inside `LookupSection` and `LookupAdminPage` renders TWO of
 *    them, so both add forms can be open AT THE SAME TIME — putting two "Save" and two "Cancel"
 *    buttons on screen together. The full names are what tell them apart ("Save New Employee Type" vs
 *    "Save New Invoice Frequency Type"), exactly as the two "+ Add Type" buttons rely on their region.
 *
 * `LookupAdminPage.test.tsx` pins both: the visible/full split per control, and all four add-form
 * controls being distinguishable with both forms open.
 *
 * **Two spans rather than a visible word plus a hidden remainder**, which is what this was written as
 * first. `Edit<span class="sr-only"> Full Time</span>` computes to the accessible name **"EditFull
 * Time"** — the accessible-name algorithm trims each text run before joining, so a space sitting at the
 * boundary between two runs is discarded. Ten tests caught it. Keeping the full phrase in ONE text node
 * removes the join, and the name is then exactly what it says here.
 *
 * **Why this and not `aria-label`, which `Toggle` uses for the identical trimming problem.** The two are
 * not interchangeable, and an earlier version of this comment wrongly claimed `Toggle` had made the same
 * choice as this one:
 *
 * - `Toggle` shows BOTH of its runs by default — the setting's name and its state word, side by side.
 *   Neither can hold the whole phrase, so there is nothing for the two-span form to do and the name has
 *   to be stated separately.
 * - Here the two runs carry DIFFERENT text: a short visible word and a long hidden phrase. One span can
 *   hold the entire phrase in one text node, so no `aria-label` is needed.
 *
 * The reason to prefer that when it IS available: the phrase stays in `document.body.textContent`, where
 * `LookupAdminPage.test.tsx`'s screen-wide doubled-article guard can see it. An `aria-label` moves it
 * into an attribute the guard does not read, and this file composes labels from {@link article}.
 *
 * WCAG 2.5.3 (Label in Name) holds as long as `visible` is a leading, contiguous part of `full` — every
 * call site is "Edit"/"Save"/"Cancel" followed by the rest of its own sentence, so a voice-control user
 * saying "click Save" still matches the control.
 */
function ActionLabel({ visible, full }: { visible: string; full: string }) {
  return (
    <>
      <span aria-hidden="true">{visible}</span>
      <span className="sr-only">{full}</span>
    </>
  );
}

interface LookupRowProps {
  row: CompassLookup;
  isEditing: boolean;
  onEdit: () => void;
  onCancel: () => void;
  onSave: (typeName: string) => void;
  onToggleActive: () => void;
}

/**
 * One lookup value, as a table row.
 *
 * Selectable state is rendered as the WORD "Active" or "Retired", never as colour alone (AC-NFR-5).
 * Every control names the value it acts on, so a screen-reader user hearing "Retire" in a list of ten
 * rows knows which one.
 *
 * **The row states only whether THIS value is offered, never where.** The "where" clause used to be
 * stated once by the card's own footnote (removed, issue #408) — a row restating it announced the
 * clause once on entry and again per row.
 *
 * It also means the row composes no sentence from a display string at all. The clause used to be
 * assembled here by comparing the singular noun against a literal and prefixing an article, which
 * rendered "configuring a an EDJEr" — flagged in review of PR #213. The fix then was to reuse a phrase
 * that was already grammatical; the phrase now lives one level up, and this row interpolates only a
 * value's own name.
 */
function LookupRow({ row, isEditing, onEdit, onCancel, onSave, onToggleActive }: LookupRowProps) {
  const fieldId = useId();
  const [draftName, setDraftName] = useState(row.typeName);

  if (isEditing) {
    return (
      <tr>
        {/* The editor replaces the value cell rather than the whole row, so the state and actions stay
            where the eye and the screen reader already expect them. */}
        <td className="px-3 py-2">
          <span className="flex flex-col gap-1">
            <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
              Name for {row.typeName}
            </label>
            <input
              id={fieldId}
              value={draftName}
              onChange={(event) => setDraftName(event.target.value)}
              className={`${fieldControlClass} max-w-xs`}
            />
          </span>
        </td>
        <td className="px-3 py-2">
          {/* Disabled mid-rename: retiring and renaming are one PUT each, and letting both fire while a
              draft name sits unsaved would make the outcome depend on which the user pressed last. */}
          <Toggle
            label={row.typeName}
            labelHidden
            stateLabels={STATE_LABELS}
            stateLabelHidden
            checked={row.isActive}
            onChange={onToggleActive}
            disabled
          />
        </td>
        <td className="px-3 py-2">
          <span className="flex flex-wrap gap-2">
            <Button size="small" onClick={() => onSave(draftName)}>
              <ActionLabel visible="Save" full={`Save ${row.typeName}`} />
            </Button>
            <Button
              size="small"
              variant="secondary"
              onClick={() => {
                setDraftName(row.typeName);
                onCancel();
              }}
            >
              <ActionLabel visible="Cancel" full={`Cancel Editing ${row.typeName}`} />
            </Button>
          </span>
        </td>
      </tr>
    );
  }

  return (
    // The mockup's `tr:hover td{background:#fafbf7}` — a barely-there green-tinted white that tracks
    // the eye across a three-column row. `brand-green/5` composites to rgb(249,251,245) on white,
    // which is that value to within a point per channel, so it reuses the palette rather than
    // hard-coding a hex the token files would not know about.
    <tr className="hover:bg-brand-green/5">
      <td className="px-3 py-2 font-medium">{row.typeName}</td>
      <td className="px-3 py-2">
        {/* The mockup's inline toggle, bare — no word beside it. It REPLACES the separate
            Retire/Reinstate button, so a click here retires or reinstates immediately, which is why the
            label has to name the value: the row is the only other thing saying which one, and a
            screen-reader user does not have the row in view. The `sr-only` sentence below is what tells
            them WHICH state that is in words. */}
        <Toggle
          label={row.typeName}
          labelHidden
          stateLabels={STATE_LABELS}
          stateLabelHidden
          checked={row.isActive}
          onChange={onToggleActive}
        />
        {/* The row states its OWN state, not where the lookup is offered — that clause used to live in
            the card's footnote (removed, issue #408), and repeating it per row announced it once on
            entry and again on every row — eleven times on a ten-value lookup. */}
        <span className="sr-only">
          {row.isActive
            ? `${row.typeName} is offered on new records.`
            : `${row.typeName} is retired and no longer offered.`}
        </span>
      </td>
      <td className="px-3 py-2">
        {/* The mockup's `a.link` Edit, as an actual control. It stays a `<button>` — it opens an inline
            editor rather than navigating, and a link that goes nowhere is the wrong element however it
            looks — but it wears the mockup's text-link appearance instead of the outlined button it
            shipped as. Two rows of "Edit Full Time" chips is what made the Actions column the widest in
            the card.

            Appearance is `tableLinkClass` (owner request 2026-08-21), not the `brand-blue-accent` run
            this shipped with: that was accessible but was not what every other Edit in a Compass table
            looked like. `rounded` stays for the focus ring. Underlined either way, so being actionable
            is not signalled by colour alone (§1.4.1). */}
        <button type="button" onClick={onEdit} className={`${tableLinkClass} rounded`}>
          <ActionLabel visible="Edit" full={`Edit ${row.typeName}`} />
        </button>
      </td>
    </tr>
  );
}
