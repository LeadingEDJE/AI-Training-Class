import { useState } from 'react';
import { ClientDirectoryPage } from './ClientDirectoryPage';
import { useClientDirectory } from './useClientDirectory';

/** Connects the Client Directory screen to the read surface. */
export function ClientDirectoryRoute() {
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState('');
  const [desc, setDesc] = useState(false);

  const { data, isPending, isError } = useClientDirectory({ search, sort, desc });

  return (
    <ClientDirectoryPage
      rows={data ?? []}
      isPending={isPending}
      isError={isError}
      // The table derives `aria-sort` from these, so they must travel with the query that actually
      // ordered the rows. Omitting them does not degrade to "no sort announced" — the page falls back
      // to its default column and reports ascending, so every header states an ordering the server is
      // not using. Mirrors TeamDirectoryRoute.
      sort={sort}
      descending={desc}
      onSearchChange={setSearch}
      onSortChange={(column) => {
        // Selecting the active column flips direction; selecting another switches to it ascending.
        setDesc(column === sort ? !desc : false);
        setSort(column);
      }}
    />
  );
}
