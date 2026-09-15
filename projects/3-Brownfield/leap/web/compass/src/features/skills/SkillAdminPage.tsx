import { SkillSection } from './SkillSection';
import { PageHeader } from '../../components/ui';

/**
 * Skill administration — the master skill list a Super Admin manages, mirroring
 * `LookupAdminPage`'s admin-config nav gate and audit-trail coverage.
 */
export function SkillAdminPage() {
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title="Skill Administration" />
      <SkillSection />
    </div>
  );
}
