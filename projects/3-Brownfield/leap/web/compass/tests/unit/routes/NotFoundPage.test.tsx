import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { NotFoundPage } from '../../../src/routes/NotFoundPage';

describe('NotFoundPage', () => {
  it('says the area does not exist rather than rendering another page', () => {
    // Before the router, an unmatched path silently rendered the index page. Several CompassNav
    // destinations still land here on purpose — they are later streams — so the page has to be
    // honest about it.
    render(<NotFoundPage />);

    expect(screen.getByRole('heading', { name: 'Page Not Found' })).toBeInTheDocument();
    expect(screen.getByText(/does not exist yet/i)).toBeInTheDocument();
  });

  it('offers a way back to the Compass home page', () => {
    render(<NotFoundPage />);

    expect(screen.getByRole('link', { name: /back to compass/i })).toHaveAttribute(
      'href',
      '/compass/',
    );
  });
});
