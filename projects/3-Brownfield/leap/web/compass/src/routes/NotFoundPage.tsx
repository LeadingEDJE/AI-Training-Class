import { buttonClassName } from '../components/ui-classes';

/**
 * Rendered for any path under `/compass/` the router does not match.
 *
 * Currently unrouted: Team Directory, Client Directory, Sales Dashboard, and Reports. A `CompassNav`
 * link to any of those lands here until its screen ships.
 */
export function NotFoundPage() {
  return (
    <main className="min-h-dvh px-6 py-12 text-brand-text">
      <div className="mx-auto max-w-2xl">
        <h1 className="text-3xl font-semibold tracking-tight">Page Not Found</h1>
        <p className="mt-2 text-sm text-brand-gray-muted">
          This Compass area does not exist yet. Use the navigation above, or return to the Compass
          home page.
        </p>
        <a
          href="/compass/"
          className={`mt-6 inline-block ${buttonClassName({ variant: 'secondary' })}`}
        >
          Back to Compass
        </a>
      </div>
    </main>
  );
}
