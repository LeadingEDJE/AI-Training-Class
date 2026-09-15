import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

const SKILLS = '/api/compass/v1/admin/skills';

const { SkillAdminPage } = await import('../../../../src/features/skills/SkillAdminPage');

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SkillAdminPage />
    </QueryClientProvider>,
  );
}

function stubSkills(): FetchByUrlStub {
  return stubFetchByUrl({
    [`GET ${SKILLS}`]: {
      status: 200,
      body: [
        { id: 1, name: 'Java', isActive: true },
        { id: 2, name: 'COBOL', isActive: false },
      ],
    },
  });
}

describe('SkillAdminPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('names the page', async () => {
    stubSkills();
    renderPage();

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Skill Administration' }),
    ).toBeInTheDocument();
  });

  it('lists every skill with its active state', async () => {
    stubSkills();
    renderPage();

    expect(await screen.findByRole('cell', { name: 'Java' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'COBOL' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /java.*active/i })).toBeChecked();
    expect(screen.getByRole('switch', { name: /cobol.*retired/i })).not.toBeChecked();
  });

  it('adds a skill and shows it in the list', async () => {
    const stub = stubSkills();
    stub.setResponse(`POST ${SKILLS}`, {
      status: 201,
      body: { id: 9, name: 'Rust', isActive: true },
    });
    renderPage();
    await screen.findByRole('cell', { name: 'Java' });

    await userEvent.click(screen.getByRole('button', { name: '+ Add Skill' }));
    await userEvent.type(screen.getByLabelText(/new skill name/i), 'Rust');

    stub.setResponse(`GET ${SKILLS}`, {
      status: 200,
      body: [
        { id: 1, name: 'Java', isActive: true },
        { id: 2, name: 'COBOL', isActive: false },
        { id: 9, name: 'Rust', isActive: true },
      ],
    });
    await userEvent.click(screen.getByRole('button', { name: /^save new skill$/i }));

    await waitFor(() => expect(screen.getByRole('cell', { name: 'Rust' })).toBeInTheDocument());
    expect(stub.bodiesFor(`POST ${SKILLS}`)).toContainEqual({ name: 'Rust' });
  });

  it('surfaces a duplicate-name rejection inline', async () => {
    const stub = stubSkills();
    stub.setResponse(`POST ${SKILLS}`, {
      status: 409,
      body: { message: "A skill named 'Java' already exists." },
    });
    renderPage();
    await screen.findByRole('cell', { name: 'Java' });

    await userEvent.click(screen.getByRole('button', { name: '+ Add Skill' }));
    await userEvent.type(screen.getByLabelText(/new skill name/i), 'Java');
    await userEvent.click(screen.getByRole('button', { name: /^save new skill$/i }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/already exists/i),
    );
  });

  it('retires a skill without renaming it', async () => {
    const stub = stubSkills();
    stub.setResponse(`PUT ${SKILLS}`, { status: 200, body: { id: 1, name: 'Java', isActive: false } });
    renderPage();
    await screen.findByRole('cell', { name: 'Java' });

    await userEvent.click(screen.getByRole('switch', { name: /java/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${SKILLS}/1`)).toContainEqual({ name: 'Java', isActive: false }),
    );
  });

  it('renames a skill', async () => {
    const stub = stubSkills();
    stub.setResponse(`PUT ${SKILLS}`, { status: 200, body: { id: 1, name: 'Java 21', isActive: true } });
    renderPage();
    await screen.findByRole('cell', { name: 'Java' });

    await userEvent.click(screen.getByRole('button', { name: /edit java/i }));
    const nameField = screen.getByLabelText(/name for java/i);
    await userEvent.clear(nameField);
    await userEvent.type(nameField, 'Java 21');
    await userEvent.click(screen.getByRole('button', { name: /^save java$/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${SKILLS}/1`)).toContainEqual({ name: 'Java 21', isActive: true }),
    );
  });

  it('reports a failed load rather than rendering an empty list as if it were the truth', async () => {
    stubFetchByUrl({ [`GET ${SKILLS}`]: { status: 500 } });
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/could not be loaded/i),
    );
  });

  it('reports a refusal when the caller lacks the Compass root role', async () => {
    stubFetchByUrl({ [`GET ${SKILLS}`]: { status: 403 } });
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/permission/i),
    );
  });

  it('says so plainly when there are no skills yet', async () => {
    stubFetchByUrl({ [`GET ${SKILLS}`]: { status: 200, body: [] } });
    renderPage();

    await waitFor(() => expect(screen.getByText(/no skills yet/i)).toBeInTheDocument());
  });
});
