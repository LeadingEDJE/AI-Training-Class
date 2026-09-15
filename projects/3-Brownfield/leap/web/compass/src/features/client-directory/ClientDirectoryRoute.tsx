import { useState } from 'react';
import { ClientDirectoryPage } from './ClientDirectoryPage';
import { useClientDirectory } from './useClientDirectory';

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
      // The table derives `aria-sort` from these. Omitting them degrades cleanly to "no sort
      // announced" — the page shows no default column and no direction, so no header states an
      // ordering the server is not using. Mirrors TeamDirectoryRoute.
      sort={sort}
      descending={desc}
      onSearchChange={setSearch}
      onSortChange={(column) => {
        setDesc(column === sort ? !desc : false);
        setSort(column);
      }}
    />
  );
}
